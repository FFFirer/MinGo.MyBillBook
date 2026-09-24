using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Data;
using MinGo.MyBillBook.Data.DuckDb;
using Parquet.Serialization;

namespace MinGo.MyBillBook.Services;

/// <summary>
/// DuckDB 同步服务（设计第 14 节）。以 Parquet 批量同步替代逐行 INSERT：
/// 将 Canonical 变更集与维度表（商户/标签/交易标签）序列化为 Parquet 文件，
/// 再由 DuckDB 用 read_parquet 一次性刷新，显著降低大批量导入的同步开销。
/// 保留 SyncedToDuckDb 增量标记，bill_records 仅导出变更集。
/// </summary>
public class DuckDbSyncService(AppDbContext efDb, DuckDbContext duckDb)
{
    private static readonly string DataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "data"));

    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(DataDir);

        var synced = await SyncBillRecordsAsync(ct);
        await SyncMerchantsAsync(ct);
        await SyncTagsAsync(ct);
        await SyncTransactionTagsAsync(ct);
        return synced;
    }

    private async Task<int> SyncBillRecordsAsync(CancellationToken ct)
    {
        var unsynced = await efDb.BillRecords
            .AsNoTracking()
            .Include(r => r.Category)
            .Include(r => r.MerchantRef)
            .Where(r => !r.SyncedToDuckDb)
            .ToListAsync(ct);

        if (unsynced.Count == 0) return 0;

        var rows = unsynced.Select(r => new BillRecordPq
        {
            id = r.Id,
            raw_record_id = r.RawRecordId,
            platform_id = r.PlatformId,
            fund_account_id = r.FundAccountId,
            merchant_id = r.MerchantId,
            transaction_date = r.TransactionDate,
            counterparty = r.Counterparty ?? "",
            merchant = r.MerchantRef?.CanonicalName ?? r.Merchant ?? "",
            category_id = r.CategoryId,
            category_name = r.Category?.Name ?? "未分类",
            category_icon = r.Category?.Icon ?? "more_horiz",
            product_name = r.ProductName ?? "",
            amount_minor = r.AmountMinor,
            transaction_type = (int)r.TransactionType,
            status = r.Status ?? "",
            source_file = r.SourceFile ?? "",
            source_transaction_id = r.SourceTransactionId ?? "",
            is_manual_adjusted = r.IsManualAdjusted
        }).ToList();

        var path = await WriteParquetAsync(rows, "bill_records_delta.parquet", ct);
        duckDb.ExecuteSQL($"""
            INSERT OR REPLACE INTO bill_records BY NAME
            SELECT id, raw_record_id, platform_id, fund_account_id, merchant_id,
                   CAST(transaction_date AS DATE) AS transaction_date,
                   counterparty, merchant, category_id, category_name, category_icon,
                   product_name, amount_minor, transaction_type, status, source_file,
                   is_manual_adjusted, source_transaction_id, CURRENT_TIMESTAMP AS synced_at
            FROM read_parquet('{path}')
            """);

        // 标记已同步（需 attach 到被跟踪实体）。
        var ids = unsynced.Select(r => r.Id).ToList();
        var tracked = await efDb.BillRecords.Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        foreach (var r in tracked) r.SyncedToDuckDb = true;
        await efDb.SaveChangesAsync(ct);

        return unsynced.Count;
    }

    private async Task SyncMerchantsAsync(CancellationToken ct)
    {
        var merchants = await efDb.Merchants.AsNoTracking()
            .Select(m => new MerchantPq { id = m.Id, canonical_name = m.CanonicalName, default_category_id = m.DefaultCategoryId })
            .ToListAsync(ct);
        if (merchants.Count == 0) { duckDb.ExecuteSQL("DELETE FROM merchants;"); return; }

        var path = await WriteParquetAsync(merchants, "merchants.parquet", ct);
        duckDb.ExecuteSQL($"DELETE FROM merchants; INSERT INTO merchants BY NAME SELECT * FROM read_parquet('{path}');");
    }

    private async Task SyncTagsAsync(CancellationToken ct)
    {
        var tags = await efDb.Tags.AsNoTracking()
            .Select(t => new TagPq { id = t.Id, name = t.Name, tag_type = (int)t.TagType, scope = (int)t.Scope })
            .ToListAsync(ct);
        if (tags.Count == 0) { duckDb.ExecuteSQL("DELETE FROM tags;"); return; }

        var path = await WriteParquetAsync(tags, "tags.parquet", ct);
        duckDb.ExecuteSQL($"DELETE FROM tags; INSERT INTO tags BY NAME SELECT * FROM read_parquet('{path}');");
    }

    private async Task SyncTransactionTagsAsync(CancellationToken ct)
    {
        var links = await efDb.TransactionTags.AsNoTracking()
            .Include(tt => tt.Tag)
            .Select(tt => new TransactionTagPq
            {
                transaction_id = tt.TransactionId,
                tag_id = tt.TagId,
                tag_name = tt.Tag.Name,
                source = (int)tt.Source,
                confidence = tt.Confidence
            })
            .ToListAsync(ct);
        if (links.Count == 0) { duckDb.ExecuteSQL("DELETE FROM transaction_tags;"); return; }

        var path = await WriteParquetAsync(links, "transaction_tags.parquet", ct);
        duckDb.ExecuteSQL($"DELETE FROM transaction_tags; INSERT INTO transaction_tags BY NAME SELECT * FROM read_parquet('{path}');");
    }

    /// <summary>将行集序列化为 Parquet 文件，返回 DuckDB 可读取的正斜杠绝对路径。</summary>
    private static async Task<string> WriteParquetAsync<T>(List<T> rows, string fileName, CancellationToken ct) where T : class
    {
        var full = Path.Combine(DataDir, fileName);
        await using var fs = File.Create(full);
        await ParquetSerializer.SerializeAsync(rows, fs, cancellationToken: ct);
        return full.Replace('\\', '/');
    }

    // Parquet 行 POCO：属性名与 DuckDB 列名一致，便于 read_parquet + BY NAME 刷新。
    public class BillRecordPq
    {
        public int id { get; set; }
        public int raw_record_id { get; set; }
        public int platform_id { get; set; }
        public int? fund_account_id { get; set; }
        public int? merchant_id { get; set; }
        public DateTime transaction_date { get; set; }
        public string counterparty { get; set; } = "";
        public string merchant { get; set; } = "";
        public int? category_id { get; set; }
        public string category_name { get; set; } = "";
        public string category_icon { get; set; } = "";
        public string product_name { get; set; } = "";
        public long amount_minor { get; set; }
        public int transaction_type { get; set; }
        public string status { get; set; } = "";
        public string source_file { get; set; } = "";
        public string source_transaction_id { get; set; } = "";
        public bool is_manual_adjusted { get; set; }
    }

    public class MerchantPq
    {
        public int id { get; set; }
        public string canonical_name { get; set; } = "";
        public int? default_category_id { get; set; }
    }

    public class TagPq
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public int tag_type { get; set; }
        public int scope { get; set; }
    }

    public class TransactionTagPq
    {
        public int transaction_id { get; set; }
        public int tag_id { get; set; }
        public string tag_name { get; set; } = "";
        public int source { get; set; }
        public double confidence { get; set; }
    }
}
