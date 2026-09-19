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
}

public interface ICategoryRuleEngine
{
    int? MatchCategory(string counterparty, string productName, string? merchant = null);
}

public interface IAnalysisService
{
    Task<AnalysisSummary> GetSummaryAsync(DateTime start, DateTime end, CancellationToken ct = default);
    Task<List<CategoryStat>> GetCategoryBreakdownAsync(DateTime start, DateTime end, CancellationToken ct = default);
    Task<List<MonthlyTrend>> GetMonthlyTrendAsync(int year, CancellationToken ct = default);
    Task<List<MerchantRank>> GetTopMerchantsAsync(DateTime start, DateTime end, int limit = 10, CancellationToken ct = default);
    Task SyncToDuckDbAsync(CancellationToken ct = default);
}
