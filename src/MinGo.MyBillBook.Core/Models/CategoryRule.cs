namespace MinGo.MyBillBook.Core.Models;

public class CategoryRule
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public MatchField MatchField { get; set; }
    public string MatchPattern { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;

    public BillCategory Category { get; set; } = null!;
}
