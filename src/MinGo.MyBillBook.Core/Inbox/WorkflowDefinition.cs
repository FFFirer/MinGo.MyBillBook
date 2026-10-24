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
    /// <summary>Quartz cron 表达式（6/7 段，按服务器本地时区触发）。</summary>
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
    /// <summary>失败重试策略；未配置时不重试。</summary>
    public WorkflowRetryDefinition? Retry { get; set; }
}

/// <summary>
/// 步骤级失败重试策略。仅对 Action 返回 Failed 或抛异常时生效；
/// 等待人工输入与已取消不触发重试。
/// </summary>
public class WorkflowRetryDefinition
{
    public const int MaxAttempts = 5;
    public const int MaxDelaySeconds = 300;

    /// <summary>总尝试次数（含首次），默认 2。</summary>
    public int Attempts { get; set; } = 2;
    /// <summary>首次重试的等待秒数，默认 5。</summary>
    public int DelaySeconds { get; set; } = 5;
    /// <summary>指数退避倍数，默认 2；1 表示固定间隔。</summary>
    public double Backoff { get; set; } = 2;

    /// <summary>第 failedAttempt 次失败后到下一次尝试前的等待时间（上限 300 秒）。</summary>
    public TimeSpan GetDelay(int failedAttempt)
    {
        var exponent = Math.Max(0, failedAttempt - 1);
        var factor = Backoff > 0 ? Math.Pow(Backoff, exponent) : 1;
        var seconds = Math.Min(MaxDelaySeconds, DelaySeconds * factor);
        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }
}
