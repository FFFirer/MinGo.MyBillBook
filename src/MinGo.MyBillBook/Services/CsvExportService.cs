using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Interfaces;

namespace MinGo.MyBillBook.Services;

public class CsvExportService(IBillQueryService queryService, IAnalysisService analysisService)
{
    public async Task<byte[]> ExportBillsAsync(BillQueryFilter filter, CancellationToken ct = default)
    {
        var result = await queryService.QueryAsync(filter with { PageSize = 100000 }, ct);
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(true));
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("交易日期");
        csv.WriteField("交易对方");
        csv.WriteField("商户");
        csv.WriteField("分类");
        csv.WriteField("商品名称");
        csv.WriteField("金额");
        csv.WriteField("类型");
        csv.WriteField("平台");
        csv.WriteField("资金账户");
        csv.WriteField("状态");
        await csv.NextRecordAsync();

        foreach (var bill in result.Items)
        {
            csv.WriteField(bill.TransactionDate.ToString("yyyy-MM-dd HH:mm:ss"));
            csv.WriteField(bill.Counterparty);
            csv.WriteField(bill.Merchant);
            csv.WriteField(bill.CategoryName ?? "");
            csv.WriteField(bill.ProductName);
            csv.WriteField(bill.Amount.ToString("F2"));
            csv.WriteField(bill.TransactionType);
            csv.WriteField(bill.PlatformName);
            csv.WriteField(bill.FundAccountName ?? "");
            csv.WriteField(bill.Status);
            await csv.NextRecordAsync();
        }

        return ms.ToArray();
    }

    public async Task<byte[]> ExportAnalysisAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        var summary = await analysisService.GetSummaryAsync(start, end, ct);
        var categories = await analysisService.GetCategoryBreakdownAsync(start, end, ct);
        var merchants = await analysisService.GetTopMerchantsAsync(start, end, 20, ct);

        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(true));
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        // Summary section
        csv.WriteField("=== 收支总览 ===");
        await csv.NextRecordAsync();
        csv.WriteField("总收入"); csv.WriteField(summary.TotalIncome.ToString("F2")); await csv.NextRecordAsync();
        csv.WriteField("总支出"); csv.WriteField(summary.TotalExpense.ToString("F2")); await csv.NextRecordAsync();
        csv.WriteField("结余"); csv.WriteField(summary.Balance.ToString("F2")); await csv.NextRecordAsync();
        csv.WriteField("交易笔数"); csv.WriteField(summary.TransactionCount); await csv.NextRecordAsync();
        await csv.NextRecordAsync();

        // Category section
        csv.WriteField("=== 分类统计 ===");
        await csv.NextRecordAsync();
        csv.WriteField("分类"); csv.WriteField("金额"); csv.WriteField("笔数"); csv.WriteField("占比(%)"); await csv.NextRecordAsync();
        foreach (var c in categories)
        {
            csv.WriteField(c.CategoryName); csv.WriteField(c.Amount.ToString("F2")); csv.WriteField(c.Count); csv.WriteField(c.Percentage.ToString("F1"));
            await csv.NextRecordAsync();
        }
        await csv.NextRecordAsync();

        // Merchant section
        csv.WriteField("=== 商户排行 ===");
        await csv.NextRecordAsync();
        csv.WriteField("商户"); csv.WriteField("金额"); csv.WriteField("笔数"); await csv.NextRecordAsync();
        foreach (var m in merchants)
        {
            csv.WriteField(m.Merchant); csv.WriteField(m.Amount.ToString("F2")); csv.WriteField(m.Count);
            await csv.NextRecordAsync();
        }

        return ms.ToArray();
    }
}
