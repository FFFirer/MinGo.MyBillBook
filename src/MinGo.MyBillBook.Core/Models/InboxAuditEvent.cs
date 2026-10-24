namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// Inbox 安全审计事件。只记录操作与结果，不保存密码、Secret 或待办输入值。
/// </summary>
public class InboxAuditEvent
{
    public int Id { get; set; }
    public DateTime OccurredAt { get; set; }
    /// <summary>事件类型：RunCreated / RunCompleted / RunFailed / RunCancelled / RunRetried / TodoCompleted / WorkspaceCleanup / HistoryCleanup。</summary>
    public string EventType { get; set; } = string.Empty;
    public int? RunId { get; set; }
    public int? TodoId { get; set; }
    public string? WorkflowName { get; set; }
    /// <summary>触发者或操作者：manual / schedule / system。</summary>
    public string? Actor { get; set; }
    /// <summary>不含敏感值的补充说明。</summary>
    public string? Detail { get; set; }
}
