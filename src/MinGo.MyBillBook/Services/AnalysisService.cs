using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Data.DuckDb;

namespace MinGo.MyBillBook.Services;

public class AnalysisService(DuckDbContext duckDb, DuckDbSyncService syncService) : IAnalysisService
{
    public async Task SyncToDuckDbAsync(CancellationToken ct = default)
    {
        await syncService.SyncAsync(ct);
    }

    public Task<AnalysisSummary> GetSummaryAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        using var cmd = duckDb.CreateCommand();
        cmd.CommandText = """
            SELECT 
                COALESCE(SUM(CASE WHEN transaction_type = 0 THEN amount ELSE 0 END), 0) as total_income,
                COALESCE(SUM(CASE WHEN transaction_type = 1 THEN amount ELSE 0 END), 0) as total_expense,
                COUNT(*) as transaction_count
            FROM bill_records
            WHERE transaction_date >= @start AND transaction_date <= @end
            """;
        AddDateParam(cmd, "@start", start);
        AddDateParam(cmd, "@end", end);

        using var reader = cmd.ExecuteReader();
        var result = new AnalysisSummary();
        if (reader.Read())
        {
            result = new AnalysisSummary
            {
                TotalIncome = reader.GetDecimal(0),
                TotalExpense = reader.GetDecimal(1),
                TransactionCount = reader.GetInt32(2)
            };
        }
        return Task.FromResult(result);
    }

    public Task<List<CategoryStat>> GetCategoryBreakdownAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        using var cmd = duckDb.CreateCommand();
        cmd.CommandText = """
            SELECT category_name, category_icon, 
                   SUM(amount) as total_amount, COUNT(*) as cnt
            FROM bill_records
            WHERE transaction_type = 1 AND transaction_date >= @start AND transaction_date <= @end
            GROUP BY category_name, category_icon
            ORDER BY total_amount DESC
            """;
        AddDateParam(cmd, "@start", start);
        AddDateParam(cmd, "@end", end);

        using var reader = cmd.ExecuteReader();
        var stats = new List<CategoryStat>();
        decimal grandTotal = 0;
        var rows = new List<(string Name, string Icon, decimal Amount, int Count)>();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var icon = reader.GetString(1);
            var amount = reader.GetDecimal(2);
            var count = reader.GetInt32(3);
            rows.Add((name, icon, amount, count));
            grandTotal += amount;
        }

        foreach (var row in rows)
        {
            var pct = grandTotal > 0 ? (double)(row.Amount / grandTotal * 100) : 0;
            stats.Add(new CategoryStat(row.Name, row.Icon, row.Amount, row.Count, pct));
        }
        return Task.FromResult(stats);
    }

    public Task<List<MonthlyTrend>> GetMonthlyTrendAsync(int year, CancellationToken ct = default)
    {
        using var cmd = duckDb.CreateCommand();
        cmd.CommandText = """
            SELECT MONTH(transaction_date) as month,
                   COALESCE(SUM(CASE WHEN transaction_type = 0 THEN amount ELSE 0 END), 0) as income,
                   COALESCE(SUM(CASE WHEN transaction_type = 1 THEN amount ELSE 0 END), 0) as expense
            FROM bill_records
            WHERE YEAR(transaction_date) = @year
            GROUP BY MONTH(transaction_date)
            ORDER BY month
            """;
        cmd.Parameters.Add(CreateParam(cmd, "@year", year));

        using var reader = cmd.ExecuteReader();
        var monthlyData = new Dictionary<int, (decimal Income, decimal Expense)>();
        while (reader.Read())
        {
            var month = reader.GetInt32(0);
            monthlyData[month] = (reader.GetDecimal(1), reader.GetDecimal(2));
        }

        var trends = Enumerable.Range(1, 12).Select(m =>
        {
            var (income, expense) = monthlyData.GetValueOrDefault(m, (0, 0));
            return new MonthlyTrend($"{year}-{m:D2}", income, expense);
        }).ToList();

        return Task.FromResult(trends);
    }

    public Task<List<MerchantRank>> GetTopMerchantsAsync(DateTime start, DateTime end, int limit = 10, CancellationToken ct = default)
    {
        using var cmd = duckDb.CreateCommand();
        cmd.CommandText = $"""
            SELECT merchant, SUM(amount) as total_amount, COUNT(*) as cnt
            FROM bill_records
            WHERE transaction_type = 1 AND transaction_date >= @start AND transaction_date <= @end
            GROUP BY merchant
            ORDER BY total_amount DESC
            LIMIT {limit}
            """;
        AddDateParam(cmd, "@start", start);
        AddDateParam(cmd, "@end", end);

        using var reader = cmd.ExecuteReader();
        var merchants = new List<MerchantRank>();
        while (reader.Read())
        {
            merchants.Add(new MerchantRank(reader.GetString(0), reader.GetDecimal(1), reader.GetInt32(2)));
        }
        return Task.FromResult(merchants);
    }

    private static void AddDateParam(DuckDB.NET.Data.DuckDBCommand cmd, string name, DateTime value)
    {
        cmd.Parameters.Add(CreateParam(cmd, name, value));
    }

    private static System.Data.Common.DbParameter CreateParam(DuckDB.NET.Data.DuckDBCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }
}
