namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 重复候选对（设计第 6 节）。当两条记录的去重评分落在"疑似重复"区间（80-99）时写入，
/// 供人工复核：LeftRecord 为已存在的 Canonical 记录，RightRecord 为本次新发布的记录。
/// Score≥100 的确定重复不进入本表（直接标 Duplicate 跳过发布）。
/// </summary>
public class DuplicateCandidate
{
    public int Id { get; set; }

    /// <summary>已存在的 Canonical 记录 Id。</summary>
    public int LeftRecordId { get; set; }

    /// <summary>本次新发布的 Canonical 记录 Id。</summary>
    public int RightRecordId { get; set; }

    /// <summary>去重评分（0-100）。</summary>
    public double Score { get; set; }

    /// <summary>匹配原因说明（命中的评分规则）。</summary>
    public string MatchReason { get; set; } = string.Empty;

    /// <summary>复核状态。</summary>
    public DuplicateCandidateStatus Status { get; set; } = DuplicateCandidateStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public BillRecord? LeftRecord { get; set; }
    public BillRecord? RightRecord { get; set; }
}
