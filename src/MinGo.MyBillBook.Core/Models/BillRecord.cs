namespace MinGo.MyBillBook.Core.Models;

public class BillRecord
{
    public int Id { get; set; }
    public int RawRecordId { get; set; }
    public int PlatformId { get; set; }
    public int? FundAccountId { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Counterparty { get; set; } = string.Empty;
    public string Merchant { get; set; } = string.Empty;
    /// <summary>归一化商户 FK（设计第 4 节）。Merchant 字符串保留为原始快照。</summary>
    public int? MerchantId { get; set; }
    public int? CategoryId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    /// <summary>金额（最小货币单位，分）。</summary>
    public long AmountMinor { get; set; }
    public TransactionType TransactionType { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public bool IsManualAdjusted { get; set; }
    public bool SyncedToDuckDb { get; set; }

    public BillRawRecord RawRecord { get; set; } = null!;
    public PaymentPlatform Platform { get; set; } = null!;
    public FundAccount? FundAccount { get; set; }
    public BillCategory? Category { get; set; }
    public Merchant? MerchantRef { get; set; }
    public ICollection<ClassificationResult> ClassificationResults { get; set; } = [];
    public ICollection<TransactionTag> Tags { get; set; } = [];
}
