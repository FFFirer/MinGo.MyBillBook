using System.Text;
using MinGo.MyBillBook.Core.Parsing;

namespace MinGo.MyBillBook.Tests;

public class AlipayCsvParserTests
{
    private readonly AlipayCsvParser _parser = new();

    [Fact]
    public void PlatformCode_Should_Be_ALIPAY()
    {
        Assert.Equal("ALIPAY", _parser.PlatformCode);
    }

    [Fact]
    public void Parse_WithValidAlipayCsv_ShouldReturnRows()
    {
        // 支付宝 CSV 使用 GBK 编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gbk = Encoding.GetEncoding("GBK");

        var csvContent = "#\u652f\u4ed8\u5b9d\n#\n#\u4ea4\u6613\u521b\u5efa\u65f6\u95f4,\u4ea4\u6613\u53f7,\u4ea4\u6613\u5bf9\u65b9,\u5546\u54c1\u540d\u79f0,\u6536/\u652f,\u91d1\u989d,\u652f\u4ed8\u65b9\u5f0f,\u4ea4\u6613\u72b6\u6001\n2024-01-15 10:30:00,2024011500001,\u7f8e\u56e2\u5916\u5356,\u5348\u9910,\u652f\u51fa,35.50,\u652f\u4ed8\u5b9d\u4f59\u989d,\u4ea4\u6613\u6210\u529f\n2024-01-15 12:00:00,2024011500002,\u6ef4\u6ef4\u51fa\u884c,\u6253\u8f66\u5230\u516c\u53f8,\u652f\u51fa,18.00,\u82b1\u5457,\u4ea4\u6613\u6210\u529f\n2024-01-16 09:00:00,2024011600001,\u67d0\u516c\u53f8,\u5de5\u8d44\u6536\u5165,\u6536\u5165,8000.00,\u94f6\u884c\u5361,\u4ea4\u6613\u6210\u529f";

        var bytes = gbk.GetBytes(csvContent);
        using var stream = new MemoryStream(bytes);

        var result = _parser.Parse(stream, "alipay_test.csv");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.Rows.Count >= 3, $"Expected at least 3 rows, got {result.Rows.Count}");

        // Find rows by TransactionId (first row might be header parsed as data)
        var row1 = result.Rows.First(r => r.TransactionId == "2024011500001");
        Assert.Equal(3550L, row1.AmountMinor);
        Assert.Equal("支出", row1.Direction);
        Assert.Equal("美团外卖", row1.Counterparty);
        Assert.Equal("午餐", row1.ProductName);

        var row3 = result.Rows.First(r => r.TransactionId == "2024011600001");
        Assert.Equal(800000L, row3.AmountMinor);
        Assert.Equal("收入", row3.Direction);
    }

    [Fact]
    public void Parse_WithInvalidFile_ShouldReturnErrors()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var content = "这不是一个有效的对账单文件\n没有任何有用的数据";
        var bytes = Encoding.GetEncoding("GBK").GetBytes(content);
        using var stream = new MemoryStream(bytes);

        var result = _parser.Parse(stream, "invalid.csv");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Parse_WithEmptyStream_ShouldReturnErrors()
    {
        using var stream = new MemoryStream(Array.Empty<byte>());
        var result = _parser.Parse(stream, "empty.csv");
        Assert.False(result.Success);
    }
}

public class WechatCsvParserTests
{
    private readonly WechatCsvParser _parser = new();

    [Fact]
    public void PlatformCode_Should_Be_WECHAT()
    {
        Assert.Equal("WECHAT", _parser.PlatformCode);
    }

    [Fact]
    public void Parse_WithValidWechatCsv_ShouldReturnRows()
    {
        // 微信 CSV 使用 UTF-8 BOM
        var csvContent = "\uFEFF" + """
            微信支付账单明细
            #交易时间,交易类型,交易对方,商品,收/支,金额,支付方式,当前状态,交易号
            2024-01-15 10:30:00,商户消费,美团外卖,午餐,支出,35.50,零钱,支付成功,4200001234001
            2024-01-15 18:00:00,转账,张三,转账,支出,200.00,零钱,支付成功,4200001234002
            """;

        var bytes = Encoding.UTF8.GetBytes(csvContent);
        using var stream = new MemoryStream(bytes);

        var result = _parser.Parse(stream, "wechat_test.csv");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.Rows.Count >= 2, $"Expected at least 2 rows, got {result.Rows.Count}");

        var row1 = result.Rows.First(r => r.TransactionId == "4200001234001");
        Assert.Equal(3550L, row1.AmountMinor);
        Assert.Equal("美团外卖", row1.Counterparty);

        var row2 = result.Rows.First(r => r.TransactionId == "4200001234002");
        Assert.Equal(20000L, row2.AmountMinor);
    }

    [Fact]
    public void Parse_WithInvalidFile_ShouldReturnErrors()
    {
        var content = "这不是微信对账单";
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(bytes);

        var result = _parser.Parse(stream, "invalid.csv");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }
}

public class BillParserFactoryTests
{
    private readonly BillParserFactory _factory;

    public BillParserFactoryTests()
    {
        _factory = new BillParserFactory([new AlipayCsvParser(), new WechatCsvParser()]);
    }

    [Theory]
    [InlineData("alipay_2024.csv", "ALIPAY")]
    [InlineData("支付宝_账单.csv", "ALIPAY")]
    [InlineData("wechat_bill.csv", "WECHAT")]
    [InlineData("微信支付_账单.csv", "WECHAT")]
    public void DetectParser_ShouldDetectCorrectParser(string fileName, string expectedCode)
    {
        using var stream = new MemoryStream();
        var parser = _factory.DetectParser(stream, fileName);
        Assert.NotNull(parser);
        Assert.Equal(expectedCode, parser.PlatformCode);
    }

    [Fact]
    public void DetectParser_WithUnknownFile_ShouldReturnNull()
    {
        using var stream = new MemoryStream();
        var parser = _factory.DetectParser(stream, "unknown_file.csv");
        Assert.Null(parser);
    }

    [Theory]
    [InlineData("ALIPAY", "ALIPAY")]
    [InlineData("WECHAT", "WECHAT")]
    [InlineData("UNKNOWN", null)]
    public void GetParser_ShouldReturnCorrectParser(string code, string? expectedCode)
    {
        var parser = _factory.GetParser(code);
        if (expectedCode == null)
            Assert.Null(parser);
        else
        {
            Assert.NotNull(parser);
            Assert.Equal(expectedCode, parser.PlatformCode);
        }
    }
}
