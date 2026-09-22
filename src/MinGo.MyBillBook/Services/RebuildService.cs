using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
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

        logger.LogInformation("局部重跑完成：from={FromStep}, 批次 {BatchCount} 个, 记录 {Total} 条", startStep, batches.Count, total);
        return total;
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
