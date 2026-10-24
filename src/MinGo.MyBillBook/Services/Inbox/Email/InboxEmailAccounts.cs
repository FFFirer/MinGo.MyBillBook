using MailKit.Security;
using MinGo.MyBillBook.Services.Inbox.Security;

namespace MinGo.MyBillBook.Services.Inbox.Email;

/// <summary>appsettings 中单个邮箱账号的原始配置。</summary>
public sealed class InboxEmailAccountOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    /// <summary>ssl / starttls / starttls-when-available / auto / none</summary>
    public string Security { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    /// <summary>直接填写密码/授权码；建议改用 PasswordSecret。</summary>
    public string Password { get; set; } = string.Empty;
    /// <summary>引用 Inbox:Secrets:&lt;key&gt;，避免密码明文写在配置文件中。</summary>
    public string? PasswordSecret { get; set; }
    public string Folder { get; set; } = "INBOX";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>校验通过、可直接用于 IMAP 连接的账号。</summary>
public sealed record ResolvedEmailAccount(
    string Name,
    string Host,
    int Port,
    SecureSocketOptions Security,
    string SecurityText,
    string Username,
    string Password,
    string Folder,
    int TimeoutSeconds,
    string PasswordSource);

/// <summary>供只读 UI 展示的账号信息（不含密码）。</summary>
public sealed record InboxEmailAccountInfo(
    string Name,
    string Host,
    int Port,
    string Security,
    string Username,
    string Folder,
    bool Configured,
    bool HasPassword,
    string PasswordSource,
    string? Error);

/// <summary>
/// 邮箱账号配置读取器。账号是基础设施配置，随 appsettings / 环境变量提供，
/// 不落数据库、不提供运行时 CRUD；密码可通过 Inbox:Secrets 间接引用。
/// </summary>
public sealed class InboxEmailAccounts
{
    private readonly IReadOnlyDictionary<string, ResolvedEmailAccount> _accounts;
    private readonly IReadOnlyList<InboxEmailAccountInfo> _infos;

    public InboxEmailAccounts(
        IConfiguration configuration,
        InboxSecrets secrets,
        ILogger<InboxEmailAccounts> logger)
    {
        var accounts = new Dictionary<string, ResolvedEmailAccount>(StringComparer.OrdinalIgnoreCase);
        var infos = new List<InboxEmailAccountInfo>();

        foreach (var child in configuration.GetSection("Inbox:EmailAccounts").GetChildren())
        {
            var name = child.Key.Trim();
            if (name.Length == 0)
                continue;

            var options = child.Get<InboxEmailAccountOptions>() ?? new InboxEmailAccountOptions();
            var port = options.Port is > 0 and <= 65535
                ? options.Port
                : 993;
            var (security, securityText) = ParseSecurity(options.Security, port);
            var passwordSource = "missing";
            var password = string.Empty;
            string? passwordError = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(options.PasswordSecret))
                {
                    password = secrets.Get(options.PasswordSecret) ?? string.Empty;
                    passwordSource = $"secret:{options.PasswordSecret}";
                }
                else if (!string.IsNullOrWhiteSpace(options.Password))
                {
                    password = secrets.Unprotect(
                        options.Password,
                        $"Inbox:EmailAccounts:{name}:Password");
                    passwordSource = "config";
                }
            }
            catch (Exception ex)
            {
                // 解密失败按配置不完整处理，避免带着空密码连接并给出误导性错误。
                passwordError = ex.Message;
            }

            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(options.Host))
                errors.Add("缺少 Host");
            if (security is null)
                errors.Add($"Security 无效: {options.Security}（可选 ssl / starttls / auto / none）");
            if (string.IsNullOrWhiteSpace(options.Username))
                errors.Add("缺少 Username");
            if (passwordError is not null)
                errors.Add(passwordError);
            else if (string.IsNullOrWhiteSpace(password))
                errors.Add(string.IsNullOrWhiteSpace(options.PasswordSecret)
                    ? "缺少 Password 或 PasswordSecret"
                    : $"未找到 Secret: {options.PasswordSecret}");

            var folder = string.IsNullOrWhiteSpace(options.Folder) ? "INBOX" : options.Folder.Trim();
            var timeout = options.TimeoutSeconds is > 0 and <= 600 ? options.TimeoutSeconds : 30;
            var configured = errors.Count == 0;

            infos.Add(new InboxEmailAccountInfo(
                name,
                options.Host?.Trim() ?? string.Empty,
                port,
                securityText,
                options.Username?.Trim() ?? string.Empty,
                folder,
                configured,
                password.Length > 0,
                passwordSource,
                configured ? null : string.Join("；", errors)));

            if (!configured)
            {
                logger.LogWarning("邮箱账号 {Account} 配置不完整：{Errors}", name, string.Join("；", errors));
                continue;
            }

            accounts[name] = new ResolvedEmailAccount(
                name,
                options.Host!.Trim(),
                port,
                security!.Value,
                securityText,
                options.Username!.Trim(),
                password,
                folder,
                timeout,
                passwordSource);
        }

        _accounts = accounts;
        _infos = infos;

        if (infos.Count > 0)
            logger.LogInformation("已加载 {Count} 个邮箱账号配置（可用 {Usable} 个）", infos.Count, accounts.Count);
    }

    /// <summary>只读账号列表，供 API / UI 展示；密码永远不返回。</summary>
    public IReadOnlyList<InboxEmailAccountInfo> List() => _infos;

    /// <summary>按名称查找可用账号（大小写不敏感）。</summary>
    public ResolvedEmailAccount? Find(string name)
        => _accounts.TryGetValue(name, out var account) ? account : null;

    private static (SecureSocketOptions? Security, string Text) ParseSecurity(string? raw, int port)
    {
        var text = string.IsNullOrWhiteSpace(raw)
            ? port == 143 ? "starttls" : "ssl"
            : raw.Trim().ToLowerInvariant();

        SecureSocketOptions? security = text switch
        {
            "ssl" or "ssl-on-connect" => SecureSocketOptions.SslOnConnect,
            "starttls" => SecureSocketOptions.StartTls,
            "starttls-when-available" => SecureSocketOptions.StartTlsWhenAvailable,
            "auto" => SecureSocketOptions.Auto,
            "none" => SecureSocketOptions.None,
            _ => null
        };

        return (security, text);
    }
}
