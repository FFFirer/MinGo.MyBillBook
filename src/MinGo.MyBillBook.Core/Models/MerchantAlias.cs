namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 商户别名规则（设计第 4/7 节）。将原始交易对方/商品描述中的多种写法
/// 映射到统一的 <see cref="Merchant"/>。按 Priority 降序匹配，命中即归一。
/// </summary>
public class MerchantAlias
{
    public int Id { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public AliasMatchType MatchType { get; set; } = AliasMatchType.Contains;
    public int MerchantId { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;

    public Merchant Merchant { get; set; } = null!;
}
