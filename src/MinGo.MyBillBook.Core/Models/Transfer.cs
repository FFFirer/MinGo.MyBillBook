namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 账户间转账（设计第 10 节）。由 DetectTransferStep 识别：同日、等额、一正一负的成对记录，
/// 或 Direction=不计收支 / 含转账关键词（转账/还款/转入/转出）。识别后相关 BillRecord 标记
/// TransactionType.Transfer，避免"还款/转账被算两次消费"。不删除原始记录。
/// </summary>
public class Transfer
{
    public int Id { get; set; }

    /// <summary>转出账户（金额减少方），无法确定账户时为 null。</summary>
    public int? FromAccountId { get; set; }

    /// <summary>转入账户（金额增加方），无法确定账户时为 null。</summary>
    public int? ToAccountId { get; set; }

    /// <summary>转账金额（最小货币单位，分）。</summary>
    public long AmountMinor { get; set; }

    public DateTime OccurredAt { get; set; }

    public TransferStatus Status { get; set; } = TransferStatus.Detected;

    /// <summary>匹配到的 BillRecord Id 集合（逗号分隔），通常为转出/转入两条记录。</summary>
    public string MatchedRecordIds { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public FundAccount? FromAccount { get; set; }
    public FundAccount? ToAccount { get; set; }
}
