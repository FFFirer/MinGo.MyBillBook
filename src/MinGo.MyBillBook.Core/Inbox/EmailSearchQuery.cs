using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MinGo.MyBillBook.Core.Inbox;

/// <summary>
/// 邮件检索条件。语法与 GitHub Actions 的易用性一致：
/// <c>from:alipay subject:账单 since:30d unseen</c>。
/// 未指定 unseen/all 时默认只取未读邮件。
/// </summary>
public sealed class EmailSearchQuery
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "yyyyMMdd"];

    /// <summary>是否只检索未读邮件。</summary>
    public bool UnreadOnly { get; init; } = true;

    /// <summary>发件人包含的文本。</summary>
    public string? FromContains { get; init; }

    /// <summary>主题包含的文本。</summary>
    public string? SubjectContains { get; init; }

    /// <summary>起始日期（含）。</summary>
    public DateTime? Since { get; init; }

    /// <summary>截止日期（不含）。</summary>
    public DateTime? Before { get; init; }

    /// <summary>
    /// 解析检索表达式。支持空格分隔的多个条件（AND 关系）、双引号包裹含空格的文本，
    /// 以及 from: / subject: / since: / before: / unseen / all。
    /// 无效条件抛出 <see cref="ArgumentException"/>，由调用方转为步骤失败信息。
    /// </summary>
    public static EmailSearchQuery Parse(string? text, DateTime? now = null)
    {
        var reference = (now ?? DateTime.Now).Date;
        bool unreadOnly = true, sawUnseen = false, sawAll = false;
        string? from = null, subject = null;
        DateTime? since = null, before = null;

        foreach (var token in Tokenize(text ?? string.Empty))
        {
            var separator = token.IndexOf(':');
            var key = (separator > 0 ? token[..separator] : token).Trim().ToLowerInvariant();
            var value = separator > 0 ? token[(separator + 1)..].Trim() : string.Empty;

            switch (key)
            {
                case "all":
                    if (sawUnseen)
                        throw new ArgumentException("检索条件不能同时包含 unseen 与 all");
                    sawAll = true;
                    unreadOnly = false;
                    break;

                case "unseen":
                case "unread":
                    if (sawAll)
                        throw new ArgumentException("检索条件不能同时包含 unseen 与 all");
                    sawUnseen = true;
                    unreadOnly = true;
                    break;

                case "from":
                    from = RequireValue(value, token);
                    break;

                case "subject":
                    subject = RequireValue(value, token);
                    break;

                case "since":
                    since = ParseDate(RequireValue(value, token), reference, "since");
                    break;

                case "before":
                    before = ParseDate(RequireValue(value, token), reference, "before");
                    break;

                default:
                    throw new ArgumentException(
                        $"无法识别的检索条件: {token}（支持 from:、subject:、since:、before:、unseen、all）");
            }
        }

        if (since.HasValue && before.HasValue && since.Value >= before.Value)
            throw new ArgumentException("since 必须早于 before");

        return new EmailSearchQuery
        {
            UnreadOnly = unreadOnly,
            FromContains = from,
            SubjectContains = subject,
            Since = since,
            Before = before
        };
    }

    /// <summary>用于步骤日志的可读描述。</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (UnreadOnly)
            parts.Add("仅未读");
        else
            parts.Add("全部邮件");
        if (!string.IsNullOrWhiteSpace(FromContains))
            parts.Add($"from 包含“{FromContains}”");
        if (!string.IsNullOrWhiteSpace(SubjectContains))
            parts.Add($"主题包含“{SubjectContains}”");
        if (Since is { } since)
            parts.Add($"自 {since:yyyy-MM-dd}");
        if (Before is { } before)
            parts.Add($"截至 {before:yyyy-MM-dd}（不含）");
        return string.Join("，", parts);
    }

    private static string RequireValue(string value, string token)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"检索条件缺少值: {token}")
            : value;

    private static DateTime ParseDate(string value, DateTime reference, string key)
    {
        if (string.Equals(value, "today", StringComparison.OrdinalIgnoreCase))
            return reference;

        var relative = Regex.Match(value, @"^(?<n>\d{1,4})(?<unit>[dwm])$", RegexOptions.IgnoreCase);
        if (relative.Success)
        {
            var n = int.Parse(relative.Groups["n"].Value, CultureInfo.InvariantCulture);
            if (n <= 0 || n > 3650)
                throw new ArgumentException($"{key}: 相对天数超出范围: {value}");
            return relative.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "d" => reference.AddDays(-n),
                "w" => reference.AddDays(-7 * n),
                _ => reference.AddMonths(-n)
            };
        }

        if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return date.Date;

        throw new ArgumentException(
            $"{key}: 日期格式无效: {value}（支持 yyyy-MM-dd、30d、2w、1m、today）");
    }

    /// <summary>按空白拆分，双引号内保留空格，支持 \" 转义。</summary>
    private static IReadOnlyList<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\\' && inQuotes && i + 1 < text.Length && text[i + 1] == '"')
            {
                current.Append('"');
                i++;
                continue;
            }

            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (inQuotes)
            throw new ArgumentException("检索条件中的引号未闭合");
        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }
}
