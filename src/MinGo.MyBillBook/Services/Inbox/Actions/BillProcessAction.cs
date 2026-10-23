using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Inbox;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Inbox.Actions;

/// <summary>
/// bill/process：对指定导入批次运行现有 9 步清洗管道（标准化、商户、分类、去重、发布等）。
/// </summary>
public sealed class BillProcessAction(IBillProcessingService processingService, AppDbContext db) : IInboxAction
{
    public string Uses => "bill/process";

    public async Task<InboxActionResult> ExecuteAsync(InboxActionContext context, CancellationToken ct)
    {
        var raw = context.Get("batch_id");
        if (!int.TryParse(raw, out var batchId) || batchId <= 0)
            return InboxActionResult.Failed($"缺少有效的 batch_id（当前值: {raw}）");

        context.Log($"开始处理批次 #{batchId}");
        await processingService.ProcessBatchAsync(batchId, ct);

        // ProcessBatchAsync 返回的是管道末步输出数，不能代表落库记录数；直接统计 Canonical 层更准确
        var processed = await db.BillRecords.CountAsync(r => r.RawRecord.ImportBatchId == batchId, ct);
        context.Log($"批次 #{batchId} 处理完成，Canonical 记录 {processed} 条");

        return InboxActionResult.Completed(("processed", processed.ToString()));
    }
}
