using MinGo.MyBillBook.Core.Inbox;

namespace MinGo.MyBillBook.Services.Inbox.Security;

/// <summary>
/// 读取 <c>Inbox:Secrets</c> 中的 Secret，并透明解密 <c>enc:v1:</c> 前缀的值。
/// Secret 仍是配置项（appsettings / 环境变量 / 用户机密），不引入数据库 CRUD。
/// </summary>
public sealed class InboxSecrets(
    IConfiguration configuration,
    IInboxSensitiveDataProtector protector,
    ILogger<InboxSecrets> logger)
{
    public IReadOnlyDictionary<string, string> All()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in configuration.GetSection("Inbox:Secrets").GetChildren())
        {
            if (string.IsNullOrEmpty(child.Key))
                continue;

            try
            {
                result[child.Key] = protector.Unprotect(child.Value ?? string.Empty);
            }
            catch (Exception ex)
            {
                // 单个 Secret 解密失败不应阻止整个工作流启动；引用时会得到空值并留日志。
                logger.LogError(ex, "Secret {Key} 解密失败", child.Key);
            }
        }

        return result;
    }

    /// <summary>按名称读取 Secret；不存在返回 null，解密失败抛出可操作异常。</summary>
    public string? Get(string name)
    {
        var raw = configuration[$"Inbox:Secrets:{name}"];
        return raw is null ? null : Unprotect(raw, $"Inbox:Secrets:{name}");
    }

    /// <summary>解密任意配置值（用于 PasswordSecret / Password 等）。</summary>
    public string Unprotect(string value, string source)
    {
        try
        {
            return protector.Unprotect(value);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"解密 {source} 失败：{ex.Message}", ex);
        }
    }

    public InboxSecretStats Stats()
    {
        var total = 0;
        var encrypted = 0;
        foreach (var child in configuration.GetSection("Inbox:Secrets").GetChildren())
        {
            if (string.IsNullOrEmpty(child.Key))
                continue;
            total++;
            if (InboxSensitiveData.IsProtected(child.Value))
                encrypted++;
        }

        return new InboxSecretStats(total, encrypted, total - encrypted);
    }
}

public sealed record InboxSecretStats(int Total, int Encrypted, int Plaintext);
