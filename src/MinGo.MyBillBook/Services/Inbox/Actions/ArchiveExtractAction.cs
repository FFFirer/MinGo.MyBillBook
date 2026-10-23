using System.Text.Json;
using MinGo.MyBillBook.Core.Inbox;

namespace MinGo.MyBillBook.Services.Inbox.Actions;

/// <summary>
/// archive/extract：把工作目录中的 ZIP（可加密）解压到目标目录。
/// 密码通常来自前序 human/input；默认扁平化输出，并限制条目数、单文件与总量大小。
/// </summary>
public sealed class ArchiveExtractAction(
    InboxWorkspace workspace,
    ILogger<ArchiveExtractAction> logger) : IInboxAction
{
    public string Uses => "archive/extract";

    public async Task<InboxActionResult> ExecuteAsync(InboxActionContext context, CancellationToken ct)
    {
        var pattern = context.Get("source");
        if (string.IsNullOrWhiteSpace(pattern))
            return InboxActionResult.Failed("缺少参数 source（相对工作目录的压缩包路径或 glob，如 attachments/*.zip）");

        var password = context.Get("password");
        var targetDir = InboxWorkspace.NormalizeRelativeDirectory(context.Get("target"), "extracted");
        var flatten = ParseBool(context.Get("flatten"), defaultValue: true);
        var maxFileMb = ParseInt(context.Get("max_file_mb"), defaultValue: 200, min: 1, max: 2048);
        var maxTotalMb = ParseInt(context.Get("max_total_mb"), defaultValue: 500, min: 1, max: 4096);
        var maxEntries = ParseInt(context.Get("max_entries"), defaultValue: 500, min: 1, max: 10000);

        IReadOnlyList<string> archives;
        try
        {
            archives = workspace.ResolveFiles(context.RunId, pattern);
        }
        catch (Exception ex)
        {
            return InboxActionResult.Failed($"source 无效: {ex.Message}");
        }

        if (archives.Count == 0)
            return InboxActionResult.Failed($"未找到压缩包: {pattern}");

        var runPath = workspace.EnsureRunWorkspace(context.RunId);
        string targetPath;
        try
        {
            targetPath = workspace.ResolvePath(runPath, targetDir);
        }
        catch (ArgumentException ex)
        {
            return InboxActionResult.Failed($"target 无效: {ex.Message}");
        }

        Directory.CreateDirectory(targetPath);
        context.Log(
            $"解压 {archives.Count} 个压缩包到 {targetDir}/（密码: {(string.IsNullOrEmpty(password) ? "无" : "已提供")}，" +
            $"扁平化: {(flatten ? "是" : "否")}，单文件上限 {maxFileMb}MB，总量上限 {maxTotalMb}MB）");

        var saved = new List<string>();
        var warnings = new List<string>();
        var failures = new List<string>();
        long bytesWritten = 0;

        foreach (var archive in archives)
        {
            ct.ThrowIfCancellationRequested();

            var archiveName = Path.GetFileName(archive);
            try
            {
                var result = await InboxArchiveExtractor.ExtractAsync(
                    archive,
                    targetPath,
                    password,
                    flatten,
                    maxFileMb * 1024L * 1024L,
                    maxTotalMb * 1024L * 1024L,
                    maxEntries,
                    ct);

                foreach (var file in result.Files)
                {
                    saved.Add($"{targetDir}/{file}");
                    context.Log($"解压 {archiveName} → {targetDir}/{file}");
                }

                foreach (var warning in result.Warnings)
                {
                    warnings.Add($"{archiveName}: {warning}");
                    context.Log($"警告（{archiveName}）: {warning}");
                }

                bytesWritten += result.BytesWritten;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InboxArchiveException ex)
            {
                failures.Add($"{archiveName}: {ex.Message}");
                context.Log($"解压失败（{archiveName}）: {ex.Message}");
                logger.LogWarning(ex, "运行 #{RunId} 解压失败: {File}", context.RunId, archive);
            }
            catch (Exception ex)
            {
                failures.Add($"{archiveName}: {ex.Message}");
                context.Log($"解压异常（{archiveName}）: {ex.Message}");
                logger.LogWarning(ex, "运行 #{RunId} 解压异常: {File}", context.RunId, archive);
            }
        }

        if (saved.Count == 0)
        {
            if (failures.Count == 0)
                return InboxActionResult.Failed("未能解压出任何文件：压缩包内没有可解压的文件");
            return InboxActionResult.Failed(failures.Count == 1
                ? $"解压失败：{failures[0]}"
                : $"全部 {failures.Count} 个压缩包均解压失败：{string.Join("; ", failures)}");
        }

        if (failures.Count > 0)
            context.Log($"部分压缩包失败 {failures.Count} 个: {string.Join("; ", failures)}");
        if (warnings.Count > 0)
            context.Log($"解压告警 {warnings.Count} 条");

        context.Log($"完成: 解压 {saved.Count} 个文件，共 {FormatSize(bytesWritten)}");

        return InboxActionResult.Completed(
            ("count", saved.Count.ToString()),
            ("files", JsonSerializer.Serialize(saved)),
            ("first_file", saved[0]),
            ("first_name", Path.GetFileName(saved[0])),
            ("target", targetDir),
            ("bytes", bytesWritten.ToString()),
            ("warnings", warnings.Count.ToString()));
    }

    private static bool ParseBool(string? value, bool defaultValue)
        => string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "on" => true,
                "false" or "0" or "no" or "off" => false,
                _ => defaultValue
            };

    private static int ParseInt(string? value, int defaultValue, int min, int max)
        => int.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : defaultValue;

    private static string FormatSize(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.#}MB"
            : $"{Math.Max(1, bytes / 1024d):0.#}KB";
}
