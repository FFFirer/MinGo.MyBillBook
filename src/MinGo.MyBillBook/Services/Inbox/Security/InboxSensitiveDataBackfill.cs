using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Inbox;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Inbox.Security;

/// <summary>回填结果，用于启动日志。</summary>
public sealed record InboxBackfillResult(
    int SensitiveTodos,
    int RunInputs,
    int StepInputs,
    int StepOutputs)
{
    public int Total => SensitiveTodos + RunInputs + StepInputs + StepOutputs;
}

/// <summary>
/// 把 Phase 5 之前落库的明文敏感数据原地加密。幂等：带 <c>enc:v1:</c> 前缀的值会被跳过，
/// 表达式（<c>${{ ... }}</c>）保留原样，等待执行时求值。
/// </summary>
public sealed class InboxSensitiveDataBackfill(
    AppDbContext db,
    IInboxSensitiveDataProtector protector,
    ILogger<InboxSensitiveDataBackfill> logger)
{
    public async Task<InboxBackfillResult> RunAsync(CancellationToken ct)
    {
        var todos = await BackfillTodosAsync(ct);
        var runInputs = await BackfillRunInputsAsync(ct);
        var stepInputs = await BackfillStepInputsAsync(ct);
        var stepOutputs = await BackfillSensitiveOutputsAsync(ct);

        var result = new InboxBackfillResult(todos, runInputs, stepInputs, stepOutputs);
        if (result.Total > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "敏感数据回填完成：待办 {Todos}，运行参数 {RunInputs}，步骤输入 {StepInputs}，步骤输出 {StepOutputs}",
                result.SensitiveTodos, result.RunInputs, result.StepInputs, result.StepOutputs);
        }

        return result;
    }

    private async Task<int> BackfillTodosAsync(CancellationToken ct)
    {
        var rows = await db.InboxTodos
            .Where(t => t.IsSensitive && t.InputValue != null)
            .ToListAsync(ct);

        var changed = 0;
        foreach (var todo in rows)
        {
            if (string.IsNullOrEmpty(todo.InputValue) || InboxSensitiveData.IsProtected(todo.InputValue))
                continue;

            todo.InputValue = protector.Protect(todo.InputValue);
            changed++;
        }

        return changed;
    }

    private async Task<int> BackfillRunInputsAsync(CancellationToken ct)
    {
        var rows = await db.InboxRuns
            .Where(r => r.InputsJson != null && r.InputsJson != "" && r.InputsJson != "{}")
            .ToListAsync(ct);

        var changed = 0;
        foreach (var run in rows)
        {
            if (TryProtectMap(run.InputsJson, skipExpressions: true, out var updated))
            {
                run.InputsJson = updated;
                changed++;
            }
        }

        return changed;
    }

    private async Task<int> BackfillStepInputsAsync(CancellationToken ct)
    {
        var rows = await db.InboxStepRuns
            .Where(s => s.InputsJson != null && s.InputsJson != "" && s.InputsJson != "{}")
            .ToListAsync(ct);

        var changed = 0;
        foreach (var step in rows)
        {
            if (TryProtectMap(step.InputsJson, skipExpressions: true, out var updated))
            {
                step.InputsJson = updated;
                changed++;
            }
        }

        return changed;
    }

    /// <summary>human/input 的敏感输出以整段 JSON 加密；只处理已完成且待办标记为敏感的步骤。</summary>
    private async Task<int> BackfillSensitiveOutputsAsync(CancellationToken ct)
    {
        var sensitiveStepIds = db.InboxTodos
            .Where(t => t.IsSensitive)
            .Select(t => t.StepRunId);

        var rows = await db.InboxStepRuns
            .Where(s => sensitiveStepIds.Contains(s.Id)
                        && s.Status == InboxStepStatus.Completed
                        && s.OutputsJson != null
                        && s.OutputsJson != ""
                        && s.OutputsJson != "{}")
            .ToListAsync(ct);

        var changed = 0;
        foreach (var step in rows)
        {
            if (InboxSensitiveData.IsProtected(step.OutputsJson))
                continue;

            step.OutputsJson = InboxSensitiveData.ProtectJson(step.OutputsJson, protector);
            changed++;
        }

        return changed;
    }

    private bool TryProtectMap(string json, bool skipExpressions, out string updated)
    {
        updated = json;
        if (InboxSensitiveData.IsProtected(json))
            return false;

        Dictionary<string, string>? map;
        try
        {
            map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (map is null || map.Count == 0)
            return false;

        var protectedMap = InboxSensitiveData.ProtectValues(map, protector, skipExpressions);
        if (!protectedMap.Any(kv => !string.Equals(kv.Value, map.GetValueOrDefault(kv.Key), StringComparison.Ordinal)))
            return false;

        updated = JsonSerializer.Serialize(protectedMap);
        return true;
    }
}
