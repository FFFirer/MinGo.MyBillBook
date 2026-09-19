namespace MinGo.MyBillBook.Core.Models;

public enum TransactionType
{
    Income = 0,
    Expense = 1,
    Transfer = 2
}

public enum AccountType
{
    Balance = 0,       // 余额
    YuEBao = 1,        // 余额宝
    BankCard = 2,      // 银行卡
    LingQian = 3,      // 零钱
    LingQianTong = 4   // 零钱通
}

public enum ImportBatchStatus
{
    Imported = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public enum MatchField
{
    Counterparty = 0,
    ProductName = 1,
    Merchant = 2
}
