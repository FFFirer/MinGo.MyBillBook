namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 分类-标签关联（设计第 4 节）。限定某分类下可用的 Tag 集合（Scope=Category 时生效）。
/// </summary>
public class CategoryTag
{
    public int CategoryId { get; set; }
    public int TagId { get; set; }

    public BillCategory Category { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
