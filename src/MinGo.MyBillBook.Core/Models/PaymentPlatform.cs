namespace MinGo.MyBillBook.Core.Models;

public class PaymentPlatform
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? IconUrl { get; set; }

    public ICollection<FundAccount> FundAccounts { get; set; } = [];
}
