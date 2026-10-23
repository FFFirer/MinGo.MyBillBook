using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using MinGo.MyBillBook.Core.Inbox;

namespace MinGo.MyBillBook.Tests;

public class InboxArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mb-archive-tests", Guid.NewGuid().ToString("N"));

    public InboxArchiveExtractorTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ExtractAsync_WithoutPassword_ShouldExtractAndFlatten()
    {
        var zipPath = CreateZip(("账单/支付宝.csv", "hello"), ("readme.txt", "world"));
        var target = Path.Combine(_root, "out");

        var result = await InboxArchiveExtractor.ExtractAsync(zipPath, target);

        Assert.Equal(2, result.Files.Count);
        Assert.Contains("支付宝.csv", result.Files);
        Assert.Contains("readme.txt", result.Files);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(target, "支付宝.csv")));
        Assert.Equal(10, result.BytesWritten);
    }

    [Fact]
    public async Task ExtractAsync_WithKeepStructure_ShouldPreserveDirectories()
    {
        var zipPath = CreateZip(("账单/支付宝/2026.csv", "data"));
        var target = Path.Combine(_root, "out");

        var result = await InboxArchiveExtractor.ExtractAsync(zipPath, target, flatten: false);

        Assert.Single(result.Files);
        Assert.Equal("账单/支付宝/2026.csv", result.Files[0]);
        Assert.True(File.Exists(Path.Combine(target, "账单", "支付宝", "2026.csv")));
    }

    [Fact]
    public async Task ExtractAsync_WithPassword_ShouldDecrypt()
    {
        var zipPath = CreateEncryptedZip("123456", false, ("bill.csv", "secret-data"));
        var target = Path.Combine(_root, "out");

        var result = await InboxArchiveExtractor.ExtractAsync(zipPath, target, password: "123456");

        Assert.Single(result.Files);
        Assert.Equal("secret-data", await File.ReadAllTextAsync(Path.Combine(target, "bill.csv")));
    }

    [Fact]
    public async Task ExtractAsync_WithAesPassword_ShouldDecrypt()
    {
        var zipPath = CreateEncryptedZip("p@ss", true, ("bill.csv", "aes-data"));
        var target = Path.Combine(_root, "out");

        var result = await InboxArchiveExtractor.ExtractAsync(zipPath, target, password: "p@ss");

        Assert.Single(result.Files);
        Assert.Equal("aes-data", await File.ReadAllTextAsync(Path.Combine(target, "bill.csv")));
    }

    [Fact]
    public async Task ExtractAsync_WithoutPasswordForEncryptedZip_ShouldThrowClearError()
    {
        var zipPath = CreateEncryptedZip("123456", false, ("bill.csv", "secret"));

        var ex = await Assert.ThrowsAsync<InboxArchiveException>(
            () => InboxArchiveExtractor.ExtractAsync(zipPath, Path.Combine(_root, "out")));

        Assert.Contains("已加密", ex.Message);
        Assert.Contains("password", ex.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithWrongPassword_ShouldThrow()
    {
        var zipPath = CreateEncryptedZip("123456", false, ("bill.csv", "secret"));

        var ex = await Assert.ThrowsAsync<InboxArchiveException>(
            () => InboxArchiveExtractor.ExtractAsync(zipPath, Path.Combine(_root, "out"), password: "wrong"));

        Assert.Contains("未能解压出任何文件", ex.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithTraversalEntry_ShouldNotEscapeTarget()
    {
        var zipPath = CreateZip(("../evil.csv", "evil"));
        var target = Path.Combine(_root, "out");

        var ex = await Assert.ThrowsAsync<InboxArchiveException>(
            () => InboxArchiveExtractor.ExtractAsync(zipPath, target, flatten: false));

        Assert.Contains("未能解压出任何文件", ex.Message);
        Assert.False(File.Exists(Path.Combine(_root, "evil.csv")));
    }

    [Fact]
    public async Task ExtractAsync_WithDuplicateNames_ShouldAddSuffix()
    {
        var zipPath = CreateZip(("a/bill.csv", "one"), ("b/bill.csv", "two"));
        var target = Path.Combine(_root, "out");

        var result = await InboxArchiveExtractor.ExtractAsync(zipPath, target);

        Assert.Equal(2, result.Files.Count);
        Assert.Contains("bill.csv", result.Files);
        Assert.Contains("bill-2.csv", result.Files);
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(target, "bill-2.csv")));
    }

    [Fact]
    public async Task ExtractAsync_WithEntryLimit_ShouldStopAndWarn()
    {
        var zipPath = CreateZip(("1.csv", "1"), ("2.csv", "2"), ("3.csv", "3"));
        var target = Path.Combine(_root, "out");

        var result = await InboxArchiveExtractor.ExtractAsync(zipPath, target, maxEntries: 2);

        Assert.Equal(2, result.Files.Count);
        Assert.Contains(result.Warnings, w => w.Contains("条目数超过上限"));
    }

    [Fact]
    public async Task ExtractAsync_WithOversizedEntry_ShouldSkipAndWarn()
    {
        var zipPath = CreateZip(("big.csv", new string('x', 2048)), ("small.csv", "ok"));
        var target = Path.Combine(_root, "out");

        var result = await InboxArchiveExtractor.ExtractAsync(
            zipPath, target, maxEntryBytes: 1024, maxTotalBytes: 1024 * 1024);

        Assert.Single(result.Files);
        Assert.Equal("small.csv", result.Files[0]);
        Assert.Contains(result.Warnings, w => w.Contains("超过单文件上限"));
    }

    [Fact]
    public async Task ExtractAsync_WithMissingArchive_ShouldThrow()
    {
        await Assert.ThrowsAsync<InboxArchiveException>(
            () => InboxArchiveExtractor.ExtractAsync(
                Path.Combine(_root, "missing.zip"), Path.Combine(_root, "out")));
    }

    private string CreateZip(params (string Name, string Content)[] entries)
        => CreateZipCore(entries, password: null, useAes: false);

    private string CreateEncryptedZip(string password, bool useAes, params (string Name, string Content)[] entries)
        => CreateZipCore(entries, password, useAes);

    private string CreateZipCore(
        (string Name, string Content)[] entries,
        string? password,
        bool useAes)
    {
        var path = Path.Combine(_root, $"archive-{Guid.NewGuid():N}.zip");
        using var fs = File.Create(path);
        using var zip = new ZipOutputStream(fs) { IsStreamOwner = false };
        zip.SetLevel(6);
        if (!string.IsNullOrEmpty(password))
            zip.Password = password;

        foreach (var (name, content) in entries)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var entry = new ZipEntry(name)
            {
                DateTime = DateTime.Now,
                Size = bytes.Length
            };
            if (useAes && !string.IsNullOrEmpty(password))
                entry.AESKeySize = 256;

            zip.PutNextEntry(entry);
            zip.Write(bytes, 0, bytes.Length);
            zip.CloseEntry();
        }

        zip.Finish();
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
    }
}
