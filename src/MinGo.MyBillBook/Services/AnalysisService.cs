using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Data;
using MinGo.MyBillBook.Data.DuckDb;

namespace MinGo.MyBillBook.Services;

public class AnalysisService(DuckDbContext duckDb, DuckDbSyncService syncService, AppDbContext efDb) : IAnalysisService
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
                COALESCE(SUM(CASE WHEN transaction_type = 0 THEN amount_minor ELSE 0 END), 0) as total_income,
                COALESCE(SUM(CASE WHEN transaction_type = 1 THEN amount_minor ELSE 0 END), 0) as total_expense,
                COUNT(*) as transaction_count
            FROM bill_records
            WHERE transaction_date >= $1 AND transaction_date <= $2
            """;
        cmd.Parameters.Add(CreateParam(cmd, "1", start));
        cmd.Parameters.Add(CreateParam(cmd, "2", end));

        using var reader = cmd.ExecuteReader();
        var result = new AnalysisSummary();
        if (reader.Read())
        {
            result = new AnalysisSummary
            {
                TotalIncome = reader.GetInt64(0) / 100m,
                TotalExpense = reader.GetInt64(1) / 100m,
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
                   SUM(amount_minor) as total_amount, COUNT(*) as cnt
            FROM bill_records
            WHERE transaction_type = 1 AND transaction_date >= $1 AND transaction_date <= $2
            GROUP BY category_name, category_icon
            ORDER BY total_amount DESC
            """;
        cmd.Parameters.Add(CreateParam(cmd, "1", start));
        cmd.Parameters.Add(CreateParam(cmd, "2", end));

        using var reader = cmd.ExecuteReader();
        var stats = new List<CategoryStat>();
        decimal grandTotal = 0;
        var rows = new List<(string Name, string Icon, decimal Amount, int Count)>();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var icon = reader.GetString(1);
            var amount = reader.GetInt64(2) / 100m;
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
                   COALESCE(SUM(CASE WHEN transaction_type = 0 THEN amount_minor ELSE 0 END), 0) as income,
                   COALESCE(SUM(CASE WHEN transaction_type = 1 THEN amount_minor ELSE 0 END), 0) as expense
            FROM bill_records
            WHERE YEAR(transaction_date) = $1
            GROUP BY MONTH(transaction_date)
            ORDER BY month
            """;
        cmd.Parameters.Add(CreateParam(cmd, "1", year));

        using var reader = cmd.ExecuteReader();
        var monthlyData = new Dictionary<int, (decimal Income, decimal Expense)>();
        while (reader.Read())
        {
            var month = reader.GetInt32(0);
            monthlyData[month] = (reader.GetInt64(1) / 100m, reader.GetInt64(2) / 100m);
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
            SELECT merchant, SUM(amount_minor) as total_amount, COUNT(*) as cnt
            FROM bill_records
            WHERE transaction_type = 1 AND transaction_date >= $1 AND transaction_date <= $2
            GROUP BY merchant
            ORDER BY total_amount DESC
            LIMIT {limit}
            """;
        cmd.Parameters.Add(CreateParam(cmd, "1", start));
        cmd.Parameters.Add(CreateParam(cmd, "2", end));

        using var reader = cmd.ExecuteReader();
        var merchants = new List<MerchantRank>();
        while (reader.Read())
        {
            merchants.Add(new MerchantRank(reader.GetString(0), reader.GetInt64(1) / 100m, reader.GetInt32(2)));
        }
        return Task.FromResult(merchants);
    }

    public Task<List<CategoryTagCell>> GetCategoryTagMatrixAsync(DateTime start, DateTime end, CancellationToken ct = default)
    {
        using var cmd = duckDb.CreateCommand();
        cmd.CommandText = """
            SELECT b.category_name, tt.tag_name, SUM(b.amount_minor) AS amt, COUNT(*) AS cnt
            FROM bill_records b JOIN transaction_tags tt ON tt.transaction_id = b.id
            WHERE b.transaction_type = 1 AND b.transaction_date >= $1 AND b.transaction_date <= $2
            GROUP BY b.category_name, tt.tag_name
            ORDER BY amt DESC
            """;
        cmd.Parameters.Add(CreateParam(cmd, "1", start));
        cmd.Parameters.Add(CreateParam(cmd, "2", end));
        using var reader = cmd.ExecuteReader();
        var cells = new List<CategoryTagCell>();
        while (reader.Read())
            cells.Add(new CategoryTagCell(reader.GetString(0), reader.GetString(1), reader.GetInt64(2) / 100m, reader.GetInt32(3)));
        return Task.FromResult(cells);
    }

    public async Task<List<DimensionMonthStat>> GetAccountMonthlyAsync(int year, CancellationToken ct = default)
    {
        var names = await efDb.FundAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        using var cmd = duckDb.CreateCommand();
        cmd.CommandText = """
            SELECT fund_account_id, MONTH(transaction_date) AS m,
                   SUM(CASE WHEN transaction_type = 0 THEN amount_minor ELSE 0 END)
                 - SUM(CASE WHEN transaction_type = 1 THEN amount_minor ELSE 0 END) AS net,
                   COUNT(*) AS cnt
            FROM bill_records
            WHERE YEAR(transaction_date) = $1 AND fund_account_id IS NOT NULL AND transaction_type IN (0, 1)
            GROUP BY fund_account_id, MONTH(transaction_date)
            ORDER BY fund_account_id, m
            """;
        cmd.Parameters.Add(CreateParam(cmd, "1", year));
        using var reader = cmd.ExecuteReader();
        var rows = new List<DimensionMonthStat>();
        while (reader.Read())
        {
            var aid = reader.GetInt32(0);
            var name = names.GetValueOrDefault(aid, $"账户#{aid}");
            rows.Add(new DimensionMonthStat(name, $"{year}-{reader.GetInt32(1):D2}", reader.GetInt64(2) / 100m, reader.GetInt32(3)));
        }
        return rows;
    }

    public Task<List<DimensionMonthStat>> GetMerchantMonthlyAsync(int year, int limit = 10, CancellationToken ct = default)
    {
        using var cmd = duckDb.CreateCommand();
        cmd.CommandText = $"""
            SELECT merchant, MONTH(transaction_date) AS m, SUM(amount_minor) AS amt, COUNT(*) AS cnt
            FROM bill_records
            WHERE transaction_type = 1 AND YEAR(transaction_date) = $1
              AND merchant IN (
                SELECT merchant FROM bill_records
                WHERE transaction_type = 1 AND YEAR(transaction_date) = $1
                GROUP BY merchant ORDER BY SUM(amount_minor) DESC LIMIT {limit})
            GROUP BY merchant, MONTH(transaction_date)
            ORDER BY merchant, m
            """;
        cmd.Parameters.Add(CreateParam(cmd, "1", year));
        using var reader = cmd.ExecuteReader();
        var rows = new List<DimensionMonthStat>();
        while (reader.Read())
            rows.Add(new DimensionMonthStat(reader.GetString(0), $"{year}-{reader.GetInt32(1):D2}", reader.GetInt64(2) / 100m, reader.GetInt32(3)));
        return Task.FromResult(rows);
    }

    public Task<List<DimensionMonthStat>> GetTagMonthlyAsync(int year, CancellationToken ct = default)
    {
        using var cmd = duckDb.CreateCommand();
        cmd.CommandText = """
            SELECT tt.tag_name, MONTH(b.transaction_date) AS m, SUM(b.amount_minor) AS amt, COUNT(*) AS cnt
            FROM bill_records b JOIN transaction_tags tt ON tt.transaction_id = b.id
            WHERE b.transaction_type = 1 AND YEAR(b.transaction_date) = $1
            GROUP BY tt.tag_name, MONTH(b.transaction_date)
            ORDER BY tt.tag_name, m
            """;
        cmd.Parameters.Add(CreateParam(cmd, "1", year));
        using var reader = cmd.ExecuteReader();
        var rows = new List<DimensionMonthStat>();
        while (reader.Read())
            rows.Add(new DimensionMonthStat(reader.GetString(0), $"{year}-{reader.GetInt32(1):D2}", reader.GetInt64(2) / 100m, reader.GetInt32(3)));
        return Task.FromResult(rows);
    }

    public Task<List<CashFlowPoint>> GetCashFlowAsync(int year, CancellationToken ct = default)
    {
        using var cmd = duckDb.CreateCommand();
        cmd.CommandText = """
            SELECT MONTH(transaction_date) AS m,
                   COALESCE(SUM(CASE WHEN transaction_type = 0 THEN amount_minor ELSE 0 END), 0) AS inflow,
                   COALESCE(SUM(CASE WHEN transaction_type = 1 THEN amount_minor ELSE 0 END), 0) AS outflow
            FROM bill_records
            WHERE YEAR(transaction_date) = $1
            GROUP BY MONTH(transaction_date)
            """;
        cmd.Parameters.Add(CreateParam(cmd, "1", year));
        using var reader = cmd.ExecuteReader();
        var map = new Dictionary<int, (decimal In, decimal Out)>();
        while (reader.Read())
            map[reader.GetInt32(0)] = (reader.GetInt64(1) / 100m, reader.GetInt64(2) / 100m);
        var points = Enumerable.Range(1, 12).Select(m =>
        {
            var (inn, outt) = map.GetValueOrDefault(m, (0, 0));
            return new CashFlowPoint($"{year}-{m:D2}", inn, outt, inn - outt);
        }).ToList();
        return Task.FromResult(points);
    }

    public async Task<NetWorthSnapshot> GetNetWorthAsync(CancellationToken ct = default)
    {
        var accounts = await efDb.FundAccounts.AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.PlatformId).ThenBy(a => a.Id)
            .Select(a => new NetWorthItem(a.Name, a.Balance))
            .ToListAsync(ct);
        var total = accounts.Sum(a => a.Balance);
        return new NetWorthSnapshot(total, accounts);
    }

    private static System.Data.Common.DbParameter CreateParam(DuckDB.NET.Data.DuckDBCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }
}
