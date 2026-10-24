using Quartz;

namespace MinGo.MyBillBook.Services.Inbox.Scheduling;

/// <summary>
/// 周期扫描工作流目录，把 <c>on.schedule</c> 同步为 Quartz 中的 cron 触发器。
/// 新增/修改/删除 YAML 后无需重启进程即可生效。
/// </summary>
public sealed class InboxScheduleSynchronizer(
    ISchedulerFactory schedulerFactory,
    InboxWorkflowCatalog catalog,
    IConfiguration configuration,
    ILogger<InboxScheduleSynchronizer> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = Math.Max(5, configuration.GetValue("Inbox:ScheduleSyncSeconds", 30));
        var interval = TimeSpan.FromSeconds(seconds);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "同步 Inbox 定时任务失败，将在下个周期重试");
            }
        } while (await WaitNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>对比目录与 Quartz 中的任务，返回本次变更数量（便于日志与测试）。</summary>
    internal async Task<int> SyncAsync(CancellationToken ct)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);

        var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in catalog.LoadAll())
        {
            if (file.Definition is null)
                continue;

            var cron = file.Definition.On.Schedule;
            if (string.IsNullOrWhiteSpace(cron))
                continue;

            if (!desired.TryAdd(file.Definition.Name, cron))
            {
                logger.LogWarning("工作流名称重复，已忽略后续定义: {Workflow} ({File})",
                    file.Definition.Name, file.FileName);
            }
        }

        var existing = await scheduler.GetJobKeys(
            GroupMatcher<JobKey>.GroupEquals(InboxScheduledRunJob.GroupName), ct);

        var changes = 0;

        foreach (var jobKey in existing)
        {
            if (!desired.TryGetValue(jobKey.Name, out var cron))
            {
                await scheduler.DeleteJob(jobKey, ct);
                logger.LogInformation("移除定时任务: {Workflow}", jobKey.Name);
                changes++;
                continue;
            }

            var detail = await scheduler.GetJobDetail(jobKey, ct);
            var currentCron = detail?.JobDataMap.GetString(InboxScheduledRunJob.CronDataKey);
            if (string.Equals(currentCron, cron, StringComparison.Ordinal))
                continue;

            // cron 变化：重建任务，避免残留旧触发器。
            await scheduler.DeleteJob(jobKey, ct);
            await ScheduleAsync(scheduler, jobKey.Name, cron, ct);
            logger.LogInformation("更新定时任务: {Workflow} {Old} -> {New}",
                jobKey.Name, currentCron ?? "(无)", cron);
            changes++;
        }

        foreach (var (name, cron) in desired)
        {
            var jobKey = InboxScheduledRunJob.GetJobKey(name);
            if (existing.Contains(jobKey))
                continue;

            await ScheduleAsync(scheduler, name, cron, ct);
            logger.LogInformation("新增定时任务: {Workflow} ({Cron})", name, cron);
            changes++;
        }

        if (changes == 0)
            logger.LogDebug("Inbox 定时任务无变化（共 {Count} 个）", desired.Count);

        return changes;
    }

    private static async Task ScheduleAsync(
        IScheduler scheduler, string workflowName, string cron, CancellationToken ct)
    {
        var jobKey = InboxScheduledRunJob.GetJobKey(workflowName);

        var job = JobBuilder.Create<InboxScheduledRunJob>()
            .WithIdentity(jobKey)
            .UsingJobData(InboxScheduledRunJob.WorkflowDataKey, workflowName)
            .UsingJobData(InboxScheduledRunJob.CronDataKey, cron)
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{workflowName}-trigger", InboxScheduledRunJob.GroupName)
            .ForJob(jobKey)
            .WithSchedule(CronScheduleBuilder
                .Create(cron)
                .InTimeZone(TimeZoneInfo.Local))
            .Build();

        await scheduler.ScheduleJob(job, trigger, ScheduleJobOptions.Replacing, ct);
    }
}
