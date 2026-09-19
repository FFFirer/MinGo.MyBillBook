using MiniExcelLibs;

namespace MinGo.MyBillBook.Core.Parsing;

public class WechatCsvParser : IBillParser
{
    public string PlatformCode => "WECHAT";

    public ParseResult Parse(Stream fileStream, string fileName)
    {
        var result = new ParseResult();

        // 微信支付账单为 Excel 文件，前若干行为说明信息，需要跳过找到表头行
        // 使用 MiniExcel 读取，自动检测 Excel 格式
        IEnumerable<dynamic>? rows = null;

        try
        {
            rows = fileStream.Query(useHeaderRow: false);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Excel 文件解析失败: {ex.Message}");
            return result;
        }

        // 找到表头行（包含 "交易时间" 和 "交易类型" 的行）
        int headerRowIndex = -1;
        string[]? headerFields = null;
        var allRows = rows.ToList();

        for (int i = 0; i < Math.Min(allRows.Count, 50); i++)
        {
            var row = allRows[i] as IDictionary<string, object>;
            if (row == null) continue;

            var values = row.Values.Select(v => v?.ToString() ?? string.Empty).ToList();
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

        // 构建列名映射
        var columnMap = new Dictionary<string, int>();
        for (int i = 0; i < headerFields.Length; i++)
        {
            var name = headerFields[i].Trim();
            if (!string.IsNullOrEmpty(name))
                columnMap[name] = i;
        }

        // 解析数据行（表头行之后的行）
        for (int i = headerRowIndex + 1; i < allRows.Count; i++)
        {
            try
            {
                var row = allRows[i] as IDictionary<string, object>;
                if (row == null) continue;

                var values = row.Values.Select(v => v?.ToString() ?? string.Empty).ToList();

                var transactionId = GetField(values, columnMap, "交易单号");
                if (string.IsNullOrEmpty(transactionId))
                    continue;

                var rawRow = new RawBillRow
                {
                    TransactionId = transactionId,
                    TransactionDate = ParseDate(GetField(values, columnMap, "交易时间")),
                    ProductName = GetField(values, columnMap, "商品"),
                    Amount = ParseAmount(GetField(values, columnMap, "金额(元)")),
                    Direction = GetField(values, columnMap, "收/支"),
                    Counterparty = GetField(values, columnMap, "交易对方"),
                    PaymentMethod = GetField(values, columnMap, "支付方式"),
                    Status = GetField(values, columnMap, "当前状态"),
                };

                // 微信的 "交易类型" 存入 ExtraFields
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

    private static string GetField(List<string> values, Dictionary<string, int> map, string fieldName)
    {
        if (map.TryGetValue(fieldName, out var index) && index < values.Count)
        {
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

    private static decimal ParseAmount(string? value)
    {
        if (decimal.TryParse(value?.Replace("¥", "").Replace(",", "").Trim(), out var amount))
            return amount;
        return 0;
    }
}
