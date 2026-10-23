using System.Text.RegularExpressions;

namespace MinGo.MyBillBook.Core.Inbox;

/// <summary>单个步骤在表达式求值时的可见快照。</summary>
public sealed class StepEvaluation
{
    public string Status { get; init; } = "Pending";
    public IReadOnlyDictionary<string, string> Outputs { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>表达式求值上下文。</summary>
public sealed class WorkflowEvaluationContext
{
    public IReadOnlyDictionary<string, string> Secrets { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, StepEvaluation> Steps { get; init; } =
        new Dictionary<string, StepEvaluation>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> Inputs { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string WorkspacePath { get; init; } = string.Empty;
    public int RunId { get; init; }

    /// <summary>无法解析的表达式会记录在这里（解析为空字符串），便于排查配置错误。</summary>
    public List<string> Warnings { get; } = [];
}

/// <summary>
/// 轻量表达式求值器，支持 <c>${{ secrets.KEY }}</c>、<c>${{ steps.id.outputs.key }}</c>、
/// <c>${{ steps.id.status }}</c>、<c>${{ workspace }}</c>、<c>${{ run.id }}</c>、<c>${{ inputs.key }}</c>。
/// 未知路径解析为空字符串并记录告警，与 GitHub Actions 的宽容行为一致。
/// </summary>
public static class ExpressionEvaluator
{
    private static readonly Regex ExpressionPattern = new(
        @"\$\{\{\s*(?<expr>.+?)\s*\}\}", RegexOptions.Compiled);

    public static string Evaluate(string? template, WorkflowEvaluationContext context)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        return ExpressionPattern.Replace(template, match =>
            Resolve(match.Groups["expr"].Value, context));
    }

    /// <summary>对整个字典的 value 做求值，返回新字典。</summary>
    public static Dictionary<string, string> EvaluateDictionary(
        IReadOnlyDictionary<string, string> source,
        WorkflowEvaluationContext context)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
            result[key] = Evaluate(value, context);
        return result;
    }

    public static string Resolve(string expression, WorkflowEvaluationContext context)
    {
        var expr = expression.Trim();
        if (expr.Length == 0)
        {
            Warn(context, expression, "表达式为空");
            return string.Empty;
        }

        var segments = expr.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var root = segments[0].ToLowerInvariant();

        switch (root)
        {
            case "secrets":
                if (segments.Length == 2 && context.Secrets.TryGetValue(segments[1], out var secret))
                    return secret;
                break;

            case "steps":
                if (segments.Length >= 3 && context.Steps.TryGetValue(segments[1], out var step))
                {
                    var field = segments[2].ToLowerInvariant();
                    if (field == "status" && segments.Length == 3)
                        return step.Status;
                    if (field == "outputs" && segments.Length >= 4)
                    {
                        var outputKey = string.Join('.', segments.Skip(3));
                        if (step.Outputs.TryGetValue(outputKey, out var output))
                            return output;
                    }
                }
                break;

            case "workspace":
                if (segments.Length == 1)
                    return context.WorkspacePath;
                break;

            case "run":
                if (segments.Length == 2 && segments[1].Equals("id", StringComparison.OrdinalIgnoreCase))
                    return context.RunId.ToString();
                break;

            case "inputs":
                if (segments.Length == 2 && context.Inputs.TryGetValue(segments[1], out var input))
                    return input;
                break;
        }

        Warn(context, expression, "未知表达式路径");
        return string.Empty;
    }

    private static void Warn(WorkflowEvaluationContext context, string expression, string reason)
        => context.Warnings.Add($"{reason}: {expression.Trim()}");
}
