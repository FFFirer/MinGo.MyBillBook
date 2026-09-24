namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 标准化交易（Normalize 层输出）。由 <c>NormalizeStep</c> 从 <see cref="BillRawRecord.RawPayload"/>
/// 解析而来，是后续 Resolve/Classify/Tag 等步骤的统一输入，避免各步骤重复解析原始 JSON。
/// 金额以最小货币单位（分）存储。
/// </summary>
public class NormalizedTransaction
{
    public int Id { get; set; }
    public int RawRecordId { get; set; }

    /// <summary>交易发生时间。</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>金额（最小货币单位，分）。</summary>
    public long AmountMinor { get; set; }

    /// <summary>ISO 4217 货币代码，默认 CNY。</summary>
    public string Currency { get; set; } = "CNY";

    /// <summary>原始描述文本（商品说明等），用于商户/分类匹配与快照。</summary>
    public string RawDescription { get; set; } = string.Empty;

    /// <summary>交易对方。</summary>
    public string Counterparty { get; set; } = string.Empty;

    /// <summary>商品名称。</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>原始收/支方向文本（收入/支出/不计收支）。</summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>收/付款方式（如余额宝/支付宝余额），用于映射资金账户。</summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>原始交易状态文本。</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>来源交易号（商户订单号），用于去重评分。</summary>
    public string SourceTransactionId { get; set; } = string.Empty;

    /// <summary>来源支付流水号（平台交易号），辅助去重与溯源。</summary>
    public string SourcePaymentTransactionId { get; set; } = string.Empty;

    /// <summary>原始交易分类文本（如支付宝"交易分类"列），用于归一化映射。</summary>
    public string SourceCategory { get; set; } = string.Empty;

    /// <summary>去重评分结果状态（DeduplicateStep 写入）。</summary>
    public DuplicateStatus DuplicateStatus { get; set; } = DuplicateStatus.Unique;

    /// <summary>去重评分（0-100），阈值判定依据。</summary>
    public double DuplicateScore { get; set; }

    public BillRawRecord RawRecord { get; set; } = null!;
}
