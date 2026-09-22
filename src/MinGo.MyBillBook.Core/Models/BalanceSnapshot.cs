namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 账户余额快照（设计第 11 节）。记录某账户在某日期的银行真实余额(BankBalance)与
/// 系统按交易计算的余额(CalculatedBalance)，二者差异用于对账。
/// 金额以元存储（面向用户录入/展示的对账语义）。
/// </summary>
public class BalanceSnapshot
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public DateTime SnapshotDate { get; set; }

    /// <summary>用户录入的银行/平台真实余额（元）。</summary>
    public decimal BankBalance { get; set; }

    /// <summary>系统按交易计算的余额（元）= Opening + Income - Expense ± Transfer。</summary>
    public decimal CalculatedBalance { get; set; }

    /// <summary>差异 = CalculatedBalance - BankBalance。</summary>
    public decimal Difference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public FundAccount Account { get; set; } = null!;
    public ICollection<ReconciliationIssue> Issues { get; set; } = [];
}
