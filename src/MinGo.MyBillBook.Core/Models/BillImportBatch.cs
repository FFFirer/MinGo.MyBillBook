namespace MinGo.MyBillBook.Core.Models;

public class BillImportBatch
{
    public int Id { get; set; }
    public int PlatformId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime ImportDate { get; set; }
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int DuplicateCount { get; set; }
    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Imported;

    /// <summary>原始文件在对象存储中的 key，用于重新解析（ReParse）。</summary>
    public string? OriginalFileKey { get; set; }

    public PaymentPlatform Platform { get; set; } = null!;
    public ICollection<BillRawRecord> RawRecords { get; set; } = [];
}
