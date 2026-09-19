using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace MinGo.MyBillBook.Core.Parsing;

public class AlipayCsvParser : IBillParser
{
    public string PlatformCode => "ALIPAY";

    public ParseResult Parse(Stream fileStream, string fileName)
    {
        var result = new ParseResult();

        // 支付宝 CSV 使用 GBK 编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding("GBK");

        using var reader = new StreamReader(fileStream, encoding);
        var content = reader.ReadToEnd();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // 跳过说明行, 找到表头行 (包含 "交易号" 或 "交易创建时间")
        int headerIndex = -1;
        for (int i = 0; i < Math.Min(lines.Length, 30); i++)
        {
            if (lines[i].Contains("交易创建时间") || lines[i].Contains("交易号"))
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0)
        {
            result.Errors.Add("无法找到支付宝对账单表头行");
            return result;
        }

        // 清理表头 (去除 # 前缀)
        var headerLine = lines[headerIndex].TrimStart('#').Trim();
        var headerFields = ParseCsvLine(headerLine);

        using var csvReader = new StringReader(headerLine + "\n" + string.Join("\n", lines.Skip(headerIndex + 1)));
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ",",
            MissingFieldFound = null,
            BadDataFound = _ => { },
        };

        using var csv = new CsvReader(csvReader, config);

        // 手动映射列
        var columnMap = new Dictionary<string, int>();
        for (int i = 0; i < headerFields.Length; i++)
        {
            var name = headerFields[i].Trim().Trim('"');
            if (!string.IsNullOrEmpty(name))
                columnMap[name] = i;
        }

        while (csv.Read())
        {
            try
            {
                var rawRow = new RawBillRow
                {
                    TransactionId = GetField(csv, columnMap, "交易号"),
                    TransactionDate = ParseDate(GetField(csv, columnMap, "交易创建时间")),
                    ProductName = GetField(csv, columnMap, "商品名称"),
                    Amount = ParseAmount(GetField(csv, columnMap, "金额")),
                    Direction = GetField(csv, columnMap, "收/支"),
                    Counterparty = GetField(csv, columnMap, "交易对方"),
                    PaymentMethod = GetField(csv, columnMap, "支付方式"),
                    Status = GetField(csv, columnMap, "交易状态"),
                };

                if (!string.IsNullOrEmpty(rawRow.TransactionId))
                    result.Rows.Add(rawRow);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"行 {csv.Parser.RawRow}: {ex.Message}");
            }
        }

        return result;
    }

    private static string GetField(CsvReader csv, Dictionary<string, int> map, string fieldName)
    {
        if (map.TryGetValue(fieldName, out var index))
        {
            try { return csv.GetField(index)?.Trim() ?? string.Empty; }
            catch { return string.Empty; }
        }
        return string.Empty;
    }

    private static string[] ParseCsvLine(string line)
    {
        return line.Split(',');
    }

    private static DateTime ParseDate(string? value)
    {
        if (DateTime.TryParse(value, out var dt))
            return dt;
        return DateTime.MinValue;
    }

    private static decimal ParseAmount(string? value)
    {
        if (decimal.TryParse(value?.Replace("¥", "").Replace(",", "").Trim(), out var amount))
            return amount;
        return 0;
    }
}
