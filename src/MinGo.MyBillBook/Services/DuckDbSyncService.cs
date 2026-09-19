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
            VALUES (@id, @raw_record_id, @platform_id, @fund_account_id, @transaction_date,
             @counterparty, @merchant, @category_id, @category_name, @category_icon,
             @product_name, @amount, @transaction_type, @status, @source_file, @is_manual_adjusted, CURRENT_TIMESTAMP)
            """;

        var idParam = insertCmd.CreateParameter(); idParam.ParameterName = "@id"; insertCmd.Parameters.Add(idParam);
        var rawIdParam = insertCmd.CreateParameter(); rawIdParam.ParameterName = "@raw_record_id"; insertCmd.Parameters.Add(rawIdParam);
        var platParam = insertCmd.CreateParameter(); platParam.ParameterName = "@platform_id"; insertCmd.Parameters.Add(platParam);
        var faParam = insertCmd.CreateParameter(); faParam.ParameterName = "@fund_account_id"; insertCmd.Parameters.Add(faParam);
        var dateParam = insertCmd.CreateParameter(); dateParam.ParameterName = "@transaction_date"; insertCmd.Parameters.Add(dateParam);
        var cpParam = insertCmd.CreateParameter(); cpParam.ParameterName = "@counterparty"; insertCmd.Parameters.Add(cpParam);
        var merchParam = insertCmd.CreateParameter(); merchParam.ParameterName = "@merchant"; insertCmd.Parameters.Add(merchParam);
        var catIdParam = insertCmd.CreateParameter(); catIdParam.ParameterName = "@category_id"; insertCmd.Parameters.Add(catIdParam);
        var catNameParam = insertCmd.CreateParameter(); catNameParam.ParameterName = "@category_name"; insertCmd.Parameters.Add(catNameParam);
        var catIconParam = insertCmd.CreateParameter(); catIconParam.ParameterName = "@category_icon"; insertCmd.Parameters.Add(catIconParam);
        var prodParam = insertCmd.CreateParameter(); prodParam.ParameterName = "@product_name"; insertCmd.Parameters.Add(prodParam);
        var amtParam = insertCmd.CreateParameter(); amtParam.ParameterName = "@amount"; insertCmd.Parameters.Add(amtParam);
        var typeParam = insertCmd.CreateParameter(); typeParam.ParameterName = "@transaction_type"; insertCmd.Parameters.Add(typeParam);
        var statusParam = insertCmd.CreateParameter(); statusParam.ParameterName = "@status"; insertCmd.Parameters.Add(statusParam);
        var srcParam = insertCmd.CreateParameter(); srcParam.ParameterName = "@source_file"; insertCmd.Parameters.Add(srcParam);
        var manualParam = insertCmd.CreateParameter(); manualParam.ParameterName = "@is_manual_adjusted"; insertCmd.Parameters.Add(manualParam);

        foreach (var r in unsynced)
        {
            idParam.Value = r.Id;
            rawIdParam.Value = r.RawRecordId;
            platParam.Value = r.PlatformId;
            faParam.Value = (object?)r.FundAccountId ?? DBNull.Value;
            dateParam.Value = r.TransactionDate;
            cpParam.Value = r.Counterparty ?? "";
            merchParam.Value = r.Merchant ?? "";
            catIdParam.Value = (object?)r.CategoryId ?? DBNull.Value;
            catNameParam.Value = r.Category?.Name ?? "未分类";
            catIconParam.Value = r.Category?.Icon ?? "more_horiz";
            prodParam.Value = r.ProductName ?? "";
            amtParam.Value = r.Amount;
            typeParam.Value = (int)r.TransactionType;
            statusParam.Value = r.Status ?? "";
            srcParam.Value = r.SourceFile ?? "";
            manualParam.Value = r.IsManualAdjusted;

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
