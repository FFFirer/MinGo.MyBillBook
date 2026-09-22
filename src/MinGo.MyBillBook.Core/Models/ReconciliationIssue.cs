namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 对账差异（设计第 11 节）。当系统计算余额与录入的银行余额不一致时生成，
/// 仅记录差异供人工核查，不偷偷修改任何交易（符合设计原则）。
/// </summary>
public class ReconciliationIssue
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int? SnapshotId { get; set; }

    /// <summary>差异金额（元）= CalculatedBalance - BankBalance。</summary>
    public decimal Difference { get; set; }

    public ReconciliationIssueStatus Status { get; set; } = ReconciliationIssueStatus.Open;

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ResolvedAt { get; set; }

    public FundAccount Account { get; set; } = null!;
    public BalanceSnapshot? Snapshot { get; set; }
}
