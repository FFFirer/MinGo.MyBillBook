namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// Inbox 通知中心消息。持久化保存，支持未读徽标、已读状态与运行跳转。
/// 只保存展示文案，不保存密码、Secret 或待办输入值。
/// </summary>
public class InboxNotification
{
    public const string TypeTodoPending = "TodoPending";
    public const string TypeRunFailed = "RunFailed";

    public const string LevelWarning = "Warning";
    public const string LevelError = "Error";

    private const int MaxTitleLength = 200;
    private const int MaxMessageLength = 1_000;

    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>通知类型：TodoPending / RunFailed。</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>级别：Warning / Error，用于图标与配色。</summary>
    public string Level { get; set; } = LevelWarning;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? RunId { get; set; }
    public int? TodoId { get; set; }
    public string? WorkflowName { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    public static InboxNotification ForTodo(InboxTodo todo, string workflowName, DateTime now) => new()
    {
        CreatedAt = now,
        Type = TypeTodoPending,
        Level = LevelWarning,
        Title = Trim($"待办：{todo.Title}", MaxTitleLength),
        Message = Trim(string.IsNullOrWhiteSpace(todo.Prompt) ? todo.Title : todo.Prompt, MaxMessageLength),
        RunId = todo.RunId,
        TodoId = todo.Id,
        WorkflowName = workflowName,
        IsRead = false
    };

    public static InboxNotification ForRunFailure(
        InboxRun run,
        string? stepId,
        DateTime now)
    {
        var detail = string.IsNullOrWhiteSpace(run.Error) ? "运行执行失败" : run.Error;
        var message = string.IsNullOrWhiteSpace(stepId)
            ? detail
            : $"步骤 {stepId}：{detail}";

        return new InboxNotification
        {
            CreatedAt = now,
            Type = TypeRunFailed,
            Level = LevelError,
            Title = Trim($"工作流运行失败：{run.WorkflowName}", MaxTitleLength),
            Message = Trim(message, MaxMessageLength),
            RunId = run.Id,
            WorkflowName = run.WorkflowName,
            IsRead = false
        };
    }

    private static string Trim(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
