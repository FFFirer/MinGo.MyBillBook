using System.Text.Json;
using MinGo.MyBillBook.Core.Inbox;

namespace MinGo.MyBillBook.Services.Inbox.Actions;

/// <summary>
/// file/detect：按 glob 收集文件并按扩展名过滤，把结果输出给后续步骤。
/// 用于解压后定位真正的账单文件（如 extracted/**/*.csv）。
/// </summary>
public sealed class FileDetectAction(InboxWorkspace workspace) : IInboxAction
{
    public string Uses => "file/detect";

    public async Task<InboxActionResult> ExecuteAsync(InboxActionContext context, CancellationToken ct)
    {
        await Task.CompletedTask;
        ct.ThrowIfCancellationRequested();

        var pattern = context.Get("source");
        if (string.IsNullOrWhiteSpace(pattern))
            return InboxActionResult.Failed("缺少参数 source（相对工作目录的 glob，如 extracted/**/*.csv）");

        var extensions = ParseExtensions(context.Get("extensions"));
        var required = ParseBool(context.Get("required"), defaultValue: true);

        IReadOnlyList<string> files;
        try
        {
            files = workspace.ResolveFiles(context.RunId, pattern);
        }
        catch (Exception ex)
        {
            return InboxActionResult.Failed($"source 无效: {ex.Message}");
        }

        var matched = files
            .Where(f => extensions.Count == 0
                        || extensions.Contains(Path.GetExtension(f).TrimStart('.'), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var runPath = workspace.GetRunPath(context.RunId);
        var relative = matched
            .Select(f => Path.GetRelativePath(runPath, f).Replace('\\', '/'))
            .ToList();

        context.Log(extensions.Count == 0
            ? $"匹配 {pattern}: {relative.Count} 个文件"
            : $"匹配 {pattern}（扩展名 {string.Join(", ", extensions)}）: {relative.Count} 个文件");

        if (relative.Count == 0)
        {
            if (required)
                return InboxActionResult.Failed(extensions.Count == 0
                    ? $"未找到匹配文件: {pattern}"
                    : $"未找到匹配文件: {pattern}（扩展名 {string.Join(", ", extensions)}）");

            return InboxActionResult.Completed(
                ("count", "0"),
                ("files", "[]"),
                ("first_file", string.Empty),
                ("first_name", string.Empty),
                ("extensions", string.Empty));
        }

        foreach (var file in relative)
            context.Log($"  · {file}");

        var foundExtensions = relative
            .Select(f => Path.GetExtension(f).TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct()
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return InboxActionResult.Completed(
            ("count", relative.Count.ToString()),
            ("files", JsonSerializer.Serialize(relative)),
            ("first_file", relative[0]),
            ("first_name", Path.GetFileName(relative[0])),
            ("extensions", string.Join(",", foundExtensions)));
    }

    private static List<string> ParseExtensions(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', ' ', '|'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.TrimStart('.').ToLowerInvariant())
                .Where(e => e.Length > 0)
                .Distinct()
                .ToList();

    private static bool ParseBool(string? value, bool defaultValue)
        => string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "on" => true,
                "false" or "0" or "no" or "off" => false,
                _ => defaultValue
            };
}
