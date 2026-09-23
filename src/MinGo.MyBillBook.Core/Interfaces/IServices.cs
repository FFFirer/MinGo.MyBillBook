using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Models;

namespace MinGo.MyBillBook.Core.Interfaces;

public interface IBillImportService
{
    Task<BillImportResult> ImportAsync(Stream fileStream, string fileName, int platformId, CancellationToken ct = default);
    Task<List<BillImportBatch>> GetBatchesAsync(CancellationToken ct = default);
}

public interface IBillProcessingService
{
    Task<int> ProcessBatchAsync(int batchId, CancellationToken ct = default);
    Task<int> ReprocessBatchAsync(int batchId, CancellationToken ct = default);
    Task<int> ProcessUnprocessedAsync(CancellationToken ct = default);
}

public interface IBillQueryService
{
    Task<PagedResult<BillDto>> QueryAsync(BillQueryFilter filter, CancellationToken ct = default);
    Task<BillDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task UpdateCategoryAsync(int billId, int categoryId, CancellationToken ct = default);

    /// <summary>获取某账单的全部标签（含来源溯源）。</summary>
    Task<List<TransactionTagDto>> GetTagsAsync(int billId, CancellationToken ct = default);
    /// <summary>为账单手工打标（Source=User，优先级最高，不被自动规则覆盖）。</summary>
    Task AddTagAsync(int billId, int tagId, CancellationToken ct = default);
    /// <summary>移除账单上的某标签。</summary>
    Task RemoveTagAsync(int billId, int tagId, CancellationToken ct = default);

    /// <summary>获取某账单的全部分类/商户/标签决策溯源（设计第 8 节优先级链可追溯）。</summary>
    Task<List<ClassificationResultDto>> GetClassificationsAsync(int billId, CancellationToken ct = default);
}

public interface ICategoryRuleEngine
{
    int? MatchCategory(string counterparty, string productName, string? merchant = null);

    /// <summary>
    /// 显式分类规则匹配（设计第 8 节优先级链 ExplicitRule 层），返回命中分类与规则 Id 以便溯源。
    /// </summary>
    CategoryMatch? MatchCategoryDetailed(string counterparty, string productName, string? merchant = null);
}

/// <summary>显式分类规则命中结果：分类 Id 与命中规则 Id。</summary>
public record CategoryMatch(int CategoryId, int RuleId);

/// <summary>AI/ML 分类结果（设计第 8 节优先级链 AI 层，可插拔扩展点）。</summary>
public record CategoryClassification(int CategoryId, double Confidence);

/// <summary>
/// 原始分类归一化结果：匹配到的分类 Id 与匹配方式。
/// </summary>
public record CategoryNormalizationResult(int CategoryId, double Confidence);

/// <summary>
/// 将平台原始分类文本（如支付宝"交易分类"列的"餐饮美食"）归一化到系统 BillCategory。
/// </summary>
public interface ICategoryNormalizer
{
    Task<CategoryNormalizationResult?> NormalizeAsync(string sourceCategory, CancellationToken ct = default);
}

/// <summary>
/// AI/ML 分类器扩展点（设计第 8 节）。当前提供空实现，未来接入模型时仅需替换实现，
/// 分类优先级链无需改动（"以后增加 AI 只是往规则层加能力"）。
/// </summary>
public interface ICategoryClassifier
{
    Task<CategoryClassification?> ClassifyAsync(string counterparty, string productName, CancellationToken ct = default);
}

/// <summary>商户归一化结果：命中的商户 Id、置信度、命中规则 Id 与决策来源。</summary>
public record MerchantResolution(int MerchantId, string CanonicalName, double Confidence, int? RuleId, ClassificationSource Source);

/// <summary>
/// 商户归一化解析器（设计第 4/7 节）。按 MerchantAlias 优先级将原始交易对方/描述
/// 归一到统一 Merchant；未命中则按原值创建/复用低置信度 Merchant。
/// </summary>
public interface IMerchantResolver
{
    Task<MerchantResolution> ResolveAsync(string counterparty, string description, CancellationToken ct = default);
}

public interface IAnalysisService
{
    Task<AnalysisSummary> GetSummaryAsync(DateTime start, DateTime end, CancellationToken ct = default);
    Task<List<CategoryStat>> GetCategoryBreakdownAsync(DateTime start, DateTime end, CancellationToken ct = default);
    Task<List<MonthlyTrend>> GetMonthlyTrendAsync(int year, CancellationToken ct = default);
    Task<List<MerchantRank>> GetTopMerchantsAsync(DateTime start, DateTime end, int limit = 10, CancellationToken ct = default);

    /// <summary>Category × Tag 交叉矩阵（支出）。</summary>
    Task<List<CategoryTagCell>> GetCategoryTagMatrixAsync(DateTime start, DateTime end, CancellationToken ct = default);
    /// <summary>账户 × 月份净流（收入-支出，排除 Transfer）。</summary>
    Task<List<DimensionMonthStat>> GetAccountMonthlyAsync(int year, CancellationToken ct = default);
    /// <summary>商户 × 月份支出。</summary>
    Task<List<DimensionMonthStat>> GetMerchantMonthlyAsync(int year, int limit = 10, CancellationToken ct = default);
    /// <summary>标签 × 月份支出。</summary>
    Task<List<DimensionMonthStat>> GetTagMonthlyAsync(int year, CancellationToken ct = default);
    /// <summary>现金流（按月，Transfer 不计入收支）。</summary>
    Task<List<CashFlowPoint>> GetCashFlowAsync(int year, CancellationToken ct = default);
    /// <summary>净资产（各账户余额汇总）。</summary>
    Task<NetWorthSnapshot> GetNetWorthAsync(CancellationToken ct = default);

    Task SyncToDuckDbAsync(CancellationToken ct = default);
}

/// <summary>
/// 局部重跑服务（设计第 16/20 节）。规则（Merchant/Category/Tag）变更后，
/// 从指定步骤起重算 Canonical 层，无需重导 CSV。
/// </summary>
public interface IRebuildService
{
    /// <summary>
    /// 从指定步骤起局部重跑。fromStep 为空则全量重建（从 Normalize）；
    /// batchId 为空则重建所有已完成批次。返回重跑的记录数。
    /// </summary>
    Task<int> RebuildAsync(string? fromStep, int? batchId, CancellationToken ct = default);
}
