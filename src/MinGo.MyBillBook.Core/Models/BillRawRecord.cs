namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 原始账单记录（System of Record 的不可变原始层）。
/// 只保存来源原样数据，不做任何结构化解析；解析结果进入 <see cref="NormalizedTransaction"/>。
/// 符合设计"不要直接删除/篡改 Raw 数据"的原则。
/// </summary>
public class BillRawRecord
{
    public int Id { get; set; }
    public int ImportBatchId { get; set; }
    public int PlatformId { get; set; }
    public int RowNumber { get; set; }

    /// <summary>原始行序列化后的 JSON 载荷（保留解析器输出的全部字段）。</summary>
    public string RawPayload { get; set; } = string.Empty;

    /// <summary>来源系统的交易号，用于跨批次去重（对应设计 SourceTransactionId）。</summary>
    public string SourceTransactionId { get; set; } = string.Empty;

    /// <summary>来源系统的支付流水号（如支付宝/微信的"交易号"），辅助去重与溯源。</summary>
    public string SourcePaymentTransactionId { get; set; } = string.Empty;

    public bool IsProcessed { get; set; }

    /// <summary>去重评分结果状态（DeduplicateStep 回写，供追溯）。</summary>
    public DuplicateStatus DuplicateStatus { get; set; } = DuplicateStatus.Unique;

    /// <summary>去重评分（0-100）。</summary>
    public double DuplicateScore { get; set; }

    public BillImportBatch ImportBatch { get; set; } = null!;
    public PaymentPlatform Platform { get; set; } = null!;
}
