using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Data;
using MinGo.MyBillBook.Services.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace MinGo.MyBillBook.Services;

public class BillProcessingService(
    AppDbContext db,
    IPipelineRunner pipelineRunner,
    IEnumerable<IPipelineStep<BillImportContext>> importSteps) : IBillProcessingService
{
    public async Task<int> ProcessBatchAsync(int batchId, CancellationToken ct = default)
    {
        var context = new BillImportContext { ImportBatchId = batchId };
        return await pipelineRunner.RunAsync(context, importSteps, PipelineType.Import, null, ct);
    }

    public async Task<int> ReprocessBatchAsync(int batchId, CancellationToken ct = default)
    {
        var oldRecords = await db.BillRecords
            .Where(r => r.RawRecord.ImportBatchId == batchId && !r.IsManualAdjusted)
            .ToListAsync(ct);
        db.BillRecords.RemoveRange(oldRecords);

        // 删除本批次的 Normalized 层输出，重跑时由 NormalizeStep 重新生成，避免重复。
        var oldNormalized = await db.NormalizedTransactions
            .Where(n => n.RawRecord.ImportBatchId == batchId)
            .ToListAsync(ct);
        db.NormalizedTransactions.RemoveRange(oldNormalized);

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
}
