namespace MinGo.MyBillBook.Core.Parsing;

public class RawBillRow
{
    public DateTime TransactionDate { get; set; }
    /// <summary>金额（最小货币单位，分）。</summary>
    public long AmountMinor { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Counterparty { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    /// <summary>支付流水号（如支付宝"交易号"、微信"交易号"），与 TransactionId（商户订单号）不同。</summary>
    public string PaymentTransactionId { get; set; } = string.Empty;
    /// <summary>原始交易分类（如支付宝的"交易分类"列），可能为空。</summary>
    public string SourceCategory { get; set; } = string.Empty;
    public Dictionary<string, string> ExtraFields { get; set; } = [];
}

public class ParseResult
{
    public List<RawBillRow> Rows { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public bool Success => Errors.Count == 0;
}

public interface IBillParser
{
    string PlatformCode { get; }
    ParseResult Parse(Stream fileStream, string fileName);
}

public interface IBillParserFactory
{
    IBillParser? DetectParser(Stream fileStream, string fileName);
    IBillParser? GetParser(string platformCode);
}
