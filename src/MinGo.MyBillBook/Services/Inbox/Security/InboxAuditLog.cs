using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Inbox.Security;

/// <summary>写入 Inbox 安全审计事件。Detail 字段由调用方保证不含敏感值。</summary>
public sealed class InboxAuditLog(AppDbContext db, ILogger<InboxAuditLog> logger)
{
    public async Task RecordAsync(
        string eventType,
        int? runId = null,
        int? todoId = null,
        string? workflowName = null,
        string? actor = null,
        string? detail = null,
        CancellationToken ct = default)
    {
        var entry = new InboxAuditEvent
        {
            OccurredAt = DateTime.Now,
            EventType = eventType,
            RunId = runId,
            TodoId = todoId,
            WorkflowName = workflowName,
            Actor = actor,
            Detail = Truncate(detail, 500)
        };

        db.InboxAuditEvents.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // 审计失败不改变业务结果；分离实体避免污染后续 SaveChanges。
            db.Entry(entry).State = EntityState.Detached;
            logger.LogWarning(ex, "写入审计事件失败: {EventType}", eventType);
        }
    }

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
}
