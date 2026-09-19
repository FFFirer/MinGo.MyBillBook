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
