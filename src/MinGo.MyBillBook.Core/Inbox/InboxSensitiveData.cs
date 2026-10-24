namespace MinGo.MyBillBook.Core.Inbox;

/// <summary>
/// 敏感字段识别与 JSON 值加解密工具。供运行器、控制器与历史数据回填共用。
/// </summary>
public static class InboxSensitiveData
{
    /// <summary>加密值前缀。Data Protection 每次加密结果不同，前缀用于识别与避免重复加密。</summary>
    public const string Prefix = "enc:v1:";

    public static bool IsProtected(string? value)
        => !string.IsNullOrEmpty(value)
           && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// 加密字典中敏感键的值。默认跳过空值、已加密值和表达式；
    /// 运行器在执行前已求值，<paramref name="skipExpressions"/> 主要用于历史数据回填。
    /// </summary>
    public static Dictionary<string, string> ProtectValues(
        IReadOnlyDictionary<string, string> values,
        IInboxSensitiveDataProtector protector,
        bool skipExpressions = false)
    {
        var result = new Dictionary<string, string>(values.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrEmpty(value)
                || !InboxSensitiveKeys.IsSensitive(key)
                || IsProtected(value)
                || (skipExpressions && value.Contains("${{", StringComparison.Ordinal)))
            {
                result[key] = value;
                continue;
            }

            result[key] = protector.Protect(value);
        }

        return result;
    }

    /// <summary>解密字典中带前缀的敏感键值；非敏感键与历史明文保持原样。</summary>
    public static Dictionary<string, string> UnprotectValues(
        IReadOnlyDictionary<string, string> values,
        IInboxSensitiveDataProtector protector)
    {
        var result = new Dictionary<string, string>(values.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            result[key] = InboxSensitiveKeys.IsSensitive(key) && IsProtected(value)
                ? protector.Unprotect(value)
                : value;
        }

        return result;
    }

    /// <summary>整体加密一段 JSON（用于 human/input 的密码输出）。</summary>
    public static string ProtectJson(string json, IInboxSensitiveDataProtector protector)
        => IsProtected(json) ? json : protector.Protect(json);

    /// <summary>整体解密一段 JSON；非加密内容原样返回。</summary>
    public static string UnprotectJson(string json, IInboxSensitiveDataProtector protector)
        => IsProtected(json) ? protector.Unprotect(json) : json;
}

/// <summary>按键名判断是否为敏感字段。</summary>
public static class InboxSensitiveKeys
{
    private static readonly string[] Fragments =
    [
        "password", "passphrase", "secret", "token", "apikey", "api_key",
        "credential", "private_key"
    ];

    public static bool IsSensitive(string key)
        => Fragments.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase));
}
