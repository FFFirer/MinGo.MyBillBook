using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Inbox.Security;

/// <summary>历史数据清理结果。DryRun 时各计数表示“将会删除”的数量。</summary>
public sealed record InboxHistoryCleanupResult(
    bool DryRun,
    DateTime Cutoff,
    int RetentionDays,
    int Runs,
    int Steps,
    int Todos,
    int AuditEvents);

/// <summary>
/// 按 <c>Inbox:HistoryRetentionDays</c> 回收已结束运行、步骤、待办与审计事件。
/// 仅删除 Completed / Failed / Cancelled 的运行，活动运行永远保留；0 表示禁用。
/// </summary>
public sealed class InboxHistoryCleaner(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<InboxHistoryCleaner> logger)
{
    public int RetentionDays => Math.Max(0, configuration.GetValue("Inbox:HistoryRetentionDays", 180));

    public DateTime GetCutoff()
    {
        var days = RetentionDays;
        return days <= 0 ? DateTime.MinValue : DateTime.Now.AddDays(-days);
    }

    public async Task<InboxHistoryCleanupResult> CleanupAsync(bool dryRun, CancellationToken ct)
    {
        var days = RetentionDays;
        if (days <= 0)
            return new InboxHistoryCleanupResult(dryRun, DateTime.MinValue, 0, 0, 0, 0, 0);

        var cutoff = DateTime.Now.AddDays(-days);

        var expiredRuns = db.InboxRuns.Where(r =>
            (r.Status == InboxRunStatus.Completed
             || r.Status == InboxRunStatus.Failed
             || r.Status == InboxRunStatus.Cancelled)
            && (r.CompletedAt ?? r.CreatedAt) < cutoff);

        var expiredSteps = db.InboxStepRuns.Where(s => expiredRuns.Any(r => r.Id == s.RunId));
        var expiredTodos = db.InboxTodos.Where(t => expiredRuns.Any(r => r.Id == t.RunId));
        var expiredAudit = db.InboxAuditEvents.Where(e => e.OccurredAt < cutoff);

        int runs, steps, todos, auditEvents;
        if (dryRun)
        {
            runs = await expiredRuns.CountAsync(ct);
            steps = await expiredSteps.CountAsync(ct);
            todos = await expiredTodos.CountAsync(ct);
            auditEvents = await expiredAudit.CountAsync(ct);
        }
        else
        {
            todos = await expiredTodos.ExecuteDeleteAsync(ct);
            steps = await expiredSteps.ExecuteDeleteAsync(ct);
            runs = await expiredRuns.ExecuteDeleteAsync(ct);
            auditEvents = await expiredAudit.ExecuteDeleteAsync(ct);
        }

        var result = new InboxHistoryCleanupResult(dryRun, cutoff, days, runs, steps, todos, auditEvents);
        if (runs > 0 || auditEvents > 0)
        {
            logger.LogInformation(
                "历史数据清理{DryRun}: cutoff={Cutoff:yyyy-MM-dd}, 运行 {Runs}, 步骤 {Steps}, 待办 {Todos}, 审计事件 {AuditEvents}",
                dryRun ? "（预演）" : string.Empty,
                cutoff, runs, steps, todos, auditEvents);
        }

        return result;
    }
}
