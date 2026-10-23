using System.Globalization;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using MinGo.MyBillBook.Core.Inbox;

namespace MinGo.MyBillBook.Services.Inbox;

/// <summary>
/// 工作流运行目录管理。每次运行拥有隔离目录 runs/{runId}/，
/// 所有文件访问统一经过路径规范化与防穿越校验。
/// </summary>
public sealed class InboxWorkspace
{
    private readonly string _root;
    private readonly ILogger<InboxWorkspace> _logger;

    public InboxWorkspace(IConfiguration configuration, ILogger<InboxWorkspace> logger)
    {
        _logger = logger;
        var configured = configuration.GetValue<string>("Inbox:WorkspacePath") ?? "data/inbox-workspace";
        _root = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(Directory.GetCurrentDirectory(), configured));
        Directory.CreateDirectory(_root);
        _logger.LogInformation("InboxWorkspace 根目录: {Root}", _root);
    }

    public string Root => _root;

    public string GetRunPath(int runId)
        => Path.Combine(_root, "runs", runId.ToString(CultureInfo.InvariantCulture));

    /// <summary>创建运行目录结构（input / attachments / extracted），返回运行根目录。</summary>
    public string EnsureRunWorkspace(int runId)
    {
        var runPath = GetRunPath(runId);
        Directory.CreateDirectory(Path.Combine(runPath, "input"));
        Directory.CreateDirectory(Path.Combine(runPath, "attachments"));
        Directory.CreateDirectory(Path.Combine(runPath, "extracted"));
        return runPath;
    }

    /// <summary>把触发文件写入 input/，返回相对路径（正斜杠）。</summary>
    public async Task<string> SaveTriggerFileAsync(int runId, Stream content, string fileName, CancellationToken ct)
    {
        var runPath = EnsureRunWorkspace(runId);
        var safeName = WorkspaceFileName.Sanitize(fileName, "input");
        var relative = $"input/{safeName}";
        var fullPath = ResolvePath(runPath, relative);

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        await content.CopyToAsync(fs, ct);
        return relative;
    }

    /// <summary>
    /// 按 glob 模式（相对运行目录）解析文件，返回绝对路径（按名称排序）。
    /// 支持 input/*、extracted/*.csv、attachments/**/*.zip 等写法。
    /// </summary>
    public IReadOnlyList<string> ResolveFiles(int runId, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("文件路径不能为空", nameof(pattern));

        var runPath = GetRunPath(runId);
        if (!Directory.Exists(runPath))
            return [];

        var normalized = pattern.Replace('\\', '/').Trim();
        if (normalized.StartsWith('/') || normalized.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException($"非法的文件模式（不允许绝对路径或 ..）: {pattern}", nameof(pattern));

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(normalized);

        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(runPath)));
        return result.Files
            .Select(f => ResolvePath(runPath, f.Path))
            .Where(File.Exists)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>解析相对路径并确保位于运行目录内。</summary>
    public string ResolvePath(string runPath, string relative)
    {
        var normalized = relative.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(runPath, normalized));

        var prefix = runPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"非法的路径（越出运行目录）: {relative}");

        return fullPath;
    }
}
