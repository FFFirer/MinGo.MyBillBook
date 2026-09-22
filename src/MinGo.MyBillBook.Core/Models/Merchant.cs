namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 归一化商户实体（设计第 4/7 节）。将 "SQ *STARBUCKS #123"、"星巴克咖啡" 等
/// 多种写法收敛到同一 CanonicalName，使分析可按真实商户聚合。
/// </summary>
public class Merchant
{
    public int Id { get; set; }
    public string CanonicalName { get; set; } = string.Empty;

    /// <summary>
    /// 商户默认分类（设计第 8 节分类优先级链中的 MerchantRule 层）。
    /// 当无显式分类规则命中时，回退到该商户预设的分类。
    /// </summary>
    public int? DefaultCategoryId { get; set; }

    public DateTime CreatedAt { get; set; }

    public BillCategory? DefaultCategory { get; set; }
    public ICollection<MerchantAlias> Aliases { get; set; } = [];
    public ICollection<BillRecord> BillRecords { get; set; } = [];
}
