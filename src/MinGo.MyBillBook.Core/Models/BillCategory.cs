namespace MinGo.MyBillBook.Core.Models;

public class BillCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }

    /// <summary>
    /// 是否为默认兜底分类（设计第 8 节分类优先级链末端 Default 层）。
    /// 当所有规则/商户/AI 均未命中时，落到该分类而非 null。全局应仅有一个。
    /// </summary>
    public bool IsDefault { get; set; }

    public BillCategory? Parent { get; set; }
    public ICollection<BillCategory> Children { get; set; } = [];
    public ICollection<CategoryRule> Rules { get; set; } = [];
    public ICollection<CategoryTag> CategoryTags { get; set; } = [];
}
