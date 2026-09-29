using System.Text.Json;
using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Parsing;
using MinGo.MyBillBook.Data;
using Microsoft.EntityFrameworkCore;

namespace MinGo.MyBillBook.Services;

public class BillImportService(AppDbContext db, IBillParserFactory parserFactory) : IBillImportService
{
    public async Task<BillImportResult> ImportAsync(Stream fileStream, string fileName, int platformId, CancellationToken ct = default)
    {
        var parser = parserFactory.GetParser(platformId == 1 ? "ALIPAY" : "WECHAT")
            ?? parserFactory.DetectParser(fileStream, fileName)
            ?? throw new InvalidOperationException("无法识别的文件格式");

        var result = parser.Parse(fileStream, fileName);
        var errors = new List<string>(result.Errors);

        var batch = new BillImportBatch
        {
            PlatformId = platformId,
            FileName = fileName,
            ImportDate = DateTime.Now,
            TotalCount = result.Rows.Count,
            Status = ImportBatchStatus.Imported
        };
        db.BillImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        int success = 0, duplicate = 0, updated = 0, rowNumber = 0;
        foreach (var row in result.Rows)
        {
            rowNumber++;

            var hasOrderNo = !string.IsNullOrEmpty(row.TransactionId);
            var hasPaymentTxId = !string.IsNullOrEmpty(row.PaymentTransactionId);

            // 按 SourceTransactionId / SourcePaymentTransactionId 查找已有 RawRecord。
            BillRawRecord? existingRaw = null;
            if (hasOrderNo)
                existingRaw = await db.BillRawRecords.FirstOrDefaultAsync(
                    r => r.SourceTransactionId == row.TransactionId, ct);
            if (existingRaw is null && hasPaymentTxId)
                existingRaw = await db.BillRawRecords.FirstOrDefaultAsync(
                    r => r.SourcePaymentTransactionId == row.PaymentTransactionId, ct);

            if (existingRaw is not null)
            {
                // 重复导入：更新 RawPayload 及标量字段，重置处理标记，删除下游 BillRecord 让管道重新生成。
                existingRaw.RawPayload = JsonSerializer.Serialize(row);
                existingRaw.SourceTransactionId = row.TransactionId;
                existingRaw.SourcePaymentTransactionId = row.PaymentTransactionId;
                existingRaw.IsProcessed = false;
                existingRaw.DuplicateStatus = DuplicateStatus.Unique;
                existingRaw.DuplicateScore = 0;
                await DeleteBillRecordCascadeAsync(existingRaw.Id, ct);
                updated++;
                continue;
            }

            var raw = new BillRawRecord
            {
                ImportBatchId = batch.Id,
                PlatformId = platformId,
                RowNumber = rowNumber,
                RawPayload = JsonSerializer.Serialize(row),
                SourceTransactionId = row.TransactionId,
                SourcePaymentTransactionId = row.PaymentTransactionId,
                IsProcessed = false
            };
            db.BillRawRecords.Add(raw);
            success++;
        }

        batch.SuccessCount = success;
        batch.DuplicateCount = duplicate;
        await db.SaveChangesAsync(ct);

        return new BillImportResult(batch.Id, fileName, result.Rows.Count, success, duplicate, updated, errors);
    }

    /// <summary>
    /// 删除指定 RawRecord 关联的下游 BillRecord 及其子表数据（分类溯源、标签、去重候选），
    /// 以便管道重新生成时不会产生重复记录。
    /// </summary>
    private async Task DeleteBillRecordCascadeAsync(int rawRecordId, CancellationToken ct)
    {
        var billRecord = await db.BillRecords.FirstOrDefaultAsync(r => r.RawRecordId == rawRecordId, ct);
        if (billRecord is null) return;

        var classifications = await db.ClassificationResults
            .Where(c => c.TransactionId == billRecord.Id).ToListAsync(ct);
        db.ClassificationResults.RemoveRange(classifications);

        var tags = await db.TransactionTags
            .Where(t => t.TransactionId == billRecord.Id).ToListAsync(ct);
        db.TransactionTags.RemoveRange(tags);

        var candidates = await db.DuplicateCandidates
            .Where(d => d.LeftRecordId == billRecord.Id || d.RightRecordId == billRecord.Id)
            .ToListAsync(ct);
        db.DuplicateCandidates.RemoveRange(candidates);

        db.BillRecords.Remove(billRecord);
    }

    public async Task<List<BillImportBatch>> GetBatchesAsync(CancellationToken ct = default)
    {
        return await db.BillImportBatches
            .Include(b => b.Platform)
            .OrderByDescending(b => b.ImportDate)
            .ToListAsync(ct);
    }
}
