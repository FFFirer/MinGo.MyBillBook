namespace MinGo.MyBillBook.Core.Interfaces;

/// <summary>
/// 对象存储抽象层。默认实现为本地文件系统（LocalObjectStorage），
/// 可扩展为 S3、MinIO 等外部对象存储服务。
/// key 为逻辑路径（如 "bills/2025/03/abc.csv"），实现负责映射到实际存储位置。
/// </summary>
public interface IObjectStorage
{
    /// <summary>写入对象。返回存储后的 key。</summary>
    Task<string> PutAsync(string key, Stream content, string? contentType = null, CancellationToken ct = default);

    /// <summary>读取对象流。调用方负责 dispose。</summary>
    Task<Stream> GetAsync(string key, CancellationToken ct = default);

    /// <summary>删除对象。不存在时不抛异常。</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>对象是否存在。</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
