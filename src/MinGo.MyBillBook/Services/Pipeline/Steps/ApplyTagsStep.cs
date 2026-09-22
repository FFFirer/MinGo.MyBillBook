using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Pipeline.Steps;

/// <summary>
/// 打标签步骤（Order 在 Classify 之后、Publish 之前）：按 TagRule 条件维度为每条 Canonical 记录
/// 自动打标签，命中写 Source=Rule，暂存于上下文由 PublishCanonicalStep 落库。
/// 规则示例：IF Category=餐饮 AND DayOfWeek∈{Sat,Sun} THEN Tag=周末；IF Merchant=星巴克 THEN Tag=咖啡。
/// </summary>
public class ApplyTagsStep(AppDbContext db) : IPipelineStep<BillImportContext>
{
    public string Name => "ApplyTags";
    public int Order => 25;

    public async Task<StepResult> ExecuteAsync(BillImportContext context, CancellationToken ct)
    {
        var input = context.CanonicalRecords.Count;
        if (input == 0)
            return new StepResult(0, 0);

        var rules = await db.TagRules.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ToListAsync(ct);

        if (rules.Count == 0)
            return new StepResult(input, 0, 0, 0);

        var categoryNames = await db.BillCategories.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var merchantNames = await db.Merchants.AsNoTracking()
            .ToDictionaryAsync(m => m.Id, m => m.CanonicalName, ct);

        var applied = 0;
        foreach (var record in context.CanonicalRecords)
        {
            // 每条记录同一 Tag 只应用一次（取优先级最高的命中规则）。
            var appliedTags = new HashSet<int>();
            foreach (var rule in rules)
            {
                if (appliedTags.Contains(rule.TagId))
                    continue;
                if (!Matches(rule, record, categoryNames, merchantNames))
                    continue;

                appliedTags.Add(rule.TagId);
                context.PendingTags.Add((record, rule.TagId, TagSource.Rule, 0.8));
                applied++;
            }
        }

        return new StepResult(input, applied);
    }

    private static bool Matches(
        TagRule rule, BillRecord record,
        Dictionary<int, string> categoryNames, Dictionary<int, string> merchantNames)
    {
        var value = rule.ConditionValue?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return false;

        switch (rule.Condition)
        {
            case TagRuleCondition.Category:
                if (record.CategoryId is int catId)
                {
                    if (catId.ToString() == value) return true;
                    if (categoryNames.TryGetValue(catId, out var catName) &&
                        string.Equals(catName, value, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;

            case TagRuleCondition.Merchant:
                if (record.MerchantId is int mId)
                {
                    if (mId.ToString() == value) return true;
                    if (merchantNames.TryGetValue(mId, out var mName) &&
                        string.Equals(mName, value, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return record.Merchant.Contains(value, StringComparison.OrdinalIgnoreCase)
                    || record.Counterparty.Contains(value, StringComparison.OrdinalIgnoreCase);

            case TagRuleCondition.DayOfWeek:
                if (record.TransactionDate == default) return false;
                var dow = record.TransactionDate.DayOfWeek.ToString(); // e.g. "Saturday"
                var wanted = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return wanted.Any(w =>
                    string.Equals(dow, w, StringComparison.OrdinalIgnoreCase) ||
                    (dow.Length >= 3 && string.Equals(dow[..3], w, StringComparison.OrdinalIgnoreCase)));

            case TagRuleCondition.AmountRange:
                var parts = value.Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length != 2) return false;
                if (!decimal.TryParse(parts[0], out var minYuan) || !decimal.TryParse(parts[1], out var maxYuan)) return false;
                var min = (long)Math.Round(minYuan * 100m, MidpointRounding.AwayFromZero);
                var max = (long)Math.Round(maxYuan * 100m, MidpointRounding.AwayFromZero);
                return record.AmountMinor >= min && record.AmountMinor <= max;

            case TagRuleCondition.Keyword:
                return record.ProductName.Contains(value, StringComparison.OrdinalIgnoreCase)
                    || record.Counterparty.Contains(value, StringComparison.OrdinalIgnoreCase)
                    || record.Merchant.Contains(value, StringComparison.OrdinalIgnoreCase);

            default:
                return false;
        }
    }
}
