using MinGo.MyBillBook.Core.Models;

namespace MinGo.MyBillBook.Core.Inbox;

/// <summary>
/// 工作流步骤动作抽象。新增接入能力 = 实现本接口并在 DI 注册，无需修改引擎。
/// </summary>
public interface IInboxAction
{
    /// <summary>Action 名称，对应 YAML 的 <c>uses</c>，如 bill/import。</summary>
    string Uses { get; }

    Task<InboxActionResult> ExecuteAsync(InboxActionContext context, CancellationToken ct);
}

/// <summary>Action 执行上下文。</summary>
public sealed class InboxActionContext
{
    public required IServiceProvider Services { get; init; }
    public required int RunId { get; init; }
    public required int StepRunId { get; init; }
    public required string WorkflowName { get; init; }
    public required string JobName { get; init; }
    public required string StepId { get; init; }
    public required string StepName { get; init; }
    /// <summary>已解析表达式的参数。</summary>
    public required IReadOnlyDictionary<string, string> With { get; init; }
    /// <summary>本次运行的隔离工作目录（绝对路径）。</summary>
    public required string WorkspacePath { get; init; }
    /// <summary>触发时传入的参数。</summary>
    public required IReadOnlyDictionary<string, string> Inputs { get; init; }
    /// <summary>写步骤日志（由 Runner 持久化）。</summary>
    public required Action<string> Log { get; init; }

    public string? Get(string key)
        => With.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

public enum InboxActionOutcome
{
    Completed = 0,
    WaitingInput = 1,
    Failed = 2
}

/// <summary>Action 执行结果。</summary>
public sealed class InboxActionResult
{
    public InboxActionOutcome Outcome { get; init; }
    public Dictionary<string, string> Outputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Error { get; init; }

    public static InboxActionResult Completed(params (string Key, string Value)[] outputs)
    {
        var result = new InboxActionResult { Outcome = InboxActionOutcome.Completed };
        foreach (var (key, value) in outputs)
            result.Outputs[key] = value;
        return result;
    }

    public static InboxActionResult Waiting() => new() { Outcome = InboxActionOutcome.WaitingInput };

    public static InboxActionResult Failed(string error)
        => new() { Outcome = InboxActionOutcome.Failed, Error = error };
}

/// <summary>工作流运行器接口（由 Server 实现，Controller 与 Worker 使用）。</summary>
public interface IInboxWorkflowRunner
{
    /// <summary>创建一次运行并入队。可选地把触发文件写入工作目录 input/。</summary>
    Task<InboxRun> CreateRunAsync(
        WorkflowDefinition definition,
        string workflowYaml,
        InboxTriggerType triggerType,
        string triggeredBy,
        IReadOnlyDictionary<string, string>? inputs,
        Stream? triggerFile,
        string? triggerFileName,
        CancellationToken ct);

    /// <summary>处理一次轮询周期：优先恢复已完成的待办，否则执行一个 Queued 运行。返回是否有进展。</summary>
    Task<bool> ProcessOnceAsync(CancellationToken ct);

    /// <summary>启动时恢复上次进程中断留下的 Running 运行（重置为 Queued，Running 步骤重置为 Pending）。</summary>
    Task<int> RecoverStaleRunsAsync(CancellationToken ct);

    /// <summary>取消运行；Queued / WaitingInput 可取消，关联 Pending 待办置为 Expired。</summary>
    Task<bool> CancelRunAsync(int runId, CancellationToken ct);
}
