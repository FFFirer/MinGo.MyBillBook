using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Parsing;
using MinGo.MyBillBook.Data;
using MinGo.MyBillBook.Services;
using MinGo.MyBillBook.Services.Pipeline;
using MinGo.MyBillBook.Services.Pipeline.Steps;

namespace MinGo.MyBillBook.Tests;

public sealed class BillImportServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AppDbContext _db = null!;
    private BillImportService _service = null!;

    public async Task InitializeAsync()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _db.PaymentPlatforms.AddRange(
            new PaymentPlatform { Id = 1, Name = "支付宝", Code = "ALIPAY" },
            new PaymentPlatform { Id = 2, Name = "微信", Code = "WECHAT" });
        await _db.SaveChangesAsync();

        _service = new BillImportService(
            _db,
            new BillParserFactory([new AlipayCsvParser()]),
            new FakeObjectStorage());
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Reimport_MovesRawToNewBatchAndClearsDownstreamForRegeneration()
    {
        var csv = Encoding.UTF8.GetBytes(SampleCsv);

        var first = await _service.ImportAsync(new MemoryStream(csv), "alipay-dup.csv", 1);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, first.SuccessCount);
        Assert.Equal(0, first.UpdatedCount);

        var raws = await _db.BillRawRecords.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(2, raws.Count);

        // 模拟首次清洗完成：每条 Raw 都有标准化记录与 Canonical 账单。
        foreach (var raw in raws)
        {
            _db.NormalizedTransactions.Add(new NormalizedTransaction
            {
                RawRecordId = raw.Id,
                OccurredAt = new DateTime(2026, 10, 20),
                AmountMinor = 3550,
                RawDescription = "午餐",
                Counterparty = "商户A",
                ProductName = "午餐",
                Direction = "支出",
                PaymentMethod = "支付宝余额",
                Status = "交易成功",
                SourceTransactionId = raw.SourceTransactionId
            });
            _db.BillRecords.Add(new BillRecord
            {
                RawRecordId = raw.Id,
                PlatformId = 1,
                TransactionDate = new DateTime(2026, 10, 20),
                Counterparty = "商户A",
                Merchant = "商户A",
                ProductName = "午餐",
                AmountMinor = 3550,
                Status = "交易成功",
                SourceFile = "alipay-dup.csv",
                SourceTransactionId = raw.SourceTransactionId
            });
        }

        await _db.SaveChangesAsync();
        Assert.Equal(2, await _db.BillRecords.CountAsync());
        Assert.Equal(2, await _db.NormalizedTransactions.CountAsync());

        // 再次导入同一文件：应删除旧下游，并把 Raw 归入新批次，供新批次清洗重建。
        var second = await _service.ImportAsync(new MemoryStream(csv), "alipay-dup.csv", 1);
        Assert.Equal(2, second.UpdatedCount);
        Assert.Equal(0, second.SuccessCount);

        Assert.Equal(0, await _db.BillRecords.CountAsync());
        Assert.Equal(0, await _db.NormalizedTransactions.CountAsync());

        var moved = await _db.BillRawRecords.AsNoTracking().ToListAsync();
        Assert.Equal(2, moved.Count);
        Assert.All(moved, raw =>
        {
            Assert.Equal(second.BatchId, raw.ImportBatchId);
            Assert.False(raw.IsProcessed);
        });

        // 新批次执行 Normalize 能重新生成标准化记录，随后管道可重建 Canonical。
        var context = new BillImportContext { ImportBatchId = second.BatchId };
        var normalized = await new NormalizeStep(_db).ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(2, normalized.InputCount);
        Assert.Equal(2, normalized.OutputCount);
        Assert.Equal(2, await _db.NormalizedTransactions.CountAsync());
    }

    private const string SampleCsv = """
        #支付宝
        #
        交易创建时间,交易订单号,商家订单号,交易对方,商品名称,收/支,金额,支付方式,交易状态,交易分类
        2026-10-20 10:30:00,DUP001,MERCHANT001,商户A,午餐,支出,35.50,支付宝余额,交易成功,餐饮美食
        2026-10-20 12:00:00,DUP002,MERCHANT002,商户B,打车,支出,18.00,花呗,交易成功,交通出行
        """;

    private sealed class FakeObjectStorage : IObjectStorage
    {
        public Task<string> PutAsync(string key, Stream content, string? contentType = null, CancellationToken ct = default)
            => Task.FromResult(key);

        public Task<Stream> GetAsync(string key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
