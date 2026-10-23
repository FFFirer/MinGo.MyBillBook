using MinGo.MyBillBook.Core.Interfaces;

namespace MinGo.MyBillBook.Services.Storage;

/// <summary>
/// 本地文件系统对象存储实现。将逻辑 key 映射为 basePath 下的子路径。
/// 写操作使用临时文件 + 原子 rename 确保并发安全。
/// </summary>
public class LocalObjectStorage : IObjectStorage, IDisposable
{
    private readonly string _basePath;
    private readonly ILogger<LocalObjectStorage> _logger;

    public LocalObjectStorage(IConfiguration configuration, ILogger<LocalObjectStorage> logger)
    {
        _basePath = configuration.GetValue<string>("ObjectStorage:Local:BasePath") ?? "data/storage";
        _logger = logger;

        if (!Path.IsPathRooted(_basePath))
            _basePath = Path.Combine(Directory.GetCurrentDirectory(), _basePath);

        // 统一为本机原生分隔符，避免配置使用正斜杠时前缀校验误判
        _basePath = Path.GetFullPath(_basePath);

        Directory.CreateDirectory(_basePath);
        _logger.LogInformation("LocalObjectStorage 初始化完成，根目录: {BasePath}", _basePath);
    }

    public async Task<string> PutAsync(string key, Stream content, string? contentType = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullPath = GetFullPath(key);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);

        // 写入临时文件后原子 rename，避免写入中断导致文件损坏
        var tempPath = fullPath + ".tmp." + Guid.NewGuid().ToString("N")[..8];
        try
        {
            // 必须在 Move 前关闭写入流：Windows 上 FileShare.None 的打开文件无法被移动
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                await content.CopyToAsync(fs, ct);
                await fs.FlushAsync(ct);
            }

            // Windows 上 Move 目标已存在会抛异常，先删再移
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            File.Move(tempPath, fullPath);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }

        _logger.LogDebug("对象已存储: {Key} -> {FullPath}", key, fullPath);
        return key;
    }

    public Task<Stream> GetAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullPath = GetFullPath(key);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"对象不存在: {key}", fullPath);

        return Task.FromResult<Stream>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var fullPath = GetFullPath(key);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogDebug("对象已删除: {Key}", key);
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Task.FromResult(File.Exists(GetFullPath(key)));
    }

    private string GetFullPath(string key)
    {
        // 规范化 key，防止路径穿越
        var normalized = key.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, normalized));

        // 必须以「根目录 + 分隔符」开头，避免 storage 与 storage-evil 这类同前缀目录误判
        var basePrefix = _basePath.EndsWith(Path.DirectorySeparatorChar)
            ? _basePath
            : _basePath + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"非法的 key（路径穿越）: {key}");

        return fullPath;
    }

    public void Dispose()
    {
        // 无需释放全局资源
    }
}
