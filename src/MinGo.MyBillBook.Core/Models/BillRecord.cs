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
    public int? CategoryId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionType TransactionType { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public bool IsManualAdjusted { get; set; }
    public bool SyncedToDuckDb { get; set; }

    public BillRawRecord RawRecord { get; set; } = null!;
    public PaymentPlatform Platform { get; set; } = null!;
    public FundAccount? FundAccount { get; set; }
    public BillCategory? Category { get; set; }
}
