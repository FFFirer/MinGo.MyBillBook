using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Pipeline.Steps;

/// <summary>
/// 去重步骤（Order 介于 ResolveMerchant 与 Classify 之间）：对每条标准化交易做多维评分，
/// 与已存在的 Canonical 记录及同批次记录比对。
/// 评分规则：Exact SourceTransactionId=100；账户+金额+日期=90；账户+金额+商户=80；阈值可配。
/// Score≥重复阈值直接标 Duplicate 并从下游工作集移除（不发布 Canonical，但保留 Raw）；
/// 落在复核区间标 PotentialDuplicate 并暂存候选，由 PublishCanonicalStep 回填落库；否则 Unique。
/// </summary>
public class DeduplicateStep(AppDbContext db) : IPipelineStep<BillImportContext>
{
    public string Name => "Deduplicate";
    public int Order => 18;

    public async Task<StepResult> ExecuteAsync(BillImportContext context, CancellationToken ct)
    {
        var input = context.NormalizedTransactions.Count;
        if (input == 0)
            return new StepResult(0, 0);

        var duplicateThreshold = GetOption(context, "duplicateThreshold", 100.0);
        var reviewThreshold = GetOption(context, "reviewThreshold", 80.0);
        var platformId = context.Batch.PlatformId;

        // 仅按金额预筛已存在的 Canonical 记录，避免全表载入。
        var amounts = context.NormalizedTransactions.Select(t => t.AmountMinor).Distinct().ToList();
        var existing = await db.BillRecords.AsNoTracking()
            .Where(r => amounts.Contains(r.AmountMinor))
            .Select(r => new { r.Id, r.PlatformId, r.AmountMinor, r.TransactionDate, r.MerchantId })
            .ToListAsync(ct);

        // 已处理过的来源交易号（跨批次精确去重的兜底）。
        var sourceIds = context.NormalizedTransactions
            .Select(t => t.SourceTransactionId)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct().ToList();
        var existingSourceIds = sourceIds.Count == 0
            ? new HashSet<string>()
            : (await db.BillRawRecords.AsNoTracking()
                .Where(r => r.IsProcessed && sourceIds.Contains(r.SourceTransactionId))
                .Select(r => r.SourceTransactionId)
                .ToListAsync(ct)).ToHashSet();

        // 已处理过的支付流水号（辅助精确去重）。
        var paymentTxIds = context.NormalizedTransactions
            .Select(t => t.SourcePaymentTransactionId)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct().ToList();
        var existingPaymentTxIds = paymentTxIds.Count == 0
            ? new HashSet<string>()
            : (await db.BillRawRecords.AsNoTracking()
                .Where(r => r.IsProcessed && paymentTxIds.Contains(r.SourcePaymentTransactionId))
                .Select(r => r.SourcePaymentTransactionId)
                .ToListAsync(ct)).ToHashSet();

        var rawById = context.RawRecords.ToDictionary(r => r.Id);
        var seenSourceIds = new HashSet<string>();
        var seenPaymentTxIds = new HashSet<string>();
        var duplicates = new List<NormalizedTransaction>();
        int duplicateCount = 0, potentialCount = 0;

        foreach (var tx in context.NormalizedTransactions.OrderBy(t => t.Id))
        {
            context.MerchantResolutions.TryGetValue(tx.Id, out var merchant);
            double score = 0;
            var reason = string.Empty;
            var leftId = 0;

            // 规则 1：Exact SourceTransactionId 或 SourcePaymentTransactionId（同批次内或已处理过的历史记录）。
            if (!string.IsNullOrEmpty(tx.SourceTransactionId))
            {
                if (existingSourceIds.Contains(tx.SourceTransactionId) || seenSourceIds.Contains(tx.SourceTransactionId))
                {
                    score = 100;
                    reason = "SourceTransactionId 完全匹配";
                }
                seenSourceIds.Add(tx.SourceTransactionId);
            }
            if (score < 100 && !string.IsNullOrEmpty(tx.SourcePaymentTransactionId))
            {
                if (existingPaymentTxIds.Contains(tx.SourcePaymentTransactionId) || seenPaymentTxIds.Contains(tx.SourcePaymentTransactionId))
                {
                    score = 100;
                    reason = "SourcePaymentTransactionId 完全匹配";
                }
                seenPaymentTxIds.Add(tx.SourcePaymentTransactionId);
            }

            // 规则 2/3：账户+金额+日期=90；账户+金额+商户=80（与已存在 Canonical 比对）。
            if (score < duplicateThreshold)
            {
                foreach (var ex in existing.Where(e => e.PlatformId == platformId && e.AmountMinor == tx.AmountMinor))
                {
                    double s = 0;
                    var r = string.Empty;
                    if (tx.OccurredAt != default && ex.TransactionDate.Date == tx.OccurredAt.Date)
                    {
                        s = 90;
                        r = "账户+金额+日期匹配";
                    }
                    else if (merchant is not null && ex.MerchantId == merchant.MerchantId)
                    {
                        s = 80;
                        r = "账户+金额+商户匹配";
                    }

                    if (s > score)
                    {
                        score = s;
                        reason = r;
                        leftId = ex.Id;
                    }
                }
            }

            tx.DuplicateScore = score;
            if (score >= duplicateThreshold)
            {
                tx.DuplicateStatus = DuplicateStatus.Duplicate;
                duplicates.Add(tx);
                duplicateCount++;
            }
            else if (score >= reviewThreshold)
            {
                tx.DuplicateStatus = DuplicateStatus.PotentialDuplicate;
                context.PendingDuplicates.Add((tx.RawRecordId, leftId, score, reason));
                potentialCount++;
            }
            else
            {
                tx.DuplicateStatus = DuplicateStatus.Unique;
            }

            // 回写 Raw 记录状态供追溯（不删除 Raw 数据）。
            if (rawById.TryGetValue(tx.RawRecordId, out var raw))
            {
                raw.DuplicateStatus = tx.DuplicateStatus;
                raw.DuplicateScore = tx.DuplicateScore;
            }
        }

        // 确定重复的交易从下游工作集移除，跳过 Canonical 发布；持久化状态标记。
        foreach (var tx in duplicates)
            context.NormalizedTransactions.Remove(tx);

        await db.SaveChangesAsync(ct);

        return new StepResult(input, context.NormalizedTransactions.Count, 0, potentialCount);
    }

    private static double GetOption(BillImportContext context, string key, double fallback) =>
        context.Options.TryGetValue(key, out var v) && v is not null
            ? Convert.ToDouble(v)
            : fallback;
}
