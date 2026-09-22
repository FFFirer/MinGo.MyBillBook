using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using MiniExcelLibs;

namespace MinGo.MyBillBook.Core.Parsing;

/// <summary>
/// 微信支付账单解析器。微信账单既可能是 xlsx（MiniExcel 读取），
/// 也可能是 CSV 文本（前若干行为说明信息，需跳过找到表头行）。
/// 按文件扩展名选择读取方式，列名支持多种别名以兼容不同导出模板。
/// </summary>
public class WechatCsvParser : IBillParser
{
    public string PlatformCode => "WECHAT";

    public ParseResult Parse(Stream fileStream, string fileName)
    {
        var result = new ParseResult();

        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        if (ext is ".xlsx" or ".xls")
            return ParseExcel(fileStream, result);

        return ParseCsv(fileStream, result);
    }

    /// <summary>CSV 文本路径：定位表头行后用 CsvHelper 逐行映射。</summary>
    private static ParseResult ParseCsv(Stream fileStream, ParseResult result)
    {
        using var reader = new StreamReader(fileStream, System.Text.Encoding.UTF8);
        var content = reader.ReadToEnd();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        int headerIndex = -1;
        for (int i = 0; i < Math.Min(lines.Length, 50); i++)
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

        // 用 CsvHelper 解析表头行, 正确处理字段内含逗号的情况
        using var headerCsvReader = new StringReader(headerLine);
        var headerConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            Delimiter = ",",
            MissingFieldFound = null,
            BadDataFound = _ => { },
        };
        string[] headerFields;
        using (var headerCsv = new CsvReader(headerCsvReader, headerConfig))
        {
            headerCsv.Read();
            headerFields = Enumerable.Range(0, headerCsv.Parser.Count)
                .Select(i => headerCsv.GetField(i)?.Trim().Trim('"') ?? string.Empty)
                .ToArray();
        }

        var columnMap = new Dictionary<string, int>();
        for (int i = 0; i < headerFields.Length; i++)
        {
            if (!string.IsNullOrEmpty(headerFields[i]))
                columnMap[headerFields[i]] = i;
        }

        using var csvReader = new StringReader(headerLine + "\n" + string.Join("\n", lines.Skip(headerIndex + 1)));
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = ",",
            MissingFieldFound = null,
            BadDataFound = _ => { },
        };

        using var csv = new CsvReader(csvReader, config);

        while (csv.Read())
        {
            try
            {
                var rawRow = new RawBillRow
                {
                    TransactionId = GetField(csv, columnMap, "交易号", "交易单号"),
                    TransactionDate = ParseDate(GetField(csv, columnMap, "交易时间")),
                    ProductName = GetField(csv, columnMap, "商品", "商品名称"),
                    AmountMinor = ParseAmountMinor(GetField(csv, columnMap, "金额", "金额(元)")),
                    Direction = GetField(csv, columnMap, "收/支"),
                    Counterparty = GetField(csv, columnMap, "交易对方"),
                    PaymentMethod = GetField(csv, columnMap, "支付方式", "收/付款方式"),
                    Status = GetField(csv, columnMap, "当前状态", "交易状态"),
                };

                if (string.IsNullOrEmpty(rawRow.TransactionId))
                    continue;

                // 微信的 "交易类型" 存入 ExtraFields
                var txType = GetField(csv, columnMap, "交易类型");
                if (!string.IsNullOrEmpty(txType))
                    rawRow.ExtraFields["TransactionType"] = txType;

                result.Rows.Add(rawRow);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"行 {csv.Parser.RawRow}: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>xlsx 路径：MiniExcel 读取，前若干行为说明信息，需跳过找到表头行。</summary>
    private static ParseResult ParseExcel(Stream fileStream, ParseResult result)
    {
        List<dynamic> allRows;
        try
        {
            allRows = fileStream.Query(useHeaderRow: false).ToList();
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Excel 文件解析失败: {ex.Message}");
            return result;
        }

        int headerRowIndex = -1;
        string[]? headerFields = null;
        for (int i = 0; i < Math.Min(allRows.Count, 50); i++)
        {
            if (allRows[i] is not IDictionary<string, object> row) continue;
            var values = ((IDictionary<string, object>)allRows[i]).Values.Select(v => v?.ToString() ?? string.Empty).ToList();
            var lineText = string.Join(",", values);
            if (lineText.Contains("交易时间") && lineText.Contains("交易类型"))
            {
                headerRowIndex = i;
                headerFields = values.ToArray();
                break;
            }
        }

        if (headerRowIndex < 0 || headerFields == null)
        {
            result.Errors.Add("无法找到微信支付对账单表头行");
            return result;
        }

        var columnMap = new Dictionary<string, int>();
        for (int i = 0; i < headerFields.Length; i++)
        {
            var name = headerFields[i].Trim();
            if (!string.IsNullOrEmpty(name))
                columnMap[name] = i;
        }

        for (int i = headerRowIndex + 1; i < allRows.Count; i++)
        {
            try
            {
                if (allRows[i] is not IDictionary<string, object> row) continue;
                var values = row.Values.Select(v => v?.ToString() ?? string.Empty).ToList();

                var transactionId = GetField(values, columnMap, "交易号", "交易单号");
                if (string.IsNullOrEmpty(transactionId))
                    continue;

                var rawRow = new RawBillRow
                {
                    TransactionId = transactionId,
                    TransactionDate = ParseDate(GetField(values, columnMap, "交易时间")),
                    ProductName = GetField(values, columnMap, "商品", "商品名称"),
                    AmountMinor = ParseAmountMinor(GetField(values, columnMap, "金额", "金额(元)")),
                    Direction = GetField(values, columnMap, "收/支"),
                    Counterparty = GetField(values, columnMap, "交易对方"),
                    PaymentMethod = GetField(values, columnMap, "支付方式", "收/付款方式"),
                    Status = GetField(values, columnMap, "当前状态", "交易状态"),
                };

                var txType = GetField(values, columnMap, "交易类型");
                if (!string.IsNullOrEmpty(txType))
                    rawRow.ExtraFields["TransactionType"] = txType;

                result.Rows.Add(rawRow);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"第 {i + 1} 行解析失败: {ex.Message}");
            }
        }

        return result;
    }

    private static string GetField(CsvReader csv, Dictionary<string, int> map, params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (map.TryGetValue(fieldName, out var index))
            {
                try { return csv.GetField(index)?.Trim() ?? string.Empty; }
                catch { return string.Empty; }
            }
        }
        return string.Empty;
    }

    private static string GetField(List<string> values, Dictionary<string, int> map, params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (map.TryGetValue(fieldName, out var index) && index < values.Count)
                return values[index].Trim();
        }
        return string.Empty;
    }

    private static DateTime ParseDate(string? value)
    {
        if (DateTime.TryParse(value, out var dt))
            return dt;
        return DateTime.MinValue;
    }

    private static long ParseAmountMinor(string? value)
    {
        if (decimal.TryParse(value?.Replace("¥", "").Replace(",", "").Trim(), out var amount))
            return decimal.ToInt64(Math.Round(amount * 100m, MidpointRounding.AwayFromZero));
        return 0;
    }
}
