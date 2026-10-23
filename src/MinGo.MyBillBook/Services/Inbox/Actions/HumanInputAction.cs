using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Inbox;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;
using MinGo.MyBillBook.Hubs;

namespace MinGo.MyBillBook.Services.Inbox.Actions;

/// <summary>
/// human/input：创建人工待办并挂起运行。待办是持久化的，用户可在任意时间、
/// 任意设备完成任务；Runner 下一轮轮询发现待办已完成即恢复执行，本 Action 再次被调用时
/// 直接读取用户输入作为输出。
/// </summary>
public sealed class HumanInputAction(
    AppDbContext db,
    IHubContext<NotificationHub> hub,
    ILogger<HumanInputAction> logger) : IInboxAction
{
    public string Uses => "human/input";

    public async Task<InboxActionResult> ExecuteAsync(InboxActionContext context, CancellationToken ct)
    {
        var existing = await db.InboxTodos
            .FirstOrDefaultAsync(t => t.StepRunId == context.StepRunId, ct);

        if (existing is not null)
        {
            switch (existing.Status)
            {
                case InboxTodoStatus.Completed:
                    context.Log($"待办 #{existing.Id} 已完成，读取用户输入");
                    return InboxActionResult.Completed(("value", existing.InputValue ?? string.Empty));

                case InboxTodoStatus.Expired:
                    return InboxActionResult.Failed($"待办 #{existing.Id} 已过期：{existing.Title}");

                default:
                    context.Log($"已存在待办 #{existing.Id}，继续等待");
                    return InboxActionResult.Waiting();
            }
        }

        var inputType = (context.Get("input_type") ?? "text").Trim().ToLowerInvariant();
        if (inputType is not ("text" or "password" or "confirm"))
            inputType = "text";

        var title = TrimTo(context.Get("title") ?? context.StepName, 200) ?? string.Empty;
        var prompt = TrimTo(context.Get("prompt") ?? "请提供输入", 2_000) ?? string.Empty;

        DateTime? expiresAt = null;
        var expiresRaw = context.Get("expires_hours");
        if (!string.IsNullOrWhiteSpace(expiresRaw)
            && double.TryParse(expiresRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)
            && hours > 0)
        {
            expiresAt = DateTime.Now.AddHours(hours);
        }

        var todo = new InboxTodo
        {
            RunId = context.RunId,
            StepRunId = context.StepRunId,
            Title = title,
            Prompt = prompt,
            InputType = inputType,
            Placeholder = TrimTo(context.Get("placeholder"), 200),
            IsSensitive = inputType == "password",
            Status = InboxTodoStatus.Pending,
            CreatedAt = DateTime.Now,
            ExpiresAt = expiresAt
        };

        db.InboxTodos.Add(todo);
        await db.SaveChangesAsync(ct);

        context.Log($"已创建待办 #{todo.Id}: {todo.Title}");
        logger.LogInformation("运行 #{RunId} 创建待办 #{TodoId}: {Title}", context.RunId, todo.Id, todo.Title);

        await NotifyTodoCreatedAsync(todo, ct);
        return InboxActionResult.Waiting();
    }

    private async Task NotifyTodoCreatedAsync(InboxTodo todo, CancellationToken ct)
    {
        try
        {
            await hub.Clients.All.SendAsync(
                "InboxTodoCreated", todo.Id, todo.Title, todo.RunId, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "发送 InboxTodoCreated 通知失败");
        }
    }

    private static string? TrimTo(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
