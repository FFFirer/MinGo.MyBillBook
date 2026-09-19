using System.Text.Json;
using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Parsing;
using MinGo.MyBillBook.Data;
using Microsoft.EntityFrameworkCore;

namespace MinGo.MyBillBook.Services;

public class BillImportService(AppDbContext db, IBillParserFactory parserFactory) : IBillImportService
{
    public async Task<BillImportResult> ImportAsync(Stream fileStream, string fileName, int platformId, CancellationToken ct = default)
    {
        var parser = parserFactory.GetParser(platformId == 1 ? "ALIPAY" : "WECHAT")
            ?? parserFactory.DetectParser(fileStream, fileName)
            ?? throw new InvalidOperationException("无法识别的文件格式");

        var result = parser.Parse(fileStream, fileName);
        var errors = new List<string>(result.Errors);

        var batch = new BillImportBatch
        {
            PlatformId = platformId,
            FileName = fileName,
            ImportDate = DateTime.Now,
            TotalCount = result.Rows.Count,
            Status = ImportBatchStatus.Imported
        };
        db.BillImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        int success = 0, duplicate = 0;
        foreach (var row in result.Rows)
        {
            if (!string.IsNullOrEmpty(row.TransactionId) &&
                await db.BillRawRecords.AnyAsync(r => r.TransactionId == row.TransactionId, ct))
            {
                duplicate++;
                continue;
            }

            var raw = new BillRawRecord
            {
                ImportBatchId = batch.Id,
                PlatformId = platformId,
                RawData = JsonSerializer.Serialize(row),
                TransactionDate = row.TransactionDate,
                Amount = row.Amount,
                Direction = row.Direction,
                Counterparty = row.Counterparty,
                ProductName = row.ProductName,
                PaymentMethod = row.PaymentMethod,
                Status = row.Status,
                TransactionId = row.TransactionId,
                IsProcessed = false
            };
            db.BillRawRecords.Add(raw);
            success++;
        }

        batch.SuccessCount = success;
        batch.DuplicateCount = duplicate;
        await db.SaveChangesAsync(ct);

        return new BillImportResult(batch.Id, fileName, result.Rows.Count, success, duplicate, errors);
    }

    public async Task<List<BillImportBatch>> GetBatchesAsync(CancellationToken ct = default)
    {
        return await db.BillImportBatches
            .Include(b => b.Platform)
            .OrderByDescending(b => b.ImportDate)
            .ToListAsync(ct);
    }
}
