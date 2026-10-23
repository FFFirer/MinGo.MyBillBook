using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Inbox;

/// <summary>运行目录概况，供 API / UI 只读展示。</summary>
public sealed record InboxWorkspaceInfo(
    string Root,
    int RetentionDays,
    int MaxRuns,
    int RunDirectories,
    long TotalBytes,
    int ActiveRuns,
    int DirectoryErrors);

/// <summary>清理结果。DryRun 时 Deleted 表示“将会删除”的数量。</summary>
public sealed record InboxCleanupResult(
    bool DryRun,
    DateTime Cutoff,
    int RetentionDays,
    int MaxRuns,
    int Scanned,
    int Deleted,
    long DeletedBytes,
    int Kept,
    int SkippedActive,
    List<string> Failures);

/// <summary>
/// 运行目录清理器。按保留天数与最大运行数回收 <c>runs/{id}</c>，
/// 但永远保留 Queued/Running/WaitingInput 的运行目录。
/// </summary>
public sealed class InboxWorkspaceCleaner(
    AppDbContext db,
    InboxWorkspace workspace,
    IConfiguration configuration,
    ILogger<InboxWorkspaceCleaner> logger)
{
    public int RetentionDays => Math.Max(0, configuration.GetValue("Inbox:WorkspaceRetentionDays", 30));
    public int MaxRuns => Math.Max(0, configuration.GetValue("Inbox:WorkspaceMaxRuns", 200));

    public async Task<InboxWorkspaceInfo> GetInfoAsync(CancellationToken ct)
    {
        var directories = EnumerateRunDirectories();
        var active = await GetActiveRunIdsAsync(ct);
        long total = 0;
        var errors = 0;

        foreach (var directory in directories)
        {
            try
            {
                total += CalculateSize(directory.Path);
            }
            catch (Exception ex)
            {
                errors++;
                logger.LogDebug(ex, "统计运行目录大小失败: {Path}", directory.Path);
            }
        }

        return new InboxWorkspaceInfo(
            workspace.Root,
            RetentionDays,
            MaxRuns,
            directories.Count,
            total,
            active.Count,
            errors);
    }

    public async Task<InboxCleanupResult> CleanupAsync(bool dryRun, CancellationToken ct)
    {
        var retentionDays = RetentionDays;
        var maxRuns = MaxRuns;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var directories = EnumerateRunDirectories();
        var active = await GetActiveRunIdsAsync(ct);

        var candidates = new Dictionary<string, RunDirectory>(StringComparer.OrdinalIgnoreCase);
        var skippedActive = 0;

        foreach (var directory in directories)
        {
            if (directory.RunId is { } runId && active.Contains(runId))
            {
                skippedActive++;
                continue;
            }

            if (retentionDays > 0 && directory.LastWriteUtc >= cutoff)
                continue;

            candidates[directory.Path] = directory;
        }

        // 数量上限：保留最新的 N 个非活动目录，超出的按时间从旧到新删除。
        if (maxRuns > 0)
        {
            var excess = directories
                .Where(d => d.RunId is null || !active.Contains(d.RunId.Value))
                .OrderByDescending(d => d.LastWriteUtc)
                .Skip(maxRuns);

            foreach (var directory in excess)
                candidates[directory.Path] = directory;
        }

        var failures = new List<string>();
        var deleted = 0;
        long deletedBytes = 0;

        foreach (var directory in candidates.Values.OrderBy(d => d.LastWriteUtc))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var size = CalculateSize(directory.Path);
                if (!dryRun)
                {
                    Directory.Delete(directory.Path, recursive: true);
                    logger.LogInformation("已清理运行目录: {Path}（{Size}）", directory.Path, FormatSize(size));
                }

                deleted++;
                deletedBytes += size;
            }
            catch (Exception ex)
            {
                failures.Add($"{directory.Name}: {ex.Message}");
                logger.LogWarning(ex, "清理运行目录失败: {Path}", directory.Path);
            }
        }

        var result = new InboxCleanupResult(
            dryRun,
            cutoff,
            retentionDays,
            maxRuns,
            directories.Count,
            deleted,
            deletedBytes,
            directories.Count - deleted,
            skippedActive,
            failures);

        logger.LogInformation(
            "运行目录清理{DryRun}: 扫描 {Scanned}, 删除 {Deleted}（{Size}）, 保留 {Kept}, 活动目录 {Active}",
            dryRun ? "（预演）" : string.Empty,
            result.Scanned, result.Deleted, FormatSize(result.DeletedBytes), result.Kept, result.SkippedActive);

        return result;
    }

    private List<RunDirectory> EnumerateRunDirectories()
    {
        var runsPath = Path.Combine(workspace.Root, "runs");
        if (!Directory.Exists(runsPath))
            return [];

        var result = new List<RunDirectory>();
        foreach (var path in Directory.EnumerateDirectories(runsPath))
        {
            var name = Path.GetFileName(path);
            int? runId = int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;
            DateTime lastWrite;
            try
            {
                lastWrite = Directory.GetLastWriteTimeUtc(path);
            }
            catch
            {
                lastWrite = DateTime.MinValue;
            }

            result.Add(new RunDirectory(name, path, runId, lastWrite));
        }

        return result;
    }

    private async Task<HashSet<int>> GetActiveRunIdsAsync(CancellationToken ct)
    {
        var ids = await db.InboxRuns.AsNoTracking()
            .Where(r => r.Status == InboxRunStatus.Queued
                        || r.Status == InboxRunStatus.Running
                        || r.Status == InboxRunStatus.WaitingInput)
            .Select(r => r.Id)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    private static long CalculateSize(string path)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // 文件可能正被写入或已删除，忽略单个文件
            }
        }

        return total;
    }

    private static string FormatSize(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.#}MB"
            : $"{Math.Max(0, bytes / 1024d):0.#}KB";

    private sealed record RunDirectory(string Name, string Path, int? RunId, DateTime LastWriteUtc);
}
