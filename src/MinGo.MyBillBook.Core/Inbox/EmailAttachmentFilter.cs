using System.IO.Enumeration;

namespace MinGo.MyBillBook.Core.Inbox;

/// <summary>邮件附件名过滤：支持逗号/分号分隔的多个通配符（*、?），匹配文件名而非完整路径。</summary>
public static class EmailAttachmentFilter
{
    public static bool Matches(string? fileName, string? patterns)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        if (string.IsNullOrWhiteSpace(patterns))
            return true;

        var name = Path.GetFileName(fileName.Trim());
        foreach (var raw in patterns.Split([',', ';'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (FileSystemName.MatchesSimpleExpression(raw, name, ignoreCase: true))
                return true;
        }

        return false;
    }
}
