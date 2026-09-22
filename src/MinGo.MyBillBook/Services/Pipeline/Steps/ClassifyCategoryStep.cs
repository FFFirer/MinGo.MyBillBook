using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Pipeline.Steps;

/// <summary>
/// 分类步骤：基于 Normalized 层输入解析交易方向、构建内存中的 Canonical 记录，并按设计第 8 节的
/// 分类优先级链决定 CategoryId：User → ExplicitRule → MerchantRule → AI → Default。
/// 每次决策生成 <see cref="ClassificationResult"/> 溯源（暂存于上下文，由发布步骤回填 Id 后落库），
/// 保证无规则命中时落到默认分类而非 null。仅做转换，不落库，符合步骤单一职责。
/// </summary>
public class ClassifyCategoryStep(
    ICategoryRuleEngine ruleEngine,
    ICategoryClassifier classifier,
    AppDbContext db) : IPipelineStep<BillImportContext>
{
    public string Name => "ClassifyCategory";
    public int Order => 20;

    public async Task<StepResult> ExecuteAsync(BillImportContext context, CancellationToken ct)
    {
        var input = context.NormalizedTransactions.Count;
        var records = new List<BillRecord>(input);
        var pending = new List<(BillRecord, ClassificationResult)>(input);

        // 一次性加载默认兜底分类与各商户的默认分类映射（MerchantRule 层）。
        var defaultCategoryId = await db.BillCategories
            .Where(c => c.IsDefault)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);
        var merchantDefaults = await db.Merchants
            .Where(m => m.DefaultCategoryId != null)
            .ToDictionaryAsync(m => m.Id, m => m.DefaultCategoryId!.Value, ct);

        foreach (var tx in context.NormalizedTransactions)
        {
            var transactionType = ParseTransactionType(tx.Direction);
            context.MerchantResolutions.TryGetValue(tx.Id, out var merchant);

            // 分类优先级链：User → ExplicitRule → MerchantRule → AI → Default
            var decision = await ResolveCategoryAsync(tx, merchant, merchantDefaults, defaultCategoryId, ct);

            var record = new BillRecord
            {
                RawRecordId = tx.RawRecordId,
                PlatformId = context.Batch.PlatformId,
                TransactionDate = tx.OccurredAt,
                Counterparty = tx.Counterparty,
                Merchant = tx.Counterparty,
                MerchantId = merchant?.MerchantId,
                CategoryId = decision.CategoryId,
                ProductName = tx.ProductName,
                AmountMinor = tx.AmountMinor,
                TransactionType = transactionType,
                Status = tx.Status,
                SourceFile = context.Batch.FileName,
                IsManualAdjusted = false,
                SyncedToDuckDb = false
            };

            records.Add(record);

            // 溯源：商户归一化决策（ResolveMerchantStep 产出）。
            if (merchant is not null)
            {
                pending.Add((record, new ClassificationResult
                {
                    Field = ClassificationField.Merchant,
                    Value = merchant.MerchantId.ToString(),
                    Source = merchant.Source,
                    RuleId = merchant.RuleId,
                    Confidence = merchant.Confidence,
                    CreatedAt = DateTime.Now
                }));
            }

            // 溯源：分类决策（记录命中的来源、规则 Id 与置信度，形成可追溯优先级链）。
            pending.Add((record, new ClassificationResult
            {
                Field = ClassificationField.Category,
                Value = decision.CategoryId?.ToString() ?? string.Empty,
                Source = decision.Source,
                RuleId = decision.RuleId,
                Confidence = decision.Confidence,
                CreatedAt = DateTime.Now
            }));
        }

        context.CanonicalRecords = records;
        context.PendingClassifications = pending;
        return new StepResult(input, records.Count);
    }

    /// <summary>
    /// 按设计第 8 节优先级链解析分类。导入场景均为新记录，无既有 User 结果（User 覆盖由手工改分类路径处理，
    /// 见 IBillQueryService.UpdateCategoryAsync）；此处从 ExplicitRule 起依次向下回退，最终落到 Default 而非 null。
    /// </summary>
    private async Task<CategoryDecision> ResolveCategoryAsync(
        NormalizedTransaction tx,
        MerchantResolution? merchant,
        Dictionary<int, int> merchantDefaults,
        int? defaultCategoryId,
        CancellationToken ct)
    {
        // 1) Explicit Rule：显式分类规则命中（最高自动优先级）。
        var match = ruleEngine.MatchCategoryDetailed(tx.Counterparty, tx.ProductName, merchant?.CanonicalName);
        if (match is not null)
            return new CategoryDecision(match.CategoryId, ClassificationSource.ExplicitRule, match.RuleId, 0.9);

        // 2) Merchant Rule：商户预设的默认分类。
        if (merchant is not null && merchantDefaults.TryGetValue(merchant.MerchantId, out var merchantCategoryId))
            return new CategoryDecision(merchantCategoryId, ClassificationSource.MerchantRule, null, 0.75);

        // 3) AI/ML：可插拔分类器（当前空实现返回 null）。
        var ai = await classifier.ClassifyAsync(tx.Counterparty, tx.ProductName, ct);
        if (ai is not null)
            return new CategoryDecision(ai.CategoryId, ClassificationSource.AI, null, ai.Confidence);

        // 4) Default：兜底分类，确保 CategoryId 非 null。
        return new CategoryDecision(defaultCategoryId, ClassificationSource.Default, null, 0.1);
    }

    private static TransactionType ParseTransactionType(string direction) => direction switch
    {
        "收入" => TransactionType.Income,
        "支出" => TransactionType.Expense,
        "不计收支" => TransactionType.Transfer,
        _ => TransactionType.Expense
    };

    /// <summary>分类优先级链的决策结果。</summary>
    private record CategoryDecision(int? CategoryId, ClassificationSource Source, int? RuleId, double Confidence);
}
