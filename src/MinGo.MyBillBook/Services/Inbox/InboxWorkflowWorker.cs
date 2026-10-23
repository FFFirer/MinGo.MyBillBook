namespace MinGo.MyBillBook.Services.Inbox;

/// <summary>
/// Inbox 后台 Worker：轮询数据库推进工作流运行。
/// 与 PipelineJobWorker 一致——HTTP 请求只负责入队/查询，长任务在后台执行。
/// </summary>
public sealed class InboxWorkflowWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<InboxWorkflowWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = Math.Max(1, configuration.GetValue("Inbox:PollSeconds", 2));
        var pollInterval = TimeSpan.FromSeconds(pollSeconds);
        var cleanupHours = Math.Max(0, configuration.GetValue("Inbox:CleanupIntervalHours", 6));
        var nextCleanup = DateTime.UtcNow;

        logger.LogInformation(
            "InboxWorkflowWorker 已启动，轮询间隔 {Interval}s，目录清理间隔 {CleanupHours}h",
            pollSeconds, cleanupHours);

        // 等待宿主完成启动（迁移等），并恢复上次进程中断留下的运行。
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            using var scope = scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<Core.Inbox.IInboxWorkflowRunner>();
            var recovered = await runner.RecoverStaleRunsAsync(stoppingToken);
            if (recovered > 0)
                logger.LogWarning("已恢复 {Count} 个中断的工作流运行", recovered);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "恢复中断运行失败");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (cleanupHours > 0 && DateTime.UtcNow >= nextCleanup)
                {
                    await RunCleanupAsync(stoppingToken);
                    nextCleanup = DateTime.UtcNow.AddHours(cleanupHours);
                }

                var progressed = await ProcessOnceAsync(stoppingToken);
                if (!progressed)
                    await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "InboxWorkflowWorker 轮询异常");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<Core.Inbox.IInboxWorkflowRunner>();
        return await runner.ProcessOnceAsync(ct);
    }

    /// <summary>按保留策略回收已结束运行的目录；活动运行永远保留。</summary>
    private async Task RunCleanupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var cleaner = scope.ServiceProvider.GetRequiredService<InboxWorkspaceCleaner>();
            await cleaner.CleanupAsync(dryRun: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 宿主正在停止
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Inbox 运行目录清理失败");
        }
    }
}
