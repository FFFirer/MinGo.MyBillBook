using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Parsing;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Pipeline.Steps;

/// <summary>
/// 标准化步骤：加载批次与待处理原始记录，将每条 RawPayload 反序列化并转换为
/// <see cref="NormalizedTransaction"/> 持久化，作为后续 Resolve/Classify/Tag 步骤的统一输入。
/// Raw 层保持只读，不做任何修改。
/// </summary>
public class NormalizeStep(AppDbContext db) : IPipelineStep<BillImportContext>
{
    public string Name => "Normalize";
    public int Order => 10;

    public async Task<StepResult> ExecuteAsync(BillImportContext context, CancellationToken ct)
    {
        var batchId = context.ImportBatchId
            ?? throw new InvalidOperationException("上下文缺少 ImportBatchId");

        var batch = await db.BillImportBatches.FindAsync([batchId], ct)
            ?? throw new InvalidOperationException($"批次 {batchId} 不存在");

        batch.Status = ImportBatchStatus.Processing;
        await db.SaveChangesAsync(ct);

        var rawRecords = await db.BillRawRecords
            .Where(r => r.ImportBatchId == batchId && !r.IsProcessed)
            .ToListAsync(ct);

        var normalized = new List<NormalizedTransaction>(rawRecords.Count);
        var errors = 0;
        foreach (var raw in rawRecords)
        {
            try
            {
                var row = JsonSerializer.Deserialize<RawBillRow>(raw.RawPayload);
                if (row is null)
                {
                    errors++;
                    context.Issues.Add(new PipelineIssue(Name, $"原始记录 {raw.Id} 的 RawPayload 为空", IssueSeverity.Warning));
                    continue;
                }

                normalized.Add(new NormalizedTransaction
                {
                    RawRecordId = raw.Id,
                    OccurredAt = row.TransactionDate,
                    AmountMinor = row.AmountMinor,
                    Currency = "CNY",
                    RawDescription = row.ProductName,
                    Counterparty = row.Counterparty,
                    ProductName = row.ProductName,
                    Direction = row.Direction,
                    PaymentMethod = row.PaymentMethod,
                    Status = row.Status,
                    SourceTransactionId = row.TransactionId,
                    SourcePaymentTransactionId = row.PaymentTransactionId,
                    SourceCategory = row.SourceCategory
                });
            }
            catch (Exception ex)
            {
                errors++;
                context.Issues.Add(new PipelineIssue(Name, $"原始记录 {raw.Id} 解析失败: {ex.Message}", IssueSeverity.Error));
            }
        }

        if (normalized.Count > 0)
        {
            db.NormalizedTransactions.AddRange(normalized);
            await db.SaveChangesAsync(ct);
        }

        context.Batch = batch;
        context.RawRecords = rawRecords;
        context.NormalizedTransactions = normalized;

        return new StepResult(rawRecords.Count, normalized.Count, errors);
    }
}
