using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Pipeline;

namespace MinGo.MyBillBook.Services.Pipeline;

/// <summary>
/// 账单导入管道上下文。承载从原始记录到 Canonical 记录的工作集，
/// 各步骤读写此上下文完成数据流转。
/// </summary>
public class BillImportContext : IPipelineContext
{
    public int RunId { get; set; }
    public int? ImportBatchId { get; set; }
    public string Source { get; set; } = "import";
    public Dictionary<string, object?> Options { get; } = new();
    public Dictionary<string, int> Statistics { get; } = new();
    public List<PipelineIssue> Issues { get; } = new();

    /// <summary>当前处理的导入批次。</summary>
    public BillImportBatch Batch { get; set; } = null!;

    /// <summary>待处理的原始记录工作集。</summary>
    public List<BillRawRecord> RawRecords { get; set; } = new();

    /// <summary>由 RawPayload 解析出的标准化交易工作集（Normalize 层输出）。</summary>
    public List<NormalizedTransaction> NormalizedTransactions { get; set; } = new();

    /// <summary>商户归一化结果，按 NormalizedTransaction.Id 索引（ResolveMerchant 步骤产出，Classify 步骤消费）。</summary>
    public Dictionary<int, MerchantResolution> MerchantResolutions { get; set; } = new();

    /// <summary>分类后、待持久化的 Canonical 记录工作集。</summary>
    public List<BillRecord> CanonicalRecords { get; set; } = new();

    /// <summary>
    /// 待写入的溯源决策。BillRecord 持久化后才有 Id，故在此暂存记录与决策的配对，
    /// 由 PublishCanonicalStep 在 SaveChanges 后回填 TransactionId 并落库。
    /// </summary>
    public List<(BillRecord Record, ClassificationResult Result)> PendingClassifications { get; set; } = new();

    /// <summary>
    /// 待写入的疑似重复候选（DeduplicateStep 产出）。按新记录的 RawRecordId 索引，
    /// PublishCanonicalStep 在新 BillRecord 获得 Id 后回填 RightRecordId 并落库。
    /// </summary>
    public List<(int RawRecordId, int LeftRecordId, double Score, string Reason)> PendingDuplicates { get; set; } = new();

    /// <summary>
    /// 待写入的交易标签（ApplyTagsStep 产出）。BillRecord 持久化后才有 Id，
    /// 由 PublishCanonicalStep 回填 TransactionId 并落库（不覆盖已存在的 User 标签）。
    /// </summary>
    public List<(BillRecord Record, int TagId, TagSource Source, double Confidence)> PendingTags { get; set; } = new();

    /// <summary>
    /// 待写入的转账（DetectTransferStep 产出）。BillRecord 持久化后才有 Id，
    /// 由 PublishCanonicalStep 回填 MatchedRecordIds 并落库（From=转出方，To=转入方）。
    /// </summary>
    public List<(BillRecord From, BillRecord To, long AmountMinor, DateTime OccurredAt)> PendingTransfers { get; set; } = new();
}
