using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;
using MinGo.MyBillBook.Data.DuckDb;

namespace MinGo.MyBillBook.Services;

public class DuckDbSyncService(AppDbContext efDb, DuckDbContext duckDb)
{
    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        // Get records not yet synced or updated after last sync
        var unsynced = await efDb.BillRecords
            .Include(r => r.Category)
            .Include(r => r.Platform)
            .Where(r => !r.SyncedToDuckDb)
            .ToListAsync(ct);

        if (unsynced.Count == 0) return 0;

        using var insertCmd = duckDb.CreateCommand();
        insertCmd.CommandText = """
            INSERT OR REPLACE INTO bill_records 
            (id, raw_record_id, platform_id, fund_account_id, transaction_date, 
             counterparty, merchant, category_id, category_name, category_icon,
             product_name, amount, transaction_type, status, source_file, is_manual_adjusted, synced_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, CURRENT_TIMESTAMP)
            """;

        var parameters = Enumerable.Range(1, 16).Select(i => { var p = insertCmd.CreateParameter(); p.ParameterName = i.ToString(); insertCmd.Parameters.Add(p); return p; }).ToArray();

        foreach (var r in unsynced)
        {
            parameters[0].Value = r.Id;
            parameters[1].Value = r.RawRecordId;
            parameters[2].Value = r.PlatformId;
            parameters[3].Value = (object?)r.FundAccountId ?? DBNull.Value;
            parameters[4].Value = r.TransactionDate;
            parameters[5].Value = r.Counterparty ?? "";
            parameters[6].Value = r.Merchant ?? "";
            parameters[7].Value = (object?)r.CategoryId ?? DBNull.Value;
            parameters[8].Value = r.Category?.Name ?? "未分类";
            parameters[9].Value = r.Category?.Icon ?? "more_horiz";
            parameters[10].Value = r.ProductName ?? "";
            parameters[11].Value = r.Amount;
            parameters[12].Value = (int)r.TransactionType;
            parameters[13].Value = r.Status ?? "";
            parameters[14].Value = r.SourceFile ?? "";
            parameters[15].Value = r.IsManualAdjusted;

            insertCmd.ExecuteNonQuery();
        }

        // Mark as synced
        foreach (var r in unsynced)
        {
            r.SyncedToDuckDb = true;
        }
        await efDb.SaveChangesAsync(ct);

        return unsynced.Count;
    }
}
