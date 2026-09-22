using MinGo.MyBillBook.Core.Models;

namespace MinGo.MyBillBook.Core.DTOs;

public record BillImportResult(
    int BatchId,
    string FileName,
    int TotalCount,
    int SuccessCount,
    int DuplicateCount,
    List<string> Errors);

public record BillQueryFilter
{
    public int? PlatformId { get; init; }
    public int? CategoryId { get; init; }
    public int? FundAccountId { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public string? Keyword { get; init; }
    public TransactionType? TransactionType { get; init; }
    /// <summary>按标签过滤：命中任一指定 TagId 即入选（设计第 10 节交叉维度）。</summary>
    public List<int>? TagIds { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string SortBy { get; init; } = "TransactionDate";
    public bool SortDescending { get; init; } = true;
}

public record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);

public record BillDto
{
    public int Id { get; init; }
    public DateTime TransactionDate { get; init; }
    public string Counterparty { get; init; } = string.Empty;
    public string Merchant { get; init; } = string.Empty;
    public string? CategoryName { get; init; }
    public int? CategoryId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string TransactionType { get; init; } = string.Empty;
    public string PlatformName { get; init; } = string.Empty;
    public string? FundAccountName { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsManualAdjusted { get; init; }
    /// <summary>该账单携带的标签名称（正交横向维度）。</summary>
    public List<string> Tags { get; init; } = [];
}

public record AnalysisSummary
{
    public decimal TotalIncome { get; init; }
    public decimal TotalExpense { get; init; }
    public decimal Balance => TotalIncome - TotalExpense;
    public int TransactionCount { get; init; }
}

public record CategoryStat(string CategoryName, string Icon, decimal Amount, int Count, double Percentage);

public record MonthlyTrend(string Month, decimal Income, decimal Expense);

public record MerchantRank(string Merchant, decimal Amount, int Count);

/// <summary>Category × Tag 交叉矩阵单元格（设计第 10 节）。</summary>
public record CategoryTagCell(string CategoryName, string TagName, decimal Amount, int Count);

/// <summary>维度 × 月份统计（用于 Account/Merchant/Tag × Month 多维分析）。</summary>
public record DimensionMonthStat(string Name, string Month, decimal Amount, int Count);

/// <summary>现金流点（设计第 10 节，Transfer 不计入收支）。</summary>
public record CashFlowPoint(string Month, decimal Inflow, decimal Outflow, decimal Net);

/// <summary>净资产快照（账户余额汇总）。</summary>
public record NetWorthSnapshot(decimal Total, List<NetWorthItem> Accounts);
public record NetWorthItem(string AccountName, decimal Balance);

/// <summary>交易标签视图（含来源溯源，设计第 7 节）。</summary>
public record TransactionTagDto(int TagId, string Name, string Source, double Confidence);

/// <summary>分类/商户/标签决策溯源视图（设计第 8/11 节），展示每笔分类决策的来源、规则与置信度。</summary>
public record ClassificationResultDto(string Field, string Value, string Source, int? RuleId, double Confidence, DateTime CreatedAt);

public record PipelineRunDto(
    int Id,
    string PipelineType,
    string Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    List<PipelineStepRunDto> Steps);

public record PipelineStepRunDto(
    string StepName,
    string Status,
    int InputCount,
    int OutputCount,
    int ErrorCount,
    int WarningCount,
    DateTime? CompletedAt);
