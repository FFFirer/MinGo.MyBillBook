using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Parsing;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Data;
using MinGo.MyBillBook.Services.Pipeline;

namespace MinGo.MyBillBook.Services;

/// <summary>
/// 局部重跑服务（设计第 16/20 节）。规则变更后从指定步骤起重算 Canonical 层，无需重导 CSV：
/// 从持久化的 RawPayload / NormalizedTransaction 重建管道上下文，删除并重新生成下游 Canonical 数据，
/// 保留手工调整（IsManualAdjusted）的记录不参与重算。管道运行器会跳过起点之前的步骤（记为 Skipped）。
/// </summary>
public class RebuildService(
    AppDbContext db,
    IPipelineRunner pipelineRunner,
    IMerchantResolver merchantResolver,
    IEnumerable<IPipelineStep<BillImportContext>> importSteps,
    ILogger<RebuildService> logger) : IRebuildService
{
    public async Task<int> RebuildAsync(string? fromStep, int? batchId, CancellationToken ct = default)
    {
        var (startStep, startOrder) = ResolveStart(fromStep);

        var batches = batchId.HasValue
            ? await db.BillImportBatches.Where(b => b.Id == batchId.Value).ToListAsync(ct)
            : await db.BillImportBatches
                .Where(b => b.Status == ImportBatchStatus.Completed)
                .ToListAsync(ct);

        if (batches.Count == 0)
        {
            logger.LogInformation("局部重跑：无匹配批次 (fromStep={FromStep}, batchId={BatchId})", startStep, batchId);
            return 0;
        }

        int total = 0;
        foreach (var batch in batches)
            total += await RebuildBatchAsync(batch, startStep, startOrder, ct);

        // 回填历史 BillRawRecord / BillRecord 缺失的流水号字段（列是后期迁移加入的，早期记录为空字符串）。
        await BackfillTransactionIdsAsync(ct);

        logger.LogInformation("局部重跑完成：from={FromStep}, 批次 {BatchCount} 个, 记录 {Total} 条", startStep, batches.Count, total);
        return total;
    }

    /// <summary>
    /// 从 BillRawRecords 回填 BillRecords 中缺失的 SourceTransactionId / SourcePaymentTransactionId。
    /// 这两个列是后期迁移加入的，历史记录的值为空字符串。
    /// 优先使用 RawRecord 标量字段；若标量字段本身也为空（早期导入），则从 RawPayload JSON 中解析。
    /// 回填后标记 SyncedToDuckDb = false 以触发 DuckDB 同步。
    /// </summary>
    private async Task BackfillTransactionIdsAsync(CancellationToken ct)
    {
        // 第一步：回填 BillRawRecord 自身的标量字段（从 RawPayload JSON 解析）。
        await BackfillRawRecordScalarsAsync(ct);

        // 第二步：从（已更新的）BillRawRecord 回填 BillRecord。
        await BackfillFieldAsync(
            r => r.SourcePaymentTransactionId == "" && r.RawRecordId > 0,
            (r, raw) => r.SourcePaymentTransactionId =
                !string.IsNullOrEmpty(raw.SourcePaymentTransactionId)
                    ? raw.SourcePaymentTransactionId
                    : ParsePaymentTransactionIdFromPayload(raw.RawPayload),
            "SourcePaymentTransactionId", ct);

        await BackfillFieldAsync(
            r => r.SourceTransactionId == "" && r.RawRecordId > 0,
            (r, raw) => r.SourceTransactionId =
                !string.IsNullOrEmpty(raw.SourceTransactionId)
                    ? raw.SourceTransactionId
                    : ParseTransactionIdFromPayload(raw.RawPayload),
            "SourceTransactionId", ct);
    }

    /// <summary>
    /// 回填 BillRawRecord 上缺失的 SourceTransactionId / SourcePaymentTransactionId 标量字段。
    /// 早期导入时这些列尚未创建，标量值为空字符串，但 RawPayload JSON 中包含完整的解析结果。
    /// </summary>
    private async Task BackfillRawRecordScalarsAsync(CancellationToken ct)
    {
        var emptyRawIds = await db.BillRawRecords
            .Where(r => r.SourcePaymentTransactionId == "" || r.SourceTransactionId == "")
            .Select(r => r.Id)
            .ToListAsync(ct);
        if (emptyRawIds.Count == 0) return;

        var batchSize = 500;
        int count = 0;
        for (var i = 0; i < emptyRawIds.Count; i += batchSize)
        {
            var batch = emptyRawIds.Skip(i).Take(batchSize).ToList();
            var raws = await db.BillRawRecords
                .Where(r => batch.Contains(r.Id))
                .ToListAsync(ct);

            foreach (var raw in raws)
            {
                var changed = false;
                if (string.IsNullOrEmpty(raw.SourcePaymentTransactionId))
                {
                    raw.SourcePaymentTransactionId = ParsePaymentTransactionIdFromPayload(raw.RawPayload);
                    changed = true;
                }
                if (string.IsNullOrEmpty(raw.SourceTransactionId))
                {
                    raw.SourceTransactionId = ParseTransactionIdFromPayload(raw.RawPayload);
                    changed = true;
                }
                if (changed) count++;
            }

            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("回填 BillRawRecord 标量字段: {Count} 条", count);
    }

    private static string ParsePaymentTransactionIdFromPayload(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return "";
        var row = JsonSerializer.Deserialize<RawBillRow>(payload);
        return row?.PaymentTransactionId ?? "";
    }

    private static string ParseTransactionIdFromPayload(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return "";
        var row = JsonSerializer.Deserialize<RawBillRow>(payload);
        return row?.TransactionId ?? "";
    }

    private async Task BackfillFieldAsync(
        System.Linq.Expressions.Expression<Func<BillRecord, bool>> predicate,
        Action<BillRecord, BillRawRecord> assign,
        string fieldName,
        CancellationToken ct)
    {
        var ids = await db.BillRecords.Where(predicate).Select(r => r.Id).ToListAsync(ct);
        if (ids.Count == 0) return;

        var batchSize = 500;
        for (var i = 0; i < ids.Count; i += batchSize)
        {
            var batch = ids.Skip(i).Take(batchSize).ToList();
            var rows = await db.BillRecords
                .Where(r => batch.Contains(r.Id))
                .Include(r => r.RawRecord)
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                if (row.RawRecord is not null)
                {
                    assign(row, row.RawRecord);
                    row.SyncedToDuckDb = false;
                }
            }

            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("回填 {Field}: {Count} 条", fieldName, ids.Count);
    }

    /// <summary>
    /// 将请求的起点步骤映射到受支持的重算起点，并返回其 Order。
    /// Canonical 层作为整体经 PublishCanonical 重新生成，故起点收敛到 Normalize / ResolveMerchant / ClassifyCategory 三档。
    /// </summary>
    private static (string Step, int Order) ResolveStart(string? fromStep)
    {
        if (string.IsNullOrWhiteSpace(fromStep))
            return ("Normalize", 10);

        var order = fromStep.Trim().ToLowerInvariant() switch
        {
            "normalizestep" or "normalize" => 10,
            "resolvemerchantstep" or "resolvemerchant" or "merchant" => 15,
            "deduplicatestep" or "deduplicate" => 15,
            "classifycategorystep" or "classifycategory" or "classify" or "category" => 20,
            "detecttransferstep" or "detecttransfer" or "transfer" => 20,
            "applytagsstep" or "applytags" or "tag" or "tags" => 20,
            "publishcanonicalstep" or "publishcanonical" or "publish" => 20,
            _ => throw new InvalidOperationException($"未知的重跑起点步骤：{fromStep}")
        };

        return order switch
        {
            10 => ("Normalize", 10),
            15 => ("ResolveMerchant", 15),
            _ => ("ClassifyCategory", 20)
        };
    }

    private async Task<int> RebuildBatchAsync(BillImportBatch batch, string startStep, int startOrder, CancellationToken ct)
    {
        // 1) 删除本批次的下游 Canonical 数据（保留手工调整记录），避免 PublishCanonical 重新生成时重复。
        var preservedRawIds = await ClearCanonicalAsync(batch.Id, startOrder, ct);

        // 2) 按起点重建管道上下文。
        var context = new BillImportContext
        {
            ImportBatchId = batch.Id,
            Source = "rebuild",
            Batch = batch
        };

        if (startOrder > 10)
        {
            // Normalize 被跳过：从 DB 重建 NormalizedTransactions（排除已保留的手工调整来源）。
            var normalized = await db.NormalizedTransactions
                .AsNoTracking()
                .Where(n => n.RawRecord.ImportBatchId == batch.Id)
                .ToListAsync(ct);
            if (preservedRawIds.Count > 0)
                normalized = normalized.Where(n => !preservedRawIds.Contains(n.RawRecordId)).ToList();
            context.NormalizedTransactions = normalized;

            context.RawRecords = await db.BillRawRecords
                .Where(r => r.ImportBatchId == batch.Id)
                .ToListAsync(ct);

            // 重置本批次 Raw 的处理标记：DeduplicateStep 依据 IsProcessed 做跨批次精确去重，
            // 若不重置，本批次自身已处理的 Raw 会被误判为重复而全部剔除。
            // PublishCanonicalStep 会在重算完成后重新置为 true。
            foreach (var raw in context.RawRecords)
                raw.IsProcessed = false;
            await db.SaveChangesAsync(ct);

            // ClassifyCategory 起点：ResolveMerchant 也被跳过，需重建商户归一化结果（内存派生，不落库）。
            if (startOrder > 15)
            {
                foreach (var tx in normalized)
                {
                    var resolution = await merchantResolver.ResolveAsync(tx.Counterparty, tx.RawDescription, ct);
                    context.MerchantResolutions[tx.Id] = resolution;
                }
            }
        }
        else
        {
            // Normalize 起点：重置原始记录处理标记，交由 NormalizeStep 重新解析。
            var rawRecords = await db.BillRawRecords
                .Where(r => r.ImportBatchId == batch.Id)
                .ToListAsync(ct);
            foreach (var raw in rawRecords)
                raw.IsProcessed = false;
            await db.SaveChangesAsync(ct);
        }

        // 3) 从起点运行管道（起点之前的步骤记为 Skipped）。
        return await pipelineRunner.RunAsync(context, importSteps, PipelineType.Rebuild, startStep, ct);
    }

    /// <summary>
    /// 删除批次的下游 Canonical 数据（BillRecord 及其溯源/标签/转账/去重候选）。
    /// 手工调整的记录保留，返回其对应的 RawRecordId 集合以从重算中排除。
    /// Normalize 起点还会删除 NormalizedTransactions（由 NormalizeStep 重新生成）。
    /// </summary>
    private async Task<HashSet<int>> ClearCanonicalAsync(int batchId, int startOrder, CancellationToken ct)
    {
        var batchRecords = await db.BillRecords
            .Where(r => r.RawRecord.ImportBatchId == batchId)
            .ToListAsync(ct);

        var preservedRawIds = batchRecords
            .Where(r => r.IsManualAdjusted)
            .Select(r => r.RawRecordId)
            .ToHashSet();

        var toDelete = batchRecords.Where(r => !r.IsManualAdjusted).ToList();
        var deleteIds = toDelete.Select(r => r.Id).ToHashSet();

        if (deleteIds.Count > 0)
        {
            // 子表先删，避免外键约束。
            var classifications = await db.ClassificationResults
                .Where(c => deleteIds.Contains(c.TransactionId)).ToListAsync(ct);
            db.ClassificationResults.RemoveRange(classifications);

            var tags = await db.TransactionTags
                .Where(t => deleteIds.Contains(t.TransactionId)).ToListAsync(ct);
            db.TransactionTags.RemoveRange(tags);

            var candidates = await db.DuplicateCandidates
                .Where(d => deleteIds.Contains(d.LeftRecordId) || deleteIds.Contains(d.RightRecordId))
                .ToListAsync(ct);
            db.DuplicateCandidates.RemoveRange(candidates);

            // 转账以 MatchedRecordIds（"from,to"）引用记录，解析后判断是否落在删除集内。
            var transfers = await db.Transfers.ToListAsync(ct);
            var affectedTransfers = transfers.Where(t => MatchedIds(t.MatchedRecordIds).Any(id => deleteIds.Contains(id))).ToList();
            db.Transfers.RemoveRange(affectedTransfers);

            db.BillRecords.RemoveRange(toDelete);
        }

        // Normalize 起点：一并删除标准化层，由 NormalizeStep 重建。
        if (startOrder <= 10)
        {
            var normalized = await db.NormalizedTransactions
                .Where(n => n.RawRecord.ImportBatchId == batchId)
                .ToListAsync(ct);
            db.NormalizedTransactions.RemoveRange(normalized);
        }

        await db.SaveChangesAsync(ct);
        return preservedRawIds;
    }

    private static IEnumerable<int> MatchedIds(string? matched)
    {
        if (string.IsNullOrWhiteSpace(matched)) yield break;
        foreach (var part in matched.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, out var id))
                yield return id;
    }
}
