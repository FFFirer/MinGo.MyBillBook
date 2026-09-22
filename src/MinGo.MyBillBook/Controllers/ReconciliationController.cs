using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Controllers;

/// <summary>
/// 对账接口（设计第 10/11 节）：账户余额录入/快照、计算余额 vs 银行余额差异、
/// 差异清单与标记已解决、检测到的转账列表。
/// </summary>
[ApiController]
[Route("api/reconciliation")]
public class ReconciliationController(AppDbContext db) : ControllerBase
{
    private const decimal Tolerance = 0.01m;

    /// <summary>账户对账概览：每账户最新快照的 Bank vs Calculated（供 Accounts.razor 展示）。</summary>
    [HttpGet("accounts")]
    public async Task<ActionResult> GetAccounts(CancellationToken ct)
    {
        var accounts = await db.FundAccounts.AsNoTracking()
            .OrderBy(a => a.PlatformId).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.Name, a.Balance, a.IsActive })
            .ToListAsync(ct);

        var snapshots = await db.BalanceSnapshots.AsNoTracking().ToListAsync(ct);
        var latest = snapshots
            .GroupBy(s => s.AccountId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.SnapshotDate).ThenByDescending(s => s.Id).First());

        var result = accounts.Select(a =>
        {
            latest.TryGetValue(a.Id, out var snap);
            return new
            {
                a.Id,
                a.Name,
                a.IsActive,
                BankBalance = snap?.BankBalance,
                CalculatedBalance = snap?.CalculatedBalance,
                Difference = snap?.Difference,
                SnapshotDate = snap?.SnapshotDate
            };
        }).ToList();
        return Ok(result);
    }

    [HttpGet("snapshots")]
    public async Task<ActionResult> GetSnapshots(CancellationToken ct)
    {
        var rows = await db.BalanceSnapshots.AsNoTracking()
            .Include(s => s.Account)
            .OrderByDescending(s => s.SnapshotDate)
            .Select(s => new
            {
                s.Id, s.AccountId, AccountName = s.Account.Name, s.SnapshotDate,
                s.BankBalance, s.CalculatedBalance, s.Difference
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>录入银行余额 → 生成快照并立即对账（计算余额、写差异、同步账户余额）。</summary>
    [HttpPost("snapshots")]
    public async Task<ActionResult> CreateSnapshot([FromBody] CreateSnapshotRequest req, CancellationToken ct)
    {
        var account = await db.FundAccounts.FindAsync([req.AccountId], ct);
        if (account == null) return NotFound(new { message = "账户不存在" });

        var flows = await db.BillRecords.AsNoTracking()
            .Where(r => r.FundAccountId == req.AccountId
                     && r.TransactionDate <= req.SnapshotDate
                     && r.TransactionType != TransactionType.Transfer)
            .Select(r => new { r.TransactionType, r.AmountMinor })
            .ToListAsync(ct);
        var income = flows.Where(f => f.TransactionType == TransactionType.Income).Sum(f => f.AmountMinor);
        var expense = flows.Where(f => f.TransactionType == TransactionType.Expense).Sum(f => f.AmountMinor);
        var calculated = (income - expense) / 100m;
        var diff = calculated - req.BankBalance;

        var snapshot = new BalanceSnapshot
        {
            AccountId = req.AccountId,
            SnapshotDate = req.SnapshotDate,
            BankBalance = req.BankBalance,
            CalculatedBalance = calculated,
            Difference = diff,
            CreatedAt = DateTime.Now
        };
        db.BalanceSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);

        // 录入的银行余额同步为账户当前余额
        account.Balance = req.BankBalance;

        if (Math.Abs(diff) > Tolerance)
        {
            db.ReconciliationIssues.Add(new ReconciliationIssue
            {
                AccountId = req.AccountId,
                SnapshotId = snapshot.Id,
                Difference = diff,
                Status = ReconciliationIssueStatus.Open,
                Note = $"计算余额 {calculated:F2} 与银行余额 {req.BankBalance:F2} 不一致",
                CreatedAt = DateTime.Now
            });
        }
        await db.SaveChangesAsync(ct);

        return Created($"/api/reconciliation/snapshots/{snapshot.Id}",
            new { snapshot.Id, calculated, difference = diff });
    }

    [HttpGet("issues")]
    public async Task<ActionResult> GetIssues([FromQuery] bool openOnly = true, CancellationToken ct = default)
    {
        var q = db.ReconciliationIssues.AsNoTracking().Include(i => i.Account).AsQueryable();
        if (openOnly) q = q.Where(i => i.Status == ReconciliationIssueStatus.Open);
        var rows = await q.OrderByDescending(i => i.CreatedAt)
            .Select(i => new
            {
                i.Id, i.AccountId, AccountName = i.Account.Name, i.Difference,
                Status = i.Status.ToString(), i.Note, i.CreatedAt, i.ResolvedAt
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("issues/{id:int}/resolve")]
    public async Task<ActionResult> ResolveIssue(int id, [FromBody] ResolveIssueRequest? req, CancellationToken ct)
    {
        var issue = await db.ReconciliationIssues.FindAsync([id], ct);
        if (issue == null) return NotFound(new { message = "差异不存在" });
        issue.Status = ReconciliationIssueStatus.Resolved;
        issue.ResolvedAt = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(req?.Note)) issue.Note = req!.Note;
        await db.SaveChangesAsync(ct);
        return Ok(new { resolved = id });
    }

    /// <summary>检测到的转账列表（金额以元展示）。</summary>
    [HttpGet("transfers")]
    public async Task<ActionResult> GetTransfers(CancellationToken ct)
    {
        var rows = await db.Transfers.AsNoTracking()
            .OrderByDescending(t => t.OccurredAt)
            .Select(t => new { t.Id, t.FromAccountId, t.ToAccountId, t.AmountMinor, t.OccurredAt, t.Status, t.MatchedRecordIds })
            .ToListAsync(ct);
        return Ok(rows.Select(t => new
        {
            t.Id, t.FromAccountId, t.ToAccountId, Amount = t.AmountMinor / 100m,
            t.OccurredAt, Status = t.Status.ToString(), t.MatchedRecordIds
        }));
    }
}

public record CreateSnapshotRequest(int AccountId, DateTime SnapshotDate, decimal BankBalance);
public record ResolveIssueRequest(string? Note);
