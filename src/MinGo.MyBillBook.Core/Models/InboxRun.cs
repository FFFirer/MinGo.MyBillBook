namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 一次工作流运行。保存触发时的 YAML 快照，保证运行过程中修改磁盘文件不会改变本次语义。
/// </summary>
public class InboxRun
{
    public int Id { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    /// <summary>触发时的工作流 YAML 快照。</summary>
    public string WorkflowYaml { get; set; } = string.Empty;
    public InboxTriggerType TriggerType { get; set; } = InboxTriggerType.Manual;
    public string TriggeredBy { get; set; } = "manual";
    /// <summary>触发参数（JSON），预留给后续扩展。</summary>
    public string InputsJson { get; set; } = "{}";
    public InboxRunStatus Status { get; set; } = InboxRunStatus.Queued;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }

    public ICollection<InboxStepRun> Steps { get; set; } = [];
}

/// <summary>工作流中单个步骤的执行追踪记录。</summary>
public class InboxStepRun
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string StepId { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    /// <summary>Action 名称，如 bill/import。</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>全局执行顺序（跨 Job 连续编号）。</summary>
    public int Order { get; set; }
    public InboxStepStatus Status { get; set; } = InboxStepStatus.Pending;
    /// <summary>已解析的输入参数（JSON）。</summary>
    public string InputsJson { get; set; } = "{}";
    /// <summary>步骤输出（JSON），供 steps.&lt;id&gt;.outputs.&lt;key&gt; 引用。</summary>
    public string OutputsJson { get; set; } = "{}";
    public string Log { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public InboxRun Run { get; set; } = null!;
}

/// <summary>
/// 人工介入待办。human/input 步骤创建待办后挂起运行；
/// 用户可在任意时间完成待办，Worker 轮询发现后恢复运行。
/// </summary>
public class InboxTodo
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public int StepRunId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    /// <summary>输入类型：text / password / confirm。</summary>
    public string InputType { get; set; } = "text";
    public string? Placeholder { get; set; }
    /// <summary>敏感输入（如密码），在 API 返回中掩码。</summary>
    public bool IsSensitive { get; set; }
    public InboxTodoStatus Status { get; set; } = InboxTodoStatus.Pending;
    /// <summary>用户提交的值。敏感输入不会出现在 API 响应中。</summary>
    public string? InputValue { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public InboxRun Run { get; set; } = null!;
}
