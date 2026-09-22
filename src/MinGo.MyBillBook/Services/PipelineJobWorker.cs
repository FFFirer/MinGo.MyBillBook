using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services;

/// <summary>
/// 后台任务 Worker（设计第 16 节 Job 队列，MVP 不用 Redis）。
/// 以 BackgroundService 轮询 Pending 任务并异步执行，使导入/重建接口可立即返回入队，
/// 避免在 HTTP 请求线程内同步跑完整 Pipeline。
/// </summary>
public class PipelineJobWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PipelineJobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PipelineJobWorker 已启动，轮询间隔 {Interval}s", PollInterval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextPendingJobAsync(stoppingToken);
                if (!processed)
                    await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PipelineJobWorker 轮询异常");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    /// <summary>领取并执行一个 Pending 任务。返回是否处理了任务（有则继续立即轮询下一个）。</summary>
    private async Task<bool> ProcessNextPendingJobAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 领取最早的 Pending 任务，置为 Running 并立即落库，避免重复领取。
        var job = await db.PipelineJobs
            .Where(j => j.Status == PipelineJobStatus.Pending)
            .OrderBy(j => j.CreatedAt).ThenBy(j => j.Id)
            .FirstOrDefaultAsync(ct);
        if (job is null)
            return false;

        job.Status = PipelineJobStatus.Running;
        job.StartedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);

        try
        {
            switch (job.Type)
            {
                case PipelineJobType.Rebuild:
                {
                    var payload = ParseRebuildPayload(job.Payload);
                    var rebuild = scope.ServiceProvider.GetRequiredService<IRebuildService>();
                    var count = await rebuild.RebuildAsync(payload.From, payload.BatchId, ct);
                    logger.LogInformation("Job {JobId} (Rebuild from={From}) 完成，重算 {Count} 条", job.Id, payload.From, count);
                    break;
                }
                case PipelineJobType.Process:
                {
                    var payload = ParseRebuildPayload(job.Payload);
                    var processing = scope.ServiceProvider.GetRequiredService<IBillProcessingService>();
                    var count = payload.BatchId.HasValue
                        ? await processing.ProcessBatchAsync(payload.BatchId.Value, ct)
                        : await processing.ProcessUnprocessedAsync(ct);
                    logger.LogInformation("Job {JobId} (Process) 完成，处理 {Count} 条", job.Id, count);
                    break;
                }
            }

            job.Status = PipelineJobStatus.Completed;
            job.CompletedAt = DateTime.Now;
            job.Error = null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} 执行失败", job.Id);
            job.Status = PipelineJobStatus.Failed;
            job.CompletedAt = DateTime.Now;
            job.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static RebuildPayload ParseRebuildPayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new RebuildPayload();
        try
        {
            return JsonSerializer.Deserialize<RebuildPayload>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new RebuildPayload();
        }
        catch
        {
            return new RebuildPayload();
        }
    }

    private sealed record RebuildPayload
    {
        public string? From { get; init; }
        public int? BatchId { get; init; }
    }
}
