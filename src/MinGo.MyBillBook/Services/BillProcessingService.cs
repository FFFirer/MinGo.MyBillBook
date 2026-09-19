using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;
using Microsoft.EntityFrameworkCore;

namespace MinGo.MyBillBook.Services;

public class BillProcessingService(AppDbContext db, ICategoryRuleEngine ruleEngine) : IBillProcessingService
{
    public async Task<int> ProcessBatchAsync(int batchId, CancellationToken ct = default)
    {
        var batch = await db.BillImportBatches.FindAsync([batchId], ct)
            ?? throw new InvalidOperationException($"批次 {batchId} 不存在");

        batch.Status = ImportBatchStatus.Processing;
        await db.SaveChangesAsync(ct);

        var rawRecords = await db.BillRawRecords
            .Where(r => r.ImportBatchId == batchId && !r.IsProcessed)
            .ToListAsync(ct);

        int processed = 0;
        foreach (var raw in rawRecords)
        {
            var categoryId = ruleEngine.MatchCategory(raw.Counterparty, raw.ProductName);
            var transactionType = ParseTransactionType(raw.Direction);

            var record = new BillRecord
            {
                RawRecordId = raw.Id,
                PlatformId = raw.PlatformId,
                TransactionDate = raw.TransactionDate,
                Counterparty = raw.Counterparty,
                Merchant = raw.Counterparty,
                CategoryId = categoryId,
                ProductName = raw.ProductName,
                Amount = raw.Amount,
                TransactionType = transactionType,
                Status = raw.Status,
                SourceFile = batch.FileName,
                IsManualAdjusted = false,
                SyncedToDuckDb = false
            };
            db.BillRecords.Add(record);
            raw.IsProcessed = true;
            processed++;
        }

        batch.Status = ImportBatchStatus.Completed;
        await db.SaveChangesAsync(ct);
        return processed;
    }

    public async Task<int> ReprocessBatchAsync(int batchId, CancellationToken ct = default)
    {
        var oldRecords = await db.BillRecords
            .Where(r => r.RawRecord.ImportBatchId == batchId && !r.IsManualAdjusted)
            .ToListAsync(ct);
        db.BillRecords.RemoveRange(oldRecords);

        var rawRecords = await db.BillRawRecords
            .Where(r => r.ImportBatchId == batchId)
            .ToListAsync(ct);
        foreach (var raw in rawRecords)
            raw.IsProcessed = false;

        await db.SaveChangesAsync(ct);
        return await ProcessBatchAsync(batchId, ct);
    }

    public async Task<int> ProcessUnprocessedAsync(CancellationToken ct = default)
    {
        var batches = await db.BillImportBatches
            .Where(b => b.Status == ImportBatchStatus.Imported)
            .ToListAsync(ct);

        int total = 0;
        foreach (var batch in batches)
            total += await ProcessBatchAsync(batch.Id, ct);

        return total;
    }

    private static TransactionType ParseTransactionType(string direction) => direction switch
    {
        "收入" => TransactionType.Income,
        "支出" => TransactionType.Expense,
        "不计收支" => TransactionType.Transfer,
        _ => TransactionType.Expense
    };
}
