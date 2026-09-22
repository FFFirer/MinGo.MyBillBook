using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Pipeline.Steps;

/// <summary>
/// 发布步骤：将分类后的 Canonical 记录持久化，回填并落库溯源决策（ClassificationResult），
/// 标记原始记录已处理，并把批次置为完成。
/// </summary>
public class PublishCanonicalStep(AppDbContext db) : IPipelineStep<BillImportContext>
{
    public string Name => "PublishCanonical";
    public int Order => 30;

    public async Task<StepResult> ExecuteAsync(BillImportContext context, CancellationToken ct)
    {
        var records = context.CanonicalRecords;
        if (records.Count > 0)
            db.BillRecords.AddRange(records);

        foreach (var raw in context.RawRecords)
            raw.IsProcessed = true;

        context.Batch.Status = ImportBatchStatus.Completed;
        await db.SaveChangesAsync(ct);

        // BillRecord 已获得 Id，回填溯源决策并落库。
        var classifications = new List<ClassificationResult>(context.PendingClassifications.Count);
        foreach (var (record, result) in context.PendingClassifications)
        {
            result.TransactionId = record.Id;
            classifications.Add(result);
        }
        if (classifications.Count > 0)
        {
            db.ClassificationResults.AddRange(classifications);
            await db.SaveChangesAsync(ct);
        }

        // 回填疑似重复候选：按 RawRecordId 定位新发布的 BillRecord 作为 RightRecord。
        if (context.PendingDuplicates.Count > 0)
        {
            var recordIdByRaw = records
                .Where(r => r.Id != 0)
                .GroupBy(r => r.RawRecordId)
                .ToDictionary(g => g.Key, g => g.First().Id);
            var candidates = new List<DuplicateCandidate>();
            foreach (var (rawRecordId, leftRecordId, score, reason) in context.PendingDuplicates)
            {
                if (leftRecordId <= 0 || !recordIdByRaw.TryGetValue(rawRecordId, out var rightRecordId))
                    continue;
                candidates.Add(new DuplicateCandidate
                {
                    LeftRecordId = leftRecordId,
                    RightRecordId = rightRecordId,
                    Score = score,
                    MatchReason = reason,
                    Status = DuplicateCandidateStatus.Pending,
                    CreatedAt = DateTime.Now
                });
            }
            if (candidates.Count > 0)
            {
                db.DuplicateCandidates.AddRange(candidates);
                await db.SaveChangesAsync(ct);
            }
        }

        // 回填交易标签：不覆盖已存在的标签（尤其是 Source=User 的手工标签）。
        if (context.PendingTags.Count > 0)
        {
            var txIds = context.PendingTags.Select(p => p.Record.Id).Where(id => id != 0).Distinct().ToList();
            var existingKeys = txIds.Count == 0
                ? new HashSet<(int, int)>()
                : (await db.TransactionTags.AsNoTracking()
                    .Where(t => txIds.Contains(t.TransactionId))
                    .Select(t => new { t.TransactionId, t.TagId })
                    .ToListAsync(ct))
                    .Select(t => (t.TransactionId, t.TagId)).ToHashSet();

            var tags = new List<TransactionTag>();
            var seen = new HashSet<(int, int)>();
            foreach (var (record, tagId, source, confidence) in context.PendingTags)
            {
                if (record.Id == 0) continue;
                var key = (record.Id, tagId);
                if (existingKeys.Contains(key) || !seen.Add(key)) continue;
                tags.Add(new TransactionTag
                {
                    TransactionId = record.Id,
                    TagId = tagId,
                    Source = source,
                    Confidence = confidence,
                    CreatedAt = DateTime.Now
                });
            }
            if (tags.Count > 0)
            {
                db.TransactionTags.AddRange(tags);
                await db.SaveChangesAsync(ct);
            }
        }

        // 回填转账：From/To 记录已获得 Id，写入 MatchedRecordIds（转出方,转入方）。
        if (context.PendingTransfers.Count > 0)
        {
            var transfers = new List<Transfer>();
            foreach (var (from, to, amountMinor, occurredAt) in context.PendingTransfers)
            {
                if (from.Id == 0 || to.Id == 0) continue;
                transfers.Add(new Transfer
                {
                    FromAccountId = from.FundAccountId,
                    ToAccountId = to.FundAccountId,
                    AmountMinor = amountMinor,
                    OccurredAt = occurredAt,
                    Status = TransferStatus.Detected,
                    MatchedRecordIds = $"{from.Id},{to.Id}",
                    CreatedAt = DateTime.Now
                });
            }
            if (transfers.Count > 0)
            {
                db.Transfers.AddRange(transfers);
                await db.SaveChangesAsync(ct);
            }
        }

        return new StepResult(records.Count, records.Count);
    }
}
