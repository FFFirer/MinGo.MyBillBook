namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 清洗决策溯源记录（设计第 11 节）。每笔交易在 Merchant/Category/Tag 等字段上的
/// 每一次赋值都留痕：值是什么、由谁决定（Source）、命中了哪条规则、置信度多少。
/// 优先级判断与"为何被这样分类"的追问都读取此表，而非依赖 BillRecord 上的布尔标记。
/// </summary>
public class ClassificationResult
{
    public int Id { get; set; }

    /// <summary>关联交易（= BillRecord.Id）。</summary>
    public int TransactionId { get; set; }

    /// <summary>决策字段维度。</summary>
    public ClassificationField Field { get; set; }

    /// <summary>决策结果值（分类 Id、商户 Id、Tag Id 等的字符串形式）。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>决策来源。</summary>
    public ClassificationSource Source { get; set; }

    /// <summary>命中的规则 Id（若有）。</summary>
    public int? RuleId { get; set; }

    /// <summary>置信度（0~1）。</summary>
    public double Confidence { get; set; }

    public DateTime CreatedAt { get; set; }

    public BillRecord Transaction { get; set; } = null!;
}
