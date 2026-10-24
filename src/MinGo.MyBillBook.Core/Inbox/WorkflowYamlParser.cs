using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MinGo.MyBillBook.Core.Inbox;

/// <summary>工作流定义解析失败（语法或语义校验）。</summary>
public class WorkflowParseException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// 工作流 YAML 解析与校验。解析采用严格模式（未知字段、重复键均报错），
/// 避免配置拼写错误被静默忽略。
/// </summary>
public static class WorkflowYamlParser
{
    private static readonly Regex StepIdPattern = new(@"^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.Compiled);

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithDuplicateKeyChecking()
        .Build();

    public static WorkflowDefinition Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            throw new WorkflowParseException("工作流 YAML 内容为空");

        WorkflowDefinition? definition;
        try
        {
            definition = Deserializer.Deserialize<WorkflowDefinition>(yaml);
        }
        catch (YamlException ex)
        {
            throw new WorkflowParseException($"YAML 语法错误: {ex.Message}", ex);
        }

        if (definition is null)
            throw new WorkflowParseException("YAML 未能解析出工作流定义");

        Normalize(definition);

        var errors = Validate(definition);
        if (errors.Count > 0)
            throw new WorkflowParseException(string.Join("；", errors));

        return definition;
    }

    /// <summary>把 YAML 中的步骤展开为全局有序的「Job + Step」列表。</summary>
    public static IReadOnlyList<WorkflowStepDefinition> FlattenSteps(WorkflowDefinition definition)
        => definition.Jobs.SelectMany(j => j.Steps).ToList();

    private static void Normalize(WorkflowDefinition definition)
    {
        definition.Name = definition.Name?.Trim() ?? string.Empty;
        definition.On ??= new WorkflowTriggerDefinition();
        definition.On.Schedule = string.IsNullOrWhiteSpace(definition.On.Schedule)
            ? null
            : definition.On.Schedule.Trim();
        definition.Jobs ??= [];

        for (var i = 0; i < definition.Jobs.Count; i++)
        {
            var job = definition.Jobs[i];
            job.Name = string.IsNullOrWhiteSpace(job.Name) ? $"job-{i + 1}" : job.Name.Trim();
            job.Steps ??= [];

            foreach (var step in job.Steps)
            {
                step.Id = step.Id?.Trim() ?? string.Empty;
                step.Name = string.IsNullOrWhiteSpace(step.Name) ? step.Id : step.Name.Trim();
                step.Uses = step.Uses?.Trim() ?? string.Empty;
                step.With ??= [];

                // 统一 key 为小写，便于 Action 参数读取时忽略大小写差异。
                step.With = new Dictionary<string, string>(
                    step.With.Select(kv => new KeyValuePair<string, string>(
                        kv.Key.Trim().ToLowerInvariant(), kv.Value ?? string.Empty)),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private static List<string> Validate(WorkflowDefinition definition)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add("缺少工作流 name");
        if (string.IsNullOrWhiteSpace(definition.On.Schedule) && !definition.On.Manual)
            errors.Add("on.manual 为 false 且未配置 on.schedule，工作流无法触发");
        if (definition.Jobs.Count == 0)
            errors.Add("至少需要一个 job");
        if (definition.Jobs.Select(j => j.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definition.Jobs.Count)
            errors.Add("job 名称重复");

        var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in definition.Jobs)
        {
            if (job.Steps.Count == 0)
                errors.Add($"job '{job.Name}' 没有任何 step");

            foreach (var step in job.Steps)
            {
                if (string.IsNullOrWhiteSpace(step.Id))
                {
                    errors.Add($"job '{job.Name}' 中存在缺少 id 的 step");
                    continue;
                }

                if (!StepIdPattern.IsMatch(step.Id))
                    errors.Add($"step id '{step.Id}' 非法：仅允许字母、数字、下划线、点和连字符，且必须以字母或下划线开头");
                if (!stepIds.Add(step.Id))
                    errors.Add($"step id '{step.Id}' 重复（全工作流内必须唯一）");
                if (string.IsNullOrWhiteSpace(step.Uses))
                    errors.Add($"step '{step.Id}' 缺少 uses");

                if (step.Retry is { } retry)
                {
                    if (retry.Attempts is < 1 or > WorkflowRetryDefinition.MaxAttempts)
                        errors.Add($"step '{step.Id}' retry.attempts 必须在 1–{WorkflowRetryDefinition.MaxAttempts} 之间");
                    if (retry.DelaySeconds is < 0 or > WorkflowRetryDefinition.MaxDelaySeconds)
                        errors.Add($"step '{step.Id}' retry.delay_seconds 必须在 0–{WorkflowRetryDefinition.MaxDelaySeconds} 之间");
                    if (double.IsNaN(retry.Backoff) || retry.Backoff is < 1 or > 10)
                        errors.Add($"step '{step.Id}' retry.backoff 必须在 1–10 之间");
                }
            }
        }

        return errors;
    }
}
