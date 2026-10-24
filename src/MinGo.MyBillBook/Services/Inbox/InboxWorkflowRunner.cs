using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Inbox;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;
using MinGo.MyBillBook.Hubs;
using MinGo.MyBillBook.Services.Inbox.Security;

namespace MinGo.MyBillBook.Services.Inbox;

/// <summary>
/// 工作流运行器：顺序执行步骤、持久化步骤级追踪、处理 human/input 挂起与恢复，
/// 并通过 SignalR 广播状态变化。运行语义与 GitHub Actions 类似，但仅支持串行 Job/Step。
/// </summary>
public sealed class InboxWorkflowRunner : IInboxWorkflowRunner
{
    private const int MaxLogLength = 20_000;
    private const int MaxErrorLength = 4_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDbContext _db;
    private readonly InboxWorkspace _workspace;
    private readonly IReadOnlyDictionary<string, IInboxAction> _actions;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly IServiceProvider _services;
    private readonly IInboxSensitiveDataProtector _protector;
    private readonly InboxSecrets _secrets;
    private readonly InboxAuditLog _audit;
    private readonly ILogger<InboxWorkflowRunner> _logger;

    public InboxWorkflowRunner(
        AppDbContext db,
        InboxWorkspace workspace,
        IEnumerable<IInboxAction> actions,
        IHubContext<NotificationHub> hub,
        IServiceProvider services,
        IInboxSensitiveDataProtector protector,
        InboxSecrets secrets,
        InboxAuditLog audit,
        ILogger<InboxWorkflowRunner> logger)
    {
        _db = db;
        _workspace = workspace;
        _actions = actions.ToDictionary(a => a.Uses, StringComparer.OrdinalIgnoreCase);
        _hub = hub;
        _services = services;
        _protector = protector;
        _secrets = secrets;
        _audit = audit;
        _logger = logger;
    }

    public async Task<InboxRun> CreateRunAsync(
        WorkflowDefinition definition,
        string workflowYaml,
        InboxTriggerType triggerType,
        string triggeredBy,
        IReadOnlyDictionary<string, string>? inputs,
        Stream? triggerFile,
        string? triggerFileName,
        CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var runInputs = inputs ?? new Dictionary<string, string>();
        var run = new InboxRun
        {
            WorkflowName = definition.Name,
            WorkflowYaml = workflowYaml,
            TriggerType = triggerType,
            TriggeredBy = triggeredBy,
            InputsJson = JsonSerializer.Serialize(
                InboxSensitiveData.ProtectValues(runInputs, _protector)),
            Status = InboxRunStatus.Queued,
            CreatedAt = DateTime.Now
        };
        _db.InboxRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var order = 0;
        foreach (var job in definition.Jobs)
        {
            foreach (var step in job.Steps)
            {
                _db.InboxStepRuns.Add(new InboxStepRun
                {
                    RunId = run.Id,
                    JobName = job.Name,
                    StepId = step.Id,
                    StepName = step.Name ?? step.Id,
                    Action = step.Uses,
                    Order = order++,
                    Status = InboxStepStatus.Pending,
                    InputsJson = JsonSerializer.Serialize(step.With)
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await _audit.RecordAsync(
            "RunCreated",
            run.Id,
            workflowName: run.WorkflowName,
            actor: triggeredBy,
            detail: $"{triggerType} · {definition.Jobs.Sum(j => j.Steps.Count)} 个步骤",
            ct: ct);

        _workspace.EnsureRunWorkspace(run.Id);
        if (triggerFile is not null && !string.IsNullOrWhiteSpace(triggerFileName))
        {
            var relative = await _workspace.SaveTriggerFileAsync(run.Id, triggerFile, triggerFileName, ct);
            _logger.LogInformation("运行 {RunId} 触发文件已保存: {Relative}", run.Id, relative);
        }

        await NotifyRunAsync(run, ct);
        return run;
    }

    public async Task<bool> ProcessOnceAsync(CancellationToken ct)
    {
        if (await ResumeWaitingRunsAsync(ct))
            return true;

        return await ExecuteNextQueuedRunAsync(ct);
    }

    public async Task<int> RecoverStaleRunsAsync(CancellationToken ct)
    {
        var staleRuns = await _db.InboxRuns
            .Include(r => r.Steps)
            .Where(r => r.Status == InboxRunStatus.Running)
            .ToListAsync(ct);

        foreach (var run in staleRuns)
        {
            run.Status = InboxRunStatus.Queued;
            run.Error = null;
            foreach (var step in run.Steps.Where(s => s.Status == InboxStepStatus.Running))
            {
                step.Status = InboxStepStatus.Pending;
                step.Error = null;
                step.Log = AppendLog(step.Log, "检测到上次进程中断，步骤重新排队执行");
            }

            _logger.LogWarning("恢复中断的运行 #{RunId}（{Workflow}）", run.Id, run.WorkflowName);
        }

        if (staleRuns.Count > 0)
            await _db.SaveChangesAsync(ct);

        return staleRuns.Count;
    }

    public async Task<bool> CancelRunAsync(int runId, CancellationToken ct)
    {
        var run = await _db.InboxRuns
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

        if (run is null || run.Status is InboxRunStatus.Completed or InboxRunStatus.Failed or InboxRunStatus.Cancelled)
            return false;

        run.Status = InboxRunStatus.Cancelled;
        run.Error = "用户取消";
        run.CompletedAt = DateTime.Now;

        foreach (var step in run.Steps)
        {
            if (step.Status is InboxStepStatus.Pending or InboxStepStatus.Running or InboxStepStatus.WaitingInput)
            {
                step.Status = InboxStepStatus.Skipped;
                step.Error = "运行已取消";
                step.CompletedAt = DateTime.Now;
                step.Log = AppendLog(step.Log, "运行被用户取消");
            }
        }

        var pendingTodos = await _db.InboxTodos
            .Where(t => t.RunId == runId && t.Status == InboxTodoStatus.Pending)
            .ToListAsync(ct);
        foreach (var todo in pendingTodos)
        {
            todo.Status = InboxTodoStatus.Expired;
            todo.CompletedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(
            "RunCancelled",
            run.Id,
            workflowName: run.WorkflowName,
            actor: "manual",
            detail: "用户取消运行",
            ct: ct);
        await NotifyRunAsync(run, ct);
        return true;
    }

    public async Task<bool> RetryRunAsync(int runId, CancellationToken ct)
    {
        var run = await _db.InboxRuns
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

        if (run is null || run.Status != InboxRunStatus.Failed)
            return false;

        var failedStep = run.Steps
            .Where(s => s.Status == InboxStepStatus.Failed)
            .OrderBy(s => s.Order)
            .FirstOrDefault();

        if (failedStep is null)
            return false;

        failedStep.Status = InboxStepStatus.Pending;
        failedStep.Error = null;
        failedStep.CompletedAt = null;
        failedStep.Log = AppendLog(failedStep.Log, "手动重试：从该步骤恢复执行");

        // 失败步骤之后的步骤理论上均为 Pending；若存在遗留状态一并复位，避免恢复后卡住。
        foreach (var step in run.Steps.Where(s =>
                     s.Order > failedStep.Order
                     && s.Status is InboxStepStatus.Running or InboxStepStatus.WaitingInput))
        {
            step.Status = InboxStepStatus.Pending;
            step.Error = null;
            step.CompletedAt = null;
        }

        // human/input 过期导致的失败：移除过期待办，恢复时会重新创建待办。
        if (string.Equals(failedStep.Action, "human/input", StringComparison.OrdinalIgnoreCase))
        {
            var expired = await _db.InboxTodos
                .Where(t => t.StepRunId == failedStep.Id && t.Status == InboxTodoStatus.Expired)
                .ToListAsync(ct);
            if (expired.Count > 0)
                _db.InboxTodos.RemoveRange(expired);
        }

        run.Status = InboxRunStatus.Queued;
        run.Error = null;
        run.CompletedAt = null;

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(
            "RunRetried",
            run.Id,
            workflowName: run.WorkflowName,
            actor: "manual",
            detail: $"从步骤 {failedStep.StepId} 恢复",
            ct: ct);
        await NotifyRunAsync(run, ct);
        _logger.LogInformation("手动重试运行 #{RunId}（{Workflow}），从步骤 {StepId} 恢复",
            run.Id, run.WorkflowName, failedStep.StepId);
        return true;
    }

    public Task<bool> HasActiveRunAsync(string workflowName, CancellationToken ct)
        => _db.InboxRuns.AnyAsync(
            r => r.WorkflowName == workflowName
                 && (r.Status == InboxRunStatus.Queued
                     || r.Status == InboxRunStatus.Running
                     || r.Status == InboxRunStatus.WaitingInput),
            ct);

    private async Task<bool> ResumeWaitingRunsAsync(CancellationToken ct)
    {
        var runs = await _db.InboxRuns
            .Include(r => r.Steps)
            .Where(r => r.Status == InboxRunStatus.WaitingInput)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        if (runs.Count == 0)
            return false;

        var changed = false;
        var failures = new List<(InboxRun Run, string Reason)>();

        foreach (var run in runs)
        {
            try
            {
                var step = run.Steps.FirstOrDefault(s => s.Status == InboxStepStatus.WaitingInput);
                if (step is null)
                {
                    FailRun(run, null, "运行处于等待输入状态，但未找到等待中的步骤");
                    failures.Add((run, "等待中的步骤缺失"));
                    changed = true;
                    await NotifyRunAsync(run, ct);
                    continue;
                }

                var todo = await _db.InboxTodos.FirstOrDefaultAsync(t => t.StepRunId == step.Id, ct);
                if (todo is null)
                {
                    FailRun(run, step, "等待中的步骤缺少待办记录");
                    failures.Add((run, "等待中的步骤缺少待办记录"));
                    changed = true;
                    await NotifyRunAsync(run, ct);
                    continue;
                }

                if (todo.Status == InboxTodoStatus.Pending
                    && todo.ExpiresAt is { } expiresAt
                    && expiresAt < DateTime.Now)
                {
                    todo.Status = InboxTodoStatus.Expired;
                    FailRun(run, step, $"待办 #{todo.Id} 已过期：{todo.Title}");
                    failures.Add((run, $"待办 #{todo.Id} 已过期"));
                    changed = true;
                    await NotifyRunAsync(run, ct);
                    continue;
                }

                if (todo.Status == InboxTodoStatus.Expired)
                {
                    FailRun(run, step, $"待办 #{todo.Id} 已过期：{todo.Title}");
                    failures.Add((run, $"待办 #{todo.Id} 已过期"));
                    changed = true;
                    await NotifyRunAsync(run, ct);
                    continue;
                }

                if (todo.Status == InboxTodoStatus.Completed)
                {
                    step.Status = InboxStepStatus.Pending;
                    step.Error = null;
                    step.CompletedAt = null;
                    step.Log = AppendLog(step.Log, $"待办 #{todo.Id} 已完成，恢复运行");

                    run.Status = InboxRunStatus.Queued;
                    run.Error = null;
                    changed = true;
                    await NotifyRunAsync(run, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "恢复运行 #{RunId} 失败", run.Id);
                FailRun(run, null, $"恢复运行失败: {ex.Message}");
                changed = true;
            }
        }

        if (changed)
            await _db.SaveChangesAsync(ct);

        foreach (var (failedRun, reason) in failures)
        {
            await _audit.RecordAsync(
                "RunFailed",
                failedRun.Id,
                workflowName: failedRun.WorkflowName,
                actor: "system",
                detail: reason,
                ct: ct);
        }

        return changed;
    }

    private async Task<bool> ExecuteNextQueuedRunAsync(CancellationToken ct)
    {
        var run = await _db.InboxRuns
            .Include(r => r.Steps)
            .Where(r => r.Status == InboxRunStatus.Queued)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .FirstOrDefaultAsync(ct);

        if (run is null)
            return false;

        run.Status = InboxRunStatus.Running;
        run.StartedAt ??= DateTime.Now;
        run.Error = null;
        await _db.SaveChangesAsync(ct);
        await NotifyRunAsync(run, ct);

        try
        {
            var inputs = InboxSensitiveData.UnprotectValues(DeserializeMap(run.InputsJson), _protector);
            var orderedSteps = run.Steps.OrderBy(s => s.Order).ToList();
            var retryPolicies = LoadRetryPolicies(run.WorkflowYaml);

            foreach (var stepRun in orderedSteps)
            {
                if (stepRun.Status is InboxStepStatus.Completed or InboxStepStatus.Skipped)
                    continue;

                if (stepRun.Status == InboxStepStatus.WaitingInput)
                {
                    run.Status = InboxRunStatus.WaitingInput;
                    await _db.SaveChangesAsync(ct);
                    await NotifyRunAsync(run, ct);
                    return true;
                }

                if (!_actions.TryGetValue(stepRun.Action, out var action))
                {
                    FailRun(run, stepRun, $"未知 Action: {stepRun.Action}");
                    await _db.SaveChangesAsync(ct);
                    await RecordRunFailedAsync(run, stepRun, "未知 Action", ct);
                    await NotifyRunAsync(run, ct);
                    return true;
                }

                var evaluation = BuildEvaluationContext(run, orderedSteps, inputs);
                var storedInputs = InboxSensitiveData.UnprotectValues(
                    DeserializeMap(stepRun.InputsJson), _protector);
                var resolvedInputs = ExpressionEvaluator.EvaluateDictionary(storedInputs, evaluation);

                stepRun.InputsJson = JsonSerializer.Serialize(
                    InboxSensitiveData.ProtectValues(resolvedInputs, _protector));
                stepRun.Status = InboxStepStatus.Running;
                stepRun.StartedAt = DateTime.Now;
                stepRun.Error = null;
                stepRun.Log = AppendLog(stepRun.Log, $"开始执行 {stepRun.Action}");

                foreach (var warning in evaluation.Warnings)
                    stepRun.Log = AppendLog(stepRun.Log, $"表达式告警: {warning}");

                await _db.SaveChangesAsync(ct);
                await NotifyRunAsync(run, ct);

                var logBuilder = new StringBuilder(stepRun.Log);
                if (logBuilder.Length > 0 && logBuilder[^1] != '\n')
                    logBuilder.AppendLine();

                var actionContext = new InboxActionContext
                {
                    Services = _services,
                    RunId = run.Id,
                    StepRunId = stepRun.Id,
                    WorkflowName = run.WorkflowName,
                    JobName = stepRun.JobName,
                    StepId = stepRun.StepId,
                    StepName = stepRun.StepName,
                    With = resolvedInputs,
                    WorkspacePath = _workspace.GetRunPath(run.Id),
                    Inputs = inputs,
                    Log = line =>
                    {
                        logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {line}");
                        if (logBuilder.Length > MaxLogLength)
                            logBuilder.Remove(0, logBuilder.Length - MaxLogLength);
                    }
                };

                var retryPolicy = retryPolicies.GetValueOrDefault(stepRun.StepId);
                var maxAttempts = retryPolicy?.Attempts ?? 1;
                InboxActionResult result;

                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        result = await action.ExecuteAsync(actionContext, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "步骤 {StepId} 执行失败 (RunId={RunId}, Attempt={Attempt})",
                            stepRun.StepId, run.Id, attempt);
                        result = InboxActionResult.Failed(ex.Message);
                    }

                    stepRun.Log = TrimLog(logBuilder.ToString());

                    if (result.Outcome != InboxActionOutcome.Failed || attempt >= maxAttempts)
                        break;

                    var delay = retryPolicy!.GetDelay(attempt);
                    actionContext.Log($"第 {attempt}/{maxAttempts} 次尝试失败: {result.Error ?? "未知错误"}；" +
                                      $"{FormatDelay(delay)}后重试");
                    stepRun.Log = TrimLog(logBuilder.ToString());
                    await _db.SaveChangesAsync(ct);
                    await NotifyRunAsync(run, ct);

                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, ct);
                }

                switch (result.Outcome)
                {
                    case InboxActionOutcome.Completed:
                        stepRun.Status = InboxStepStatus.Completed;
                        stepRun.OutputsJson = SerializeStepOutputs(
                            stepRun.Action, resolvedInputs, result.Outputs);
                        stepRun.CompletedAt = DateTime.Now;
                        stepRun.Log = AppendLog(stepRun.Log, "步骤完成");
                        await _db.SaveChangesAsync(ct);
                        await NotifyRunAsync(run, ct);
                        break;

                    case InboxActionOutcome.WaitingInput:
                        stepRun.Status = InboxStepStatus.WaitingInput;
                        stepRun.CompletedAt = null;
                        stepRun.Log = AppendLog(stepRun.Log, "等待人工输入");
                        run.Status = InboxRunStatus.WaitingInput;
                        await _db.SaveChangesAsync(ct);
                        await NotifyRunAsync(run, ct);
                        return true;

                    default:
                        FailRun(run, stepRun, result.Error ?? "步骤执行失败");
                        stepRun.Log = AppendLog(stepRun.Log, $"步骤失败: {stepRun.Error}");
                        await _db.SaveChangesAsync(ct);
                        await RecordRunFailedAsync(run, stepRun, "步骤执行失败", ct);
                        await NotifyRunAsync(run, ct);
                        return true;
                }
            }

            run.Status = InboxRunStatus.Completed;
            run.CompletedAt = DateTime.Now;
            await _db.SaveChangesAsync(ct);
            await _audit.RecordAsync(
                "RunCompleted",
                run.Id,
                workflowName: run.WorkflowName,
                actor: run.TriggeredBy,
                detail: $"{orderedSteps.Count} 个步骤完成",
                ct: ct);
            await NotifyRunAsync(run, ct);
            _logger.LogInformation("运行 #{RunId}（{Workflow}）完成", run.Id, run.WorkflowName);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "运行 #{RunId} 执行失败", run.Id);
            var runningStep = run.Steps.FirstOrDefault(s => s.Status == InboxStepStatus.Running);
            FailRun(run, runningStep, ex.Message);
            await _db.SaveChangesAsync(ct);
            await RecordRunFailedAsync(run, runningStep, "运行异常", ct);
            await NotifyRunAsync(run, ct);
            return true;
        }
    }

    private WorkflowEvaluationContext BuildEvaluationContext(
        InboxRun run,
        IReadOnlyList<InboxStepRun> steps,
        IReadOnlyDictionary<string, string> inputs)
    {
        var stepMap = new Dictionary<string, StepEvaluation>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            stepMap[step.StepId] = new StepEvaluation
            {
                Status = step.Status.ToString(),
                Outputs = DeserializeMap(
                    InboxSensitiveData.UnprotectJson(step.OutputsJson, _protector))
            };
        }

        return new WorkflowEvaluationContext
        {
            Secrets = GetSecrets(),
            Steps = stepMap,
            Inputs = inputs,
            WorkspacePath = _workspace.GetRunPath(run.Id),
            RunId = run.Id
        };
    }

    private IReadOnlyDictionary<string, string> GetSecrets() => _secrets.All();

    /// <summary>
    /// human/input 且 input_type=password 的输出整段加密；其余步骤输出按原样保存。
    /// 下游通过 steps.&lt;id&gt;.outputs 引用时会在 BuildEvaluationContext 中透明解密。
    /// </summary>
    private string SerializeStepOutputs(
        string action,
        IReadOnlyDictionary<string, string> resolvedInputs,
        IReadOnlyDictionary<string, string> outputs)
    {
        var json = JsonSerializer.Serialize(outputs);
        var sensitive = action.Equals("human/input", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            resolvedInputs.GetValueOrDefault("input_type"),
                            "password",
                            StringComparison.OrdinalIgnoreCase);

        return sensitive ? InboxSensitiveData.ProtectJson(json, _protector) : json;
    }

    private Task RecordRunFailedAsync(
        InboxRun run,
        InboxStepRun? step,
        string reason,
        CancellationToken ct)
        => _audit.RecordAsync(
            "RunFailed",
            run.Id,
            workflowName: run.WorkflowName,
            actor: run.TriggeredBy,
            detail: step is null ? reason : $"{reason}（步骤 {step.StepId}）",
            ct: ct);

    private async Task NotifyRunAsync(InboxRun run, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.All.SendAsync(
                "InboxRunUpdated", run.Id, run.WorkflowName, run.Status.ToString(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "发送 InboxRunUpdated 通知失败");
        }
    }

    private static void FailRun(InboxRun run, InboxStepRun? step, string error)
    {
        var message = TrimTo(error, MaxErrorLength);
        run.Status = InboxRunStatus.Failed;
        run.Error = message;
        run.CompletedAt = DateTime.Now;

        if (step is not null)
        {
            step.Status = InboxStepStatus.Failed;
            step.Error = message;
            step.CompletedAt = DateTime.Now;
        }
    }

    /// <summary>从运行快照中读取步骤重试策略；快照解析失败时视为不重试。</summary>
    private static Dictionary<string, WorkflowRetryDefinition> LoadRetryPolicies(string workflowYaml)
    {
        try
        {
            var definition = WorkflowYamlParser.Parse(workflowYaml);
            return WorkflowYamlParser.FlattenSteps(definition)
                .Where(s => s.Retry is not null)
                .ToDictionary(s => s.Id, s => s.Retry!, StringComparer.OrdinalIgnoreCase);
        }
        catch (WorkflowParseException)
        {
            return new Dictionary<string, WorkflowRetryDefinition>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string FormatDelay(TimeSpan delay)
        => delay <= TimeSpan.Zero ? "立即" : $"{delay.TotalSeconds:0.#} 秒";

    private static Dictionary<string, string> DeserializeMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return map is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string AppendLog(string existing, string line)
    {
        var builder = new StringBuilder(existing);
        if (builder.Length > 0 && builder[^1] != '\n')
            builder.AppendLine();
        builder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {line}");
        return TrimLog(builder.ToString());
    }

    private static string TrimLog(string log)
        => log.Length <= MaxLogLength ? log : log[^MaxLogLength..];

    private static string TrimTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
