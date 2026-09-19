using System.Data;
using DuckDB.NET.Data;

namespace MinGo.MyBillBook.Data.DuckDb;

public class DuckDbContext : IDisposable
{
    private readonly DuckDBConnection _connection;

    public DuckDbContext(string dbPath = "analysis.duckdb")
    {
        _connection = new DuckDBConnection($"DataSource={dbPath}");
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS bill_records (
                id INTEGER PRIMARY KEY,
                raw_record_id INTEGER,
                platform_id INTEGER,
                fund_account_id INTEGER,
                transaction_date DATE,
                counterparty VARCHAR,
                merchant VARCHAR,
                category_id INTEGER,
                category_name VARCHAR,
                category_icon VARCHAR,
                product_name VARCHAR,
                amount DECIMAL(18,2),
                transaction_type INTEGER,
                status VARCHAR,
                source_file VARCHAR,
                is_manual_adjusted BOOLEAN,
                synced_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public IDbConnection Connection => _connection;

    public void ExecuteSQL(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public DuckDBCommand CreateCommand() => _connection.CreateCommand();

    public void Dispose()
    {
        _connection.Dispose();
    }
}
