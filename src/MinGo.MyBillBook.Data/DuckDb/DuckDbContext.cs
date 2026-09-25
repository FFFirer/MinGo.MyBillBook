using System.Data;
using DuckDB.NET.Data;

namespace MinGo.MyBillBook.Data.DuckDb;

public class DuckDbContext : IDisposable
{
    private readonly DuckDBConnection _connection;

    public DuckDbContext(string dbPath = "analysis.duckdb")
    {
        _connection = new DuckDBConnection($"DataSource={dbPath}");
        OpenWithRecovery(dbPath);
        InitializeSchema();
    }

    private void OpenWithRecovery(string dbPath)
    {
        try
        {
            _connection.Open();
        }
        catch (DuckDBException ex) when (ex.Message.Contains("WAL", StringComparison.OrdinalIgnoreCase))
        {
            // WAL 文件损坏，尝试删除 WAL 后重试
            var walPath = dbPath + ".wal";
            if (File.Exists(walPath))
            {
                File.Delete(walPath);
                _connection.Open();
                return;
            }
            // WAL 不存在或仍失败，尝试删除整个数据库重建
            if (File.Exists(dbPath))
                File.Delete(dbPath);
            _connection.Open();
        }
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
                merchant_id INTEGER,
                transaction_date DATE,
                counterparty VARCHAR,
                merchant VARCHAR,
                category_id INTEGER,
                category_name VARCHAR,
                category_icon VARCHAR,
                product_name VARCHAR,
                amount_minor BIGINT,
                transaction_type INTEGER,
                status VARCHAR,
                source_file VARCHAR,
                is_manual_adjusted BOOLEAN,
                source_transaction_id VARCHAR,
                synced_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );
            CREATE TABLE IF NOT EXISTS merchants (
                id INTEGER PRIMARY KEY,
                canonical_name VARCHAR,
                default_category_id INTEGER
            );
            CREATE TABLE IF NOT EXISTS tags (
                id INTEGER PRIMARY KEY,
                name VARCHAR,
                tag_type INTEGER,
                scope INTEGER
            );
            CREATE TABLE IF NOT EXISTS transaction_tags (
                transaction_id INTEGER,
                tag_id INTEGER,
                tag_name VARCHAR,
                source INTEGER,
                confidence DOUBLE,
                PRIMARY KEY (transaction_id, tag_id)
            );
            """;
        cmd.ExecuteNonQuery();

        // 对既有分析库补充新增列（允许破坏性重建，但兼容旧库）。
        using var alter = _connection.CreateCommand();
        alter.CommandText = """
            ALTER TABLE bill_records ADD COLUMN IF NOT EXISTS merchant_id INTEGER;
            ALTER TABLE bill_records ADD COLUMN IF NOT EXISTS source_transaction_id VARCHAR;
            """;
        alter.ExecuteNonQuery();
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
