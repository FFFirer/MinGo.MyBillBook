namespace MinGo.MyBillBook.Core.Models;

public class FundAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PlatformId { get; set; }
    public AccountType AccountType { get; set; }
    public decimal Balance { get; set; }
    public bool IsActive { get; set; } = true;

    public PaymentPlatform Platform { get; set; } = null!;
}
