using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Controllers;

/// <summary>
/// 后台任务与统一规则视图（设计第 16/20 节）。
/// 重建/处理请求入队后立即返回，由 <see cref="Services.PipelineJobWorker"/> 异步执行；
/// 并提供跨 CategoryRule / MerchantAlias / TagRule 的统一 CleaningRule 只读视图，便于集中管理。
/// </summary>
[ApiController]
[Route("api")]
public class JobController(AppDbContext db) : ControllerBase
{
    /// <summary>入队局部重建任务（规则变更后无需重导 CSV）。立即返回 jobId，Worker 异步执行。</summary>
    [HttpPost("bill/rebuild")]
    public async Task<ActionResult> EnqueueRebuild(
        [FromQuery] string? from, [FromQuery] int? batchId, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { from, batchId });
        var job = new PipelineJob
        {
            Type = PipelineJobType.Rebuild,
            Payload = payload,
            Status = PipelineJobStatus.Pending,
            CreatedAt = DateTime.Now
        };
        db.PipelineJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return Accepted($"/api/jobs/{job.Id}", new { jobId = job.Id, status = job.Status.ToString(), from, batchId });
    }

    /// <summary>入队批次处理任务。立即返回 jobId，Worker 异步执行。</summary>
    [HttpPost("jobs/process")]
    public async Task<ActionResult> EnqueueProcess([FromQuery] int? batchId, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { batchId });
        var job = new PipelineJob
        {
            Type = PipelineJobType.Process,
            Payload = payload,
            Status = PipelineJobStatus.Pending,
            CreatedAt = DateTime.Now
        };
        db.PipelineJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return Accepted($"/api/jobs/{job.Id}", new { jobId = job.Id, status = job.Status.ToString(), batchId });
    }

    /// <summary>任务列表（按创建时间倒序）。</summary>
    [HttpGet("jobs")]
    public async Task<ActionResult<List<PipelineJobDto>>> GetJobs([FromQuery] int take = 50, CancellationToken ct = default)
    {
        var jobs = await db.PipelineJobs
            .AsNoTracking()
            .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        return Ok(jobs.Select(ToDto).ToList());
    }

    /// <summary>单个任务状态。</summary>
    [HttpGet("jobs/{id:int}")]
    public async Task<ActionResult<PipelineJobDto>> GetJob(int id, CancellationToken ct = default)
    {
        var job = await db.PipelineJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return NotFound();
        return Ok(ToDto(job));
    }

    /// <summary>
    /// 统一 CleaningRule 视图（设计第 20 节，可选）：把 CategoryRule / MerchantAlias / TagRule
    /// 收敛到同一结构，便于集中查看各类清洗规则的优先级与启用状态。
    /// </summary>
    [HttpGet("cleaning-rules")]
    public async Task<ActionResult<List<CleaningRuleDto>>> GetCleaningRules(CancellationToken ct = default)
    {
        var result = new List<CleaningRuleDto>();

        var categoryRules = await db.CategoryRules.AsNoTracking()
            .Include(r => r.Category)
            .ToListAsync(ct);
        result.AddRange(categoryRules.Select(r => new CleaningRuleDto(
            "Category", r.Id, $"{r.MatchField} ~ {r.MatchPattern}", $"分类={r.Category.Name}", r.Priority, r.IsActive)));

        var aliases = await db.MerchantAliases.AsNoTracking()
            .Include(a => a.Merchant)
            .ToListAsync(ct);
        result.AddRange(aliases.Select(a => new CleaningRuleDto(
            "Merchant", a.Id, $"{a.MatchType} ~ {a.Pattern}", $"商户={a.Merchant.CanonicalName}", a.Priority, a.IsActive)));

        var tagRules = await db.TagRules.AsNoTracking()
            .Include(t => t.Tag)
            .ToListAsync(ct);
        result.AddRange(tagRules.Select(t => new CleaningRuleDto(
            "Tag", t.Id, $"{t.Condition} ~ {t.ConditionValue}", $"标签={t.Tag.Name}", t.Priority, t.IsActive)));

        return Ok(result
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.Type)
            .ToList());
    }

    private static PipelineJobDto ToDto(PipelineJob j) => new(
        j.Id, j.Type.ToString(), j.Payload, j.Status.ToString(),
        j.CreatedAt, j.StartedAt, j.CompletedAt, j.Error);
}

public record PipelineJobDto(
    int Id, string Type, string Payload, string Status,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? CompletedAt, string? Error);

/// <summary>统一清洗规则视图行（设计第 20 节）。</summary>
public record CleaningRuleDto(
    string Type, int RuleId, string Condition, string Action, int Priority, bool IsActive);
