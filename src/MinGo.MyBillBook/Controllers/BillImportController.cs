using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Controllers;

[ApiController]
[Route("api/bill")]
public class BillImportController(IBillImportService importService, IBillProcessingService processingService, AppDbContext db) : ControllerBase
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

    [HttpGet("import/batches/{batchId}/pipeline")]
    public async Task<ActionResult<List<PipelineRunDto>>> GetPipelineProgress(int batchId, CancellationToken ct = default)
    {
        var runs = await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.ImportBatchId == batchId)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new PipelineRunDto(
                r.Id,
                r.PipelineType.ToString(),
                r.Status.ToString(),
                r.StartedAt,
                r.CompletedAt,
                r.Steps.OrderBy(s => s.StartedAt).Select(s => new PipelineStepRunDto(
                    s.StepName, s.Status.ToString(), s.InputCount, s.OutputCount,
                    s.ErrorCount, s.WarningCount, s.CompletedAt)).ToList()))
            .ToListAsync(ct);
        return Ok(runs);
    }

    /// <summary>待复核的疑似重复候选列表（按评分降序）。</summary>
    [HttpGet("duplicates")]
    public async Task<ActionResult<List<DuplicateCandidateDto>>> GetDuplicates(CancellationToken ct = default)
    {
        var rows = await db.DuplicateCandidates
            .AsNoTracking()
            .Where(c => c.Status == DuplicateCandidateStatus.Pending)
            .OrderByDescending(c => c.Score).ThenByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Score,
                c.MatchReason,
                Status = c.Status.ToString(),
                c.CreatedAt,
                c.LeftRecordId,
                LeftMerchant = c.LeftRecord!.Merchant,
                LeftProduct = c.LeftRecord.ProductName,
                LeftAmountMinor = c.LeftRecord.AmountMinor,
                LeftDate = c.LeftRecord.TransactionDate,
                c.RightRecordId,
                RightMerchant = c.RightRecord!.Merchant,
                RightProduct = c.RightRecord.ProductName,
                RightAmountMinor = c.RightRecord.AmountMinor,
                RightDate = c.RightRecord.TransactionDate
            })
            .ToListAsync(ct);

        var dtos = rows.Select(r => new DuplicateCandidateDto(
            r.Id, r.Score, r.MatchReason, r.Status, r.CreatedAt,
            r.LeftRecordId, $"{r.LeftMerchant} / {r.LeftProduct}", r.LeftAmountMinor / 100m, r.LeftDate,
            r.RightRecordId, $"{r.RightMerchant} / {r.RightProduct}", r.RightAmountMinor / 100m, r.RightDate)).ToList();
        return Ok(dtos);
    }

    /// <summary>确认重复：删除多余的 Canonical 记录（保留 Raw），候选一并移除。</summary>
    [HttpPost("duplicates/{id}/confirm")]
    public async Task<ActionResult> ConfirmDuplicate(int id, CancellationToken ct = default)
    {
        var candidate = await db.DuplicateCandidates.FindAsync([id], ct);
        if (candidate is null)
            return NotFound(new { message = "候选不存在" });

        var right = await db.BillRecords.FindAsync([candidate.RightRecordId], ct);
        if (right is not null)
            db.BillRecords.Remove(right);
        db.DuplicateCandidates.Remove(candidate);
        await db.SaveChangesAsync(ct);
        return Ok(new { confirmed = id });
    }

    /// <summary>驳回：非重复，保留两条记录，候选置为 Rejected。</summary>
    [HttpPost("duplicates/{id}/reject")]
    public async Task<ActionResult> RejectDuplicate(int id, CancellationToken ct = default)
    {
        var candidate = await db.DuplicateCandidates.FindAsync([id], ct);
        if (candidate is null)
            return NotFound(new { message = "候选不存在" });

        candidate.Status = DuplicateCandidateStatus.Rejected;
        await db.SaveChangesAsync(ct);
        return Ok(new { rejected = id });
    }
}

public record BillImportBatchDto(
    int Id, string PlatformName, string FileName, DateTime ImportDate,
    int TotalCount, int SuccessCount, int DuplicateCount, string Status);

public record DuplicateCandidateDto(
    int Id, double Score, string MatchReason, string Status, DateTime CreatedAt,
    int LeftRecordId, string LeftDescription, decimal LeftAmount, DateTime LeftDate,
    int RightRecordId, string RightDescription, decimal RightAmount, DateTime RightDate);
