using Microsoft.AspNetCore.Mvc;
using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Interfaces;

namespace MinGo.MyBillBook.Controllers;

[ApiController]
[Route("api/bill")]
public class BillImportController(IBillImportService importService, IBillProcessingService processingService) : ControllerBase
{
    [HttpPost("import")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50MB
    public async Task<ActionResult<BillImportResult>> Import(
        IFormFile file,
        [FromForm] int platformId = 1,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "请选择文件" });

        using var stream = file.OpenReadStream();
        var result = await importService.ImportAsync(stream, file.FileName, platformId, ct);
        return Ok(result);
    }

    [HttpGet("import/batches")]
    public async Task<ActionResult<List<BillImportBatchDto>>> GetBatches(CancellationToken ct = default)
    {
        var batches = await importService.GetBatchesAsync(ct);
        var dtos = batches.Select(b => new BillImportBatchDto(
            b.Id, b.Platform.Name, b.FileName, b.ImportDate,
            b.TotalCount, b.SuccessCount, b.DuplicateCount, b.Status.ToString()
        )).ToList();
        return Ok(dtos);
    }

    [HttpPost("import/batches/{batchId}/process")]
    public async Task<ActionResult> ProcessBatch(int batchId, CancellationToken ct = default)
    {
        var count = await processingService.ProcessBatchAsync(batchId, ct);
        return Ok(new { processed = count });
    }

    [HttpPost("import/process-all")]
    public async Task<ActionResult> ProcessAll(CancellationToken ct = default)
    {
        var count = await processingService.ProcessUnprocessedAsync(ct);
        return Ok(new { processed = count });
    }
}

public record BillImportBatchDto(
    int Id, string PlatformName, string FileName, DateTime ImportDate,
    int TotalCount, int SuccessCount, int DuplicateCount, string Status);
