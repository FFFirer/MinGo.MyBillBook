using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;
using MinGo.MyBillBook.Hubs;

namespace MinGo.MyBillBook.Services.Inbox.Notifications;

/// <summary>
/// 通知中心服务：持久化待办提醒与运行失败告警，并通过 SignalR 广播未读数变化。
/// 通知只包含展示文案，不写入密码、Secret 或待办输入值。
/// </summary>
public sealed class InboxNotificationService(
    AppDbContext db,
    IHubContext<NotificationHub> hub,
    ILogger<InboxNotificationService> logger)
{
    public async Task<InboxNotification> NotifyTodoAsync(
        InboxTodo todo,
        string workflowName,
        CancellationToken ct)
        => await CreateAsync(InboxNotification.ForTodo(todo, workflowName, DateTime.Now), ct);

    public async Task<InboxNotification> NotifyRunFailedAsync(
        InboxRun run,
        string? stepId,
        CancellationToken ct)
        => await CreateAsync(InboxNotification.ForRunFailure(run, stepId, DateTime.Now), ct);

    public async Task<InboxNotification> CreateAsync(InboxNotification notification, CancellationToken ct)
    {
        db.InboxNotifications.Add(notification);
        await db.SaveChangesAsync(ct);

        var unread = await UnreadCountAsync(ct);
        await BroadcastCreatedAsync(notification, unread, ct);
        return notification;
    }

    public async Task<InboxNotificationListDto> ListAsync(
        bool unreadOnly,
        int take,
        CancellationToken ct)
    {
        var query = db.InboxNotifications.AsNoTracking().AsQueryable();
        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(take)
            .ToListAsync(ct);

        return new InboxNotificationListDto(
            rows.Select(ToDto).ToList(),
            await UnreadCountAsync(ct));
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct)
        => await db.InboxNotifications.CountAsync(n => !n.IsRead, ct);

    /// <summary>标记单条通知已读；返回剩余未读数，通知不存在时返回 null。</summary>
    public async Task<int?> MarkReadAsync(int id, CancellationToken ct)
    {
        var notification = await db.InboxNotifications.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (notification is null)
            return null;

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.Now;
            await db.SaveChangesAsync(ct);
        }

        var unread = await UnreadCountAsync(ct);
        await BroadcastReadAsync(id, unread, ct);
        return unread;
    }

    /// <summary>全部标记已读；返回本次更新的条数。</summary>
    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var updated = await db.InboxNotifications
            .Where(n => !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, now), ct);

        await BroadcastReadAsync(null, 0, ct);
        return updated;
    }

    /// <summary>按保留天数清理旧通知；0 表示禁用。</summary>
    public async Task<int> CleanupAsync(int retentionDays, CancellationToken ct)
    {
        if (retentionDays <= 0)
            return 0;

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        return await db.InboxNotifications
            .Where(n => n.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    public static InboxNotificationDto ToDto(InboxNotification n)
        => new(
            n.Id, n.Type, n.Level, n.Title, n.Message,
            n.RunId, n.TodoId, n.WorkflowName, n.IsRead, n.CreatedAt, n.ReadAt);

    private async Task BroadcastCreatedAsync(
        InboxNotification notification,
        int unread,
        CancellationToken ct)
    {
        try
        {
            await hub.Clients.All.SendAsync(
                "InboxNotificationCreated", ToDto(notification), unread, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "发送 InboxNotificationCreated 通知失败");
        }
    }

    private async Task BroadcastReadAsync(int? notificationId, int unread, CancellationToken ct)
    {
        try
        {
            await hub.Clients.All.SendAsync(
                "InboxNotificationsRead", notificationId ?? 0, unread, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "发送 InboxNotificationsRead 通知失败");
        }
    }
}
