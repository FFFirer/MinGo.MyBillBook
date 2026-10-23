namespace MinGo.MyBillBook.Core.Inbox;

/// <summary>
/// 工作流 YAML 的映射模型。字段命名采用下划线约定（job 名称、step 的 id/uses/with）。
/// </summary>
public class WorkflowDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public WorkflowTriggerDefinition On { get; set; } = new();
    public List<WorkflowJobDefinition> Jobs { get; set; } = [];
}

public class WorkflowTriggerDefinition
{
    /// <summary>是否允许手动触发，默认允许。</summary>
    public bool Manual { get; set; } = true;
    /// <summary>Quartz cron 表达式（Phase 4 生效）。</summary>
    public string? Schedule { get; set; }
}

public class WorkflowJobDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<WorkflowStepDefinition> Steps { get; set; } = [];
}

public class WorkflowStepDefinition
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Uses { get; set; } = string.Empty;
    public Dictionary<string, string> With { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
