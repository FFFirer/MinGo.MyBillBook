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
}
