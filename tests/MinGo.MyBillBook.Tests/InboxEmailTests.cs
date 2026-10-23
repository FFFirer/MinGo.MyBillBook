using MinGo.MyBillBook.Core.Inbox;

namespace MinGo.MyBillBook.Tests;

public class InboxEmailSearchQueryTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 15, 30, 0);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WithoutConditions_ShouldDefaultToUnread(string? text)
    {
        var query = EmailSearchQuery.Parse(text, Now);

        Assert.True(query.UnreadOnly);
        Assert.Null(query.FromContains);
        Assert.Null(query.SubjectContains);
        Assert.Null(query.Since);
        Assert.Null(query.Before);
    }

    [Fact]
    public void Parse_ShouldMapAllConditions()
    {
        var query = EmailSearchQuery.Parse(
            "from:alipay subject:账单 since:2026-09-01 before:2026-10-01 unseen", Now);

        Assert.True(query.UnreadOnly);
        Assert.Equal("alipay", query.FromContains);
        Assert.Equal("账单", query.SubjectContains);
        Assert.Equal(new DateTime(2026, 9, 1), query.Since);
        Assert.Equal(new DateTime(2026, 10, 1), query.Before);
    }

    [Fact]
    public void Parse_WithAll_ShouldIncludeReadMessages()
    {
        var query = EmailSearchQuery.Parse("all", Now);

        Assert.False(query.UnreadOnly);
    }

    [Fact]
    public void Parse_WithQuotedValue_ShouldKeepSpaces()
    {
        var query = EmailSearchQuery.Parse("subject:\"月度 账单\" from:\"a b\"", Now);

        Assert.Equal("月度 账单", query.SubjectContains);
        Assert.Equal("a b", query.FromContains);
    }

    [Fact]
    public void Parse_WithEscapedQuote_ShouldUnescape()
    {
        var query = EmailSearchQuery.Parse("subject:\"他说\\\"你好\\\"\"", Now);

        Assert.Equal("他说\"你好\"", query.SubjectContains);
    }

    [Theory]
    [InlineData("30d", 2026, 9, 23)]
    [InlineData("2w", 2026, 10, 9)]
    [InlineData("1m", 2026, 9, 23)]
    [InlineData("7D", 2026, 10, 16)]
    [InlineData("today", 2026, 10, 23)]
    public void Parse_WithRelativeDate_ShouldResolveAgainstNow(string value, int year, int month, int day)
    {
        var query = EmailSearchQuery.Parse($"since:{value}", Now);

        Assert.Equal(new DateTime(year, month, day), query.Since);
    }

    [Theory]
    [InlineData("2026/09/01", 2026, 9, 1)]
    [InlineData("2026.09.01", 2026, 9, 1)]
    [InlineData("20260901", 2026, 9, 1)]
    public void Parse_WithAbsoluteDateFormats_ShouldParse(string value, int year, int month, int day)
    {
        var query = EmailSearchQuery.Parse($"before:{value}", Now);

        Assert.Equal(new DateTime(year, month, day), query.Before);
    }

    [Theory]
    [InlineData("unseen all")]
    [InlineData("all unseen")]
    [InlineData("from:")]
    [InlineData("subject:   ")]
    [InlineData("since:yesterday")]
    [InlineData("since:2026-10-01 before:2026-09-01")]
    [InlineData("since:2026-10-01 before:2026-10-01")]
    [InlineData("unknown:value")]
    [InlineData("subject:\"未闭合")]
    [InlineData("since:0d")]
    public void Parse_WithInvalidText_ShouldThrow(string text)
    {
        Assert.Throws<ArgumentException>(() => EmailSearchQuery.Parse(text, Now));
    }

    [Fact]
    public void Describe_ShouldSummarizeConditions()
    {
        var query = EmailSearchQuery.Parse("from:alipay subject:账单 since:30d before:today", Now);
        var description = query.Describe();

        Assert.Contains("仅未读", description);
        Assert.Contains("alipay", description);
        Assert.Contains("账单", description);
        Assert.Contains("2026-09-23", description);
        Assert.Contains("2026-10-23", description);
    }
}

public class InboxEmailAttachmentFilterTests
{
    [Theory]
    [InlineData("alipay_202609.csv", "*.csv", true)]
    [InlineData("ALIPAY_202609.CSV", "*.csv", true)]
    [InlineData("alipay.xlsx", "*.csv", false)]
    [InlineData("bill.zip", "*.csv, *.xlsx", false)]
    [InlineData("bill.xlsx", "*.csv;*.xlsx", true)]
    [InlineData("账单.csv", "*.csv", true)]
    [InlineData("folder/bill.csv", "*.csv", true)]
    [InlineData(@"C:\mail\bill.csv", "*.csv", true)]
    [InlineData("bill.csv", "*", true)]
    public void Matches_ShouldApplyGlobsToFileName(string fileName, string patterns, bool expected)
    {
        Assert.Equal(expected, EmailAttachmentFilter.Matches(fileName, patterns));
    }

    [Fact]
    public void Matches_WithoutPatterns_ShouldAcceptAnyNamedFile()
    {
        Assert.True(EmailAttachmentFilter.Matches("bill.csv", null));
        Assert.True(EmailAttachmentFilter.Matches("bill.csv", " "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Matches_WithoutFileName_ShouldReject(string? fileName)
    {
        Assert.False(EmailAttachmentFilter.Matches(fileName, "*.csv"));
    }
}

public class InboxWorkspaceFileNameTests
{
    [Theory]
    [InlineData("bill.csv", "bill.csv")]
    [InlineData("账单 202609.csv", "账单 202609.csv")]
    [InlineData("folder/bill.csv", "bill.csv")]
    [InlineData(@"C:\mail\bill.csv", "bill.csv")]
    [InlineData("a:b*c?.csv", "a_b_c_.csv")]
    [InlineData("bill.csv. ", "bill.csv")]
    public void Sanitize_ShouldNormalizeName(string input, string expected)
    {
        Assert.Equal(expected, WorkspaceFileName.Sanitize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("/")]
    public void Sanitize_WithoutUsableName_ShouldUseFallback(string? input)
    {
        var name = WorkspaceFileName.Sanitize(input, "attachment");

        Assert.StartsWith("attachment-", name);
        Assert.EndsWith(".bin", name);
    }

    [Fact]
    public void Sanitize_WithLongName_ShouldKeepExtension()
    {
        var input = new string('账', 300) + ".csv";
        var name = WorkspaceFileName.Sanitize(input);

        Assert.True(name.Length <= 150);
        Assert.EndsWith(".csv", name);
    }
}
