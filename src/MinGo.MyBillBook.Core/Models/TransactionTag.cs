namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 交易-标签关联（设计第 7 节，复合主键 + 溯源）。Source 记录标签来源，
/// User 手工标签优先级最高，自动规则（Rule/AI）不得覆盖已存在的 User 标签。
/// </summary>
public class TransactionTag
{
    /// <summary>交易 Id（= BillRecord.Id）。</summary>
    public int TransactionId { get; set; }

    public int TagId { get; set; }

    public TagSource Source { get; set; } = TagSource.Rule;

    public double Confidence { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public BillRecord Transaction { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
