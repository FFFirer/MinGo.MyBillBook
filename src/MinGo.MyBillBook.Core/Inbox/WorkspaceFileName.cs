namespace MinGo.MyBillBook.Core.Inbox;

/// <summary>
/// 工作目录文件名清洗。跨平台替换非法字符、去掉路径部分与结尾的点/空格，
/// 避免邮件附件名或上传文件名造成路径穿越或非法路径。
/// </summary>
public static class WorkspaceFileName
{
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    // 常见文件系统的单段名称上限为 255 字节，这里保守截断，避免非 ASCII 名称超限。
    private const int MaxNameLength = 150;

    public static string Sanitize(string? fileName, string fallbackPrefix = "file")
    {
        var name = fileName ?? string.Empty;

        // 同时兼容 / 与 \ 分隔（附件名常来自 Windows 邮件客户端）
        var slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0)
            name = name[(slash + 1)..];

        var chars = name.Select(c => char.IsControl(c) || InvalidChars.Contains(c) ? '_' : c).ToArray();
        name = new string(chars).Trim().TrimEnd('.', ' ');

        if (name.Length > MaxNameLength)
        {
            var extension = Path.GetExtension(name);
            if (extension.Length > 20)
                extension = string.Empty;
            name = name[..(MaxNameLength - extension.Length)] + extension;
        }

        if (string.IsNullOrEmpty(name) || name is "." or "..")
            return $"{fallbackPrefix}-{DateTime.Now:yyyyMMddHHmmssfff}.bin";

        return name;
    }
}
