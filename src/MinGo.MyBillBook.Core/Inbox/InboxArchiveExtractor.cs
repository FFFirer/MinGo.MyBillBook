using System.Buffers;
using ICSharpCode.SharpZipLib.Zip;

namespace MinGo.MyBillBook.Core.Inbox;

/// <summary>压缩包解压失败（格式损坏、缺密码、密码错误或安全限制）。</summary>
public class InboxArchiveException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>解压结果。Files 为相对 target 目录的路径（正斜杠），按条目顺序排列。</summary>
public sealed class InboxArchiveResult
{
    public List<string> Files { get; } = [];
    public List<string> Warnings { get; } = [];
    public long BytesWritten { get; set; }
}

/// <summary>
/// ZIP 解压器。默认扁平化（只保留文件名），并做路径穿越、条目大小、
/// 总大小与条目数限制；加密条目使用调用方提供的密码。
/// </summary>
public static class InboxArchiveExtractor
{
    private const int BufferSize = 81920;

    public static async Task<InboxArchiveResult> ExtractAsync(
        string archivePath,
        string targetDirectory,
        string? password = null,
        bool flatten = true,
        long maxEntryBytes = 200L * 1024 * 1024,
        long maxTotalBytes = 500L * 1024 * 1024,
        int maxEntries = 500,
        CancellationToken ct = default)
    {
        if (!File.Exists(archivePath))
            throw new InboxArchiveException($"压缩包不存在: {Path.GetFileName(archivePath)}");
        if (maxEntryBytes <= 0 || maxTotalBytes <= 0 || maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntryBytes), "解压限制必须为正数");

        Directory.CreateDirectory(targetDirectory);
        var targetRoot = Path.GetFullPath(targetDirectory);
        var targetPrefix = targetRoot.EndsWith(Path.DirectorySeparatorChar)
            ? targetRoot
            : targetRoot + Path.DirectorySeparatorChar;

        var result = new InboxArchiveResult();
        var sawEncryptedEntry = false;
        var processed = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            using var zip = OpenZip(archivePath, password);

            foreach (ZipEntry entry in zip)
            {
                ct.ThrowIfCancellationRequested();

                if (entry.IsDirectory)
                    continue;

                if (++processed > maxEntries)
                {
                    result.Warnings.Add($"条目数超过上限 {maxEntries}，其余条目已跳过");
                    break;
                }

                var entryName = entry.Name.Replace('\\', '/');
                if (entry.IsCrypted)
                    sawEncryptedEntry = true;

                var relative = BuildRelativePath(entryName, flatten);
                if (relative is null)
                {
                    result.Warnings.Add($"跳过不安全的条目路径: {entryName}");
                    continue;
                }

                if (entry.Size > maxEntryBytes)
                {
                    result.Warnings.Add($"跳过 {relative}: 超过单文件上限 {FormatMb(maxEntryBytes)}");
                    continue;
                }

                if (result.BytesWritten + Math.Max(0, entry.Size) > maxTotalBytes)
                {
                    result.Warnings.Add($"跳过 {relative}: 解压总量将超过上限 {FormatMb(maxTotalBytes)}");
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(targetRoot, relative));
                if (!fullPath.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add($"跳过越界条目: {entryName}");
                    continue;
                }

                fullPath = GetUniquePath(fullPath);
                relative = Path.GetRelativePath(targetRoot, fullPath).Replace('\\', '/');

                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

                try
                {
                    long written;
                    await using (var input = zip.GetInputStream(entry))
                    await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write,
                                     FileShare.None, BufferSize, useAsync: true))
                    {
                        written = 0;
                        int read;
                        while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
                        {
                            written += read;
                            if (written > maxEntryBytes)
                                throw new InboxArchiveException($"超过单文件上限 {FormatMb(maxEntryBytes)}");
                            if (result.BytesWritten + written > maxTotalBytes)
                                throw new InboxArchiveException($"超过解压总量上限 {FormatMb(maxTotalBytes)}");

                            await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        }
                    }

                    result.BytesWritten += written;
                    result.Files.Add(relative);
                }
                catch (OperationCanceledException)
                {
                    TryDelete(fullPath);
                    throw;
                }
                catch (Exception ex) when (ex is ZipException or InboxArchiveException or InvalidDataException)
                {
                    TryDelete(fullPath);
                    result.Warnings.Add(entry.IsCrypted
                        ? $"解压 {relative} 失败（密码错误或加密方式不支持）: {ex.Message}"
                        : $"解压 {relative} 失败: {ex.Message}");
                }
            }
        }
        catch (ZipException ex)
        {
            throw new InboxArchiveException($"无法读取压缩包: {ex.Message}", ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (result.Files.Count == 0)
        {
            if (sawEncryptedEntry && string.IsNullOrEmpty(password))
                throw new InboxArchiveException("压缩包已加密，请提供 password（可通过 human/input 获取）");
            if (result.Warnings.Count > 0)
                throw new InboxArchiveException("未能解压出任何文件: " + result.Warnings[0]);
            throw new InboxArchiveException("压缩包内没有可解压的文件");
        }

        return result;
    }

    private static ZipFile OpenZip(string archivePath, string? password)
    {
        var zip = new ZipFile(archivePath);
        if (!string.IsNullOrEmpty(password))
            zip.Password = password;
        return zip;
    }

    /// <summary>生成安全的相对路径；路径含 .. 等越界片段时返回 null。</summary>
    private static string? BuildRelativePath(string entryName, bool flatten)
    {
        if (flatten)
            return WorkspaceFileName.Sanitize(Path.GetFileName(entryName), "extract");

        var segments = entryName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;
        if (segments.Any(s => s is "." or ".."))
            return null;

        return string.Join('/',
            segments.Select(s => WorkspaceFileName.Sanitize(s, "part")));
    }

    private static string GetUniquePath(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(fullPath);
        var extension = Path.GetExtension(fullPath);
        var candidate = fullPath;
        var index = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{fileName}-{index}{extension}");
            index++;
        }

        return candidate;
    }

    private static string FormatMb(long bytes) => $"{bytes / 1024d / 1024d:0.#}MB";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 清理失败不影响错误上报
        }
    }
}
