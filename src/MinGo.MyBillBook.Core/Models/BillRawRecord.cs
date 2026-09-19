namespace MinGo.MyBillBook.Core.Models;

public class BillRawRecord
{
    public int Id { get; set; }
    public int ImportBatchId { get; set; }
    public int PlatformId { get; set; }
    public string RawData { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Counterparty { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public bool IsProcessed { get; set; }

    public BillImportBatch ImportBatch { get; set; } = null!;
    public PaymentPlatform Platform { get; set; } = null!;
}
