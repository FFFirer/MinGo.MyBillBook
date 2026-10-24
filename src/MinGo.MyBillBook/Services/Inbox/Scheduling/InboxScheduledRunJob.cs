using MinGo.MyBillBook.Core.Inbox;
using MinGo.MyBillBook.Core.Models;
using Quartz;

namespace MinGo.MyBillBook.Services.Inbox.Scheduling;

/// <summary>
/// 按 <c>on.schedule</c> 触发一次工作流运行。每次触发前重新读取目录中的 YAML，
/// 保证工作流定义修改后无需重启即可生效。
/// </summary>
[DisallowConcurrentExecution]
public sealed class InboxScheduledRunJob(
    InboxWorkflowCatalog catalog,
    IInboxWorkflowRunner runner,
    ILogger<InboxScheduledRunJob> logger) : IJob
{
    /// <summary>所有 Inbox 定时任务共用的 Quartz 分组。</summary>
    public const string GroupName = "inbox-workflows";

    /// <summary>JobDataMap 中保存 cron 表达式的键，供同步器判断是否需要重建触发器。</summary>
    public const string CronDataKey = "cron";

    /// <summary>JobDataMap 中保存工作流名称的键。</summary>
    public const string WorkflowDataKey = "workflow";

    public static JobKey GetJobKey(string workflowName) => new(workflowName, GroupName);

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken stoppingToken)
    {
        var workflowName = context.MergedJobDataMap.GetString(WorkflowDataKey)
            ?? context.JobDetail.Key.Name;

        var file = catalog.Find(workflowName);
        if (file?.Definition is null)
        {
            logger.LogError("定时运行跳过：工作流 {Workflow} 不存在或解析失败（{Error}）",
                workflowName, file?.Error ?? "文件已删除");
            return;
        }

        // 上一次运行仍在排队/执行/等待人工输入时不再叠加新运行，避免重复导入。
        if (await runner.HasActiveRunAsync(workflowName, stoppingToken))
        {
            logger.LogInformation("定时运行跳过：工作流 {Workflow} 已有进行中的运行", workflowName);
            return;
        }

        var scheduledAt = context.ScheduledFireTimeUtc?.ToLocalTime() ?? DateTimeOffset.Now;
        var inputs = new Dictionary<string, string>
        {
            ["trigger"] = "schedule",
            ["scheduled_at"] = scheduledAt.ToString("O")
        };

        var run = await runner.CreateRunAsync(
            file.Definition,
            file.Yaml,
            InboxTriggerType.Schedule,
            "schedule",
            inputs,
            triggerFile: null,
            triggerFileName: null,
            stoppingToken);

        logger.LogInformation("定时触发工作流 {Workflow}，创建运行 {RunId}", workflowName, run.Id);
    }
}
