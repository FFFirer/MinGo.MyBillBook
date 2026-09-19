using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace MinGo.MyBillBook.Core.Parsing;

public class WechatCsvParser : IBillParser
{
    public string PlatformCode => "WECHAT";

    public ParseResult Parse(Stream fileStream, string fileName)
    {
        var result = new ParseResult();

        // 微信 CSV 使用 UTF-8 with BOM
        using var reader = new StreamReader(fileStream, Encoding.UTF8);
        var content = reader.ReadToEnd();

        // 跳过 BOM
        if (content.Length > 0 && content[0] == '\uFEFF')
            content = content[1..];

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // 跳过说明行, 找到表头行 (包含 "交易时间" 或 "交易号")
        int headerIndex = -1;
        for (int i = 0; i < Math.Min(lines.Length, 30); i++)
        {
            if (lines[i].Contains("交易时间") && lines[i].Contains("交易类型"))
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0)
        {
            result.Errors.Add("无法找到微信支付对账单表头行");
            return result;
        }

        var headerLine = lines[headerIndex].TrimStart('#').Trim();
        var headerFields = headerLine.Split(',');

        using var csvReader = new StringReader(headerLine + "\n" + string.Join("\n", lines.Skip(headerIndex + 1)));
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ",",
            MissingFieldFound = null,
            BadDataFound = _ => { },
        };

        using var csv = new CsvReader(csvReader, config);

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
                    TransactionDate = ParseDate(GetField(csv, columnMap, "交易时间")),
                    ProductName = GetField(csv, columnMap, "商品"),
                    Amount = ParseAmount(GetField(csv, columnMap, "金额")),
                    Direction = GetField(csv, columnMap, "收/支"),
                    Counterparty = GetField(csv, columnMap, "交易对方"),
                    PaymentMethod = GetField(csv, columnMap, "支付方式"),
                    Status = GetField(csv, columnMap, "当前状态"),
                    TransactionId = GetField(csv, columnMap, "交易号"),
                };

                // 微信的 "交易类型" 存入 ExtraFields
                var txType = GetField(csv, columnMap, "交易类型");
                if (!string.IsNullOrEmpty(txType))
                    rawRow.ExtraFields["TransactionType"] = txType;

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
