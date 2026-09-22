using Microsoft.AspNetCore.Mvc;
using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Interfaces;

namespace MinGo.MyBillBook.Controllers;

[ApiController]
[Route("api/analysis")]
public class AnalysisController(IAnalysisService analysisService) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<AnalysisSummary>> GetSummary(
        [FromQuery] DateTime? start, [FromQuery] DateTime? end, CancellationToken ct = default)
    {
        var s = start ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var e = end ?? DateTime.Today;
        return Ok(await analysisService.GetSummaryAsync(s, e, ct));
    }

    [HttpGet("category")]
    public async Task<ActionResult<List<CategoryStat>>> GetCategoryBreakdown(
        [FromQuery] DateTime? start, [FromQuery] DateTime? end, CancellationToken ct = default)
    {
        var s = start ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var e = end ?? DateTime.Today;
        return Ok(await analysisService.GetCategoryBreakdownAsync(s, e, ct));
    }

    [HttpGet("trend")]
    public async Task<ActionResult<List<MonthlyTrend>>> GetMonthlyTrend(
        [FromQuery] int? year, CancellationToken ct = default)
    {
        return Ok(await analysisService.GetMonthlyTrendAsync(year ?? DateTime.Today.Year, ct));
    }

    [HttpGet("merchants")]
    public async Task<ActionResult<List<MerchantRank>>> GetTopMerchants(
        [FromQuery] DateTime? start, [FromQuery] DateTime? end, [FromQuery] int limit = 10, CancellationToken ct = default)
    {
        var s = start ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var e = end ?? DateTime.Today;
        return Ok(await analysisService.GetTopMerchantsAsync(s, e, limit, ct));
    }

    [HttpPost("sync")]
    public async Task<ActionResult> SyncToDuckDb(CancellationToken ct = default)
    {
        await analysisService.SyncToDuckDbAsync(ct);
        return Ok(new { message = "同步完成" });
    }

    /// <summary>Category × Tag 交叉矩阵。</summary>
    [HttpGet("category-tag-matrix")]
    public async Task<ActionResult<List<CategoryTagCell>>> GetCategoryTagMatrix(
        [FromQuery] DateTime? start, [FromQuery] DateTime? end, CancellationToken ct = default)
    {
        var s = start ?? new DateTime(DateTime.Today.Year, 1, 1);
        var e = end ?? DateTime.Today;
        return Ok(await analysisService.GetCategoryTagMatrixAsync(s, e, ct));
    }

    /// <summary>账户 × 月份净流。</summary>
    [HttpGet("account-monthly")]
    public async Task<ActionResult<List<DimensionMonthStat>>> GetAccountMonthly([FromQuery] int? year, CancellationToken ct = default)
        => Ok(await analysisService.GetAccountMonthlyAsync(year ?? DateTime.Today.Year, ct));

    /// <summary>商户 × 月份支出。</summary>
    [HttpGet("merchant-monthly")]
    public async Task<ActionResult<List<DimensionMonthStat>>> GetMerchantMonthly([FromQuery] int? year, [FromQuery] int limit = 10, CancellationToken ct = default)
        => Ok(await analysisService.GetMerchantMonthlyAsync(year ?? DateTime.Today.Year, limit, ct));

    /// <summary>标签 × 月份支出。</summary>
    [HttpGet("tag-monthly")]
    public async Task<ActionResult<List<DimensionMonthStat>>> GetTagMonthly([FromQuery] int? year, CancellationToken ct = default)
        => Ok(await analysisService.GetTagMonthlyAsync(year ?? DateTime.Today.Year, ct));

    /// <summary>现金流（按月，Transfer 不计入）。</summary>
    [HttpGet("cash-flow")]
    public async Task<ActionResult<List<CashFlowPoint>>> GetCashFlow([FromQuery] int? year, CancellationToken ct = default)
        => Ok(await analysisService.GetCashFlowAsync(year ?? DateTime.Today.Year, ct));

    /// <summary>净资产（账户余额汇总）。</summary>
    [HttpGet("net-worth")]
    public async Task<ActionResult<NetWorthSnapshot>> GetNetWorth(CancellationToken ct = default)
        => Ok(await analysisService.GetNetWorthAsync(ct));
}
