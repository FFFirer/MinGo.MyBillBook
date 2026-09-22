using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Pipeline.Steps;

/// <summary>
/// 对账步骤（Pipeline 末端，Order 在 Publish 之后）。对每个存在银行余额快照(BalanceSnapshot)的账户：
/// Calculated = Opening(0) + Income - Expense（排除 Transfer），与用户录入的 BankBalance 比对，
/// 回写快照的计算余额/差异，并把超过容差的差异写入 ReconciliationIssue。
/// 不偷偷修改任何交易，仅记录差异供人工核查（符合设计原则）。
/// </summary>
public class ReconcileStep(AppDbContext db) : IPipelineStep<BillImportContext>
{
    public string Name => "Reconcile";
    public int Order => 40;

    private const decimal Tolerance = 0.01m;

    public async Task<StepResult> ExecuteAsync(BillImportContext context, CancellationToken ct)
    {
        // 取每个账户最新的快照（用户录入的银行余额）。快照量小，内存分组即可。
        var all = await db.BalanceSnapshots.AsNoTracking().ToListAsync(ct);
        var snapshots = all
            .GroupBy(s => s.AccountId)
            .Select(g => g.OrderByDescending(s => s.SnapshotDate).ThenByDescending(s => s.Id).First())
            .ToList();

        if (snapshots.Count == 0)
            return new StepResult(0, 0);

        var issues = 0;
        foreach (var snap in snapshots)
        {
            var flows = await db.BillRecords.AsNoTracking()
                .Where(r => r.FundAccountId == snap.AccountId
                         && r.TransactionDate <= snap.SnapshotDate
                         && r.TransactionType != TransactionType.Transfer)
                .Select(r => new { r.TransactionType, r.AmountMinor })
                .ToListAsync(ct);

            var income = flows.Where(f => f.TransactionType == TransactionType.Income).Sum(f => f.AmountMinor);
            var expense = flows.Where(f => f.TransactionType == TransactionType.Expense).Sum(f => f.AmountMinor);
            var calculated = (income - expense) / 100m;
            var diff = calculated - snap.BankBalance;

            // 回写快照的计算余额与差异
            var tracked = await db.BalanceSnapshots.FindAsync([snap.Id], ct);
            if (tracked != null)
            {
                tracked.CalculatedBalance = calculated;
                tracked.Difference = diff;
            }

            if (Math.Abs(diff) > Tolerance)
            {
                // 同一快照已有 Open 差异则更新，否则新建，避免重复堆积
                var existing = await db.ReconciliationIssues
                    .FirstOrDefaultAsync(i => i.SnapshotId == snap.Id && i.Status == ReconciliationIssueStatus.Open, ct);
                if (existing != null)
                {
                    existing.Difference = diff;
                    existing.Note = $"计算余额 {calculated:F2} 与银行余额 {snap.BankBalance:F2} 不一致";
                }
                else
                {
                    db.ReconciliationIssues.Add(new ReconciliationIssue
                    {
                        AccountId = snap.AccountId,
                        SnapshotId = snap.Id,
                        Difference = diff,
                        Status = ReconciliationIssueStatus.Open,
                        Note = $"计算余额 {calculated:F2} 与银行余额 {snap.BankBalance:F2} 不一致",
                        CreatedAt = DateTime.Now
                    });
                    issues++;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return new StepResult(snapshots.Count, snapshots.Count, 0, issues);
    }
}
