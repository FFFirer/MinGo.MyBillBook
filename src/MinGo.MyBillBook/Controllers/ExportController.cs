using Microsoft.AspNetCore.Mvc;
using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Services;

namespace MinGo.MyBillBook.Controllers;

[ApiController]
[Route("api/export")]
public class ExportController(CsvExportService exportService) : ControllerBase
{
    [HttpGet("bills")]
    public async Task<IActionResult> ExportBills([FromQuery] BillQueryFilter filter, CancellationToken ct = default)
    {
        var bytes = await exportService.ExportBillsAsync(filter, ct);
        return File(bytes, "text/csv", $"bills_{DateTime.Today:yyyyMMdd}.csv");
    }

    [HttpGet("analysis")]
    public async Task<IActionResult> ExportAnalysis(
        [FromQuery] DateTime? start, [FromQuery] DateTime? end, CancellationToken ct = default)
    {
        var s = start ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var e = end ?? DateTime.Today;
        var bytes = await exportService.ExportAnalysisAsync(s, e, ct);
        return File(bytes, "text/csv", $"analysis_{DateTime.Today:yyyyMMdd}.csv");
    }
}
