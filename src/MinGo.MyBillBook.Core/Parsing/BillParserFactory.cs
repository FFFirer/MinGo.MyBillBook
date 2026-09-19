namespace MinGo.MyBillBook.Core.Parsing;

public class BillParserFactory(IEnumerable<IBillParser> parsers) : IBillParserFactory
{
    public IBillParser? DetectParser(Stream fileStream, string fileName)
    {
        var name = fileName.ToLowerInvariant();
        if (name.Contains("alipay") || name.Contains("支付宝"))
            return parsers.FirstOrDefault(p => p.PlatformCode == "ALIPAY");
        if (name.Contains("wechat") || name.Contains("微信") || name.EndsWith(".xlsx") || name.EndsWith(".xls"))
            return parsers.FirstOrDefault(p => p.PlatformCode == "WECHAT");

        return null;
    }

    public IBillParser? GetParser(string platformCode)
    {
        return parsers.FirstOrDefault(p => p.PlatformCode == platformCode);
    }
}
