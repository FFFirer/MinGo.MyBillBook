namespace MinGo.MyBillBook.Core.Models;

public class BillCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }

    public BillCategory? Parent { get; set; }
    public ICollection<BillCategory> Children { get; set; } = [];
    public ICollection<CategoryRule> Rules { get; set; } = [];
}
