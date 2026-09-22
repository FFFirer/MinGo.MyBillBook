namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 标签规则（设计第 7 节）。按条件维度自动为交易打 Tag，命中写 Source=Rule。
/// ConditionValue 语义随 Condition 变化：
/// Category/Merchant → 目标 Id；DayOfWeek → 逗号分隔英文缩写（如 "Sat,Sun"）；
/// AmountRange → "min-max"（元，闭区间）；Keyword → 子串（匹配描述/交易对方/商品名）。
/// </summary>
public class TagRule
{
    public int Id { get; set; }
    public TagRuleCondition Condition { get; set; } = TagRuleCondition.Keyword;
    public string ConditionValue { get; set; } = string.Empty;
    public int TagId { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;

    public Tag Tag { get; set; } = null!;
}
