using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Parsing;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Pipeline.Steps;

/// <summary>
/// 重新解析步骤：从对象存储中读取原始文件，用当前解析器重新解析，
/// 替换旧的 RawRecord 及其下游数据。仅在 Source == "rebuild" 且 fromStep == "ReParse" 时执行。
/// 保留手工调整（IsManualAdjusted）的 BillRecord 不参与重算。
/// </summary>
public class ReParseStep(
    AppDbContext db,
    IObjectStorage objectStorage,
    IBillParserFactory parserFactory,
    ILogger<ReParseStep> logger) : IPipelineStep<BillImportContext>
{
    public string Name => "ReParse";
    public int Order => 5;

    public async Task<StepResult> ExecuteAsync(BillImportContext context, CancellationToken ct)
    {
        var batch = context.Batch;
        if (string.IsNullOrEmpty(batch.OriginalFileKey))
            throw new InvalidOperationException($"批次 {batch.Id} 没有原始文件，无法重新解析");

        // 1) 从对象存储读取原始文件
        using var stream = await objectStorage.GetAsync(batch.OriginalFileKey, ct);

        // 2) 用当前解析器重新解析
        var parser = parserFactory.GetParser(batch.Platform.Code)
            ?? throw new InvalidOperationException($"找不到平台 {batch.Platform.Code} 的解析器");
        var parseResult = parser.Parse(stream, batch.FileName);

        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
                context.Issues.Add(new PipelineIssue(Name, error, IssueSeverity.Error));
        }

        // 3) 删除本批次旧的 RawRecord 及其下游数据
        var oldRaws = await db.BillRawRecords
            .Where(r => r.ImportBatchId == batch.Id)
            .ToListAsync(ct);
        var oldRawIds = oldRaws.Select(r => r.Id).ToHashSet();

        // 删除 NormalizedTransactions
        var oldNormalized = await db.NormalizedTransactions
            .Where(n => oldRawIds.Contains(n.RawRecordId))
            .ToListAsync(ct);
        db.NormalizedTransactions.RemoveRange(oldNormalized);

        // 删除 BillRecords（保留 IsManualAdjusted）
        var oldBills = await db.BillRecords
            .Where(r => oldRawIds.Contains(r.RawRecordId))
            .ToListAsync(ct);

        var preservedRawIds = new HashSet<int>();
        var toDelete = new List<BillRecord>();
        foreach (var bill in oldBills)
        {
            if (bill.IsManualAdjusted)
            {
                preservedRawIds.Add(bill.RawRecordId);
            }
            else
            {
                toDelete.Add(bill);
            }
        }

        if (toDelete.Count > 0)
        {
            var deleteIds = toDelete.Select(r => r.Id).ToHashSet();

            // 子表先删
            var classifications = await db.ClassificationResults
                .Where(c => deleteIds.Contains(c.TransactionId))
                .ToListAsync(ct);
            db.ClassificationResults.RemoveRange(classifications);

            var tags = await db.TransactionTags
                .Where(t => deleteIds.Contains(t.TransactionId))
                .ToListAsync(ct);
            db.TransactionTags.RemoveRange(tags);

            var candidates = await db.DuplicateCandidates
                .Where(d => deleteIds.Contains(d.LeftRecordId) || deleteIds.Contains(d.RightRecordId))
                .ToListAsync(ct);
            db.DuplicateCandidates.RemoveRange(candidates);

            // 转账以 MatchedRecordIds 引用记录
            var transfers = await db.Transfers.ToListAsync(ct);
            var affectedTransfers = transfers
                .Where(t => MatchedIds(t.MatchedRecordIds).Any(id => deleteIds.Contains(id)))
                .ToList();
            db.Transfers.RemoveRange(affectedTransfers);

            db.BillRecords.RemoveRange(toDelete);
        }

        // 删除旧的 RawRecords（排除被手工调整记录引用的）
        var rawsToDelete = oldRaws.Where(r => !preservedRawIds.Contains(r.Id)).ToList();
        db.BillRawRecords.RemoveRange(rawsToDelete);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "ReParse 批次 {BatchId}: 删除旧 Raw {OldCount} 条（保留 {Preserved} 条），解析新结果 {NewCount} 条",
            batch.Id, oldRaws.Count, preservedRawIds.Count, parseResult.Rows.Count);

        // 4) 用新的解析结果创建 BillRawRecord
        int success = 0, errors = 0, rowNumber = 0;
        var newRaws = new List<BillRawRecord>();
        foreach (var row in parseResult.Rows)
        {
            rowNumber++;
            var raw = new BillRawRecord
            {
                ImportBatchId = batch.Id,
                PlatformId = batch.PlatformId,
                RowNumber = rowNumber,
                RawPayload = JsonSerializer.Serialize(row),
                SourceTransactionId = row.TransactionId,
                SourcePaymentTransactionId = row.PaymentTransactionId,
                IsProcessed = false
            };
            db.BillRawRecords.Add(raw);
            newRaws.Add(raw);
            success++;
        }

        // 更新批次统计
        batch.TotalCount = parseResult.Rows.Count;
        batch.SuccessCount = success;
        await db.SaveChangesAsync(ct);

        // 5) 将新 RawRecords 放入上下文，供后续 Normalize 步骤使用
        context.RawRecords = newRaws;

        return new StepResult(parseResult.Rows.Count, success, errors);
    }

    private static IEnumerable<int> MatchedIds(string? matched)
    {
        if (string.IsNullOrWhiteSpace(matched)) yield break;
        foreach (var part in matched.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, out var id))
                yield return id;
    }
}
