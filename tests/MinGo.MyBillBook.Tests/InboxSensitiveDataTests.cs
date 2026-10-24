using System.Text;
using MinGo.MyBillBook.Core.Inbox;

namespace MinGo.MyBillBook.Tests;

public class InboxSensitiveDataTests
{
    private static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        ["password"] = "s3cret",
        ["input_type"] = "password",
        ["note"] = "hello",
        ["empty"] = string.Empty,
        ["api_token"] = "abc"
    };

    [Theory]
    [InlineData("password")]
    [InlineData("dbPassword")]
    [InlineData("api_key")]
    [InlineData("apiKey")]
    [InlineData("access_token")]
    [InlineData("client_secret")]
    [InlineData("private_key")]
    [InlineData("credential")]
    [InlineData("passphrase")]
    public void IsSensitive_DetectsKnownFragments(string key)
        => Assert.True(InboxSensitiveKeys.IsSensitive(key));

    [Theory]
    [InlineData("input_type")]
    [InlineData("username")]
    [InlineData("note")]
    public void IsSensitive_IgnoresNonSensitiveKeys(string key)
        => Assert.False(InboxSensitiveKeys.IsSensitive(key));

    [Fact]
    public void ProtectValues_EncryptsOnlyPopulatedSensitiveValues()
    {
        var protector = new FakeProtector();

        var result = InboxSensitiveData.ProtectValues(Values, protector);

        Assert.StartsWith(InboxSensitiveData.Prefix, result["password"]);
        Assert.StartsWith(InboxSensitiveData.Prefix, result["api_token"]);
        Assert.Equal("password", result["input_type"]);
        Assert.Equal("hello", result["note"]);
        Assert.Equal(string.Empty, result["empty"]);
    }

    [Fact]
    public void ProtectValues_IsIdempotentAndCanSkipExpressions()
    {
        var protector = new FakeProtector();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["password"] = protector.Protect("already"),
            ["db_password"] = "${{ secrets.db_password }}"
        };

        var once = InboxSensitiveData.ProtectValues(values, protector, skipExpressions: true);
        var twice = InboxSensitiveData.ProtectValues(once, protector, skipExpressions: true);

        Assert.Equal(once["password"], twice["password"]);
        Assert.Equal("${{ secrets.db_password }}", once["db_password"]);
        Assert.Equal("${{ secrets.db_password }}", twice["db_password"]);

        // 执行期不跳过表达式：求值后的真实值应被加密。
        var resolved = InboxSensitiveData.ProtectValues(values, protector);
        Assert.StartsWith(InboxSensitiveData.Prefix, resolved["db_password"]);
        Assert.Equal("${{ secrets.db_password }}",
            protector.Unprotect(resolved["db_password"]));
    }

    [Fact]
    public void UnprotectValues_RoundTripsSensitiveValuesAndKeepsPlaintext()
    {
        var protector = new FakeProtector();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["password"] = "s3cret",
            ["legacy"] = "plain",
            ["note"] = "hello"
        };

        var protectedValues = InboxSensitiveData.ProtectValues(values, protector);
        var result = InboxSensitiveData.UnprotectValues(protectedValues, protector);

        Assert.Equal("s3cret", result["password"]);
        Assert.Equal("plain", result["legacy"]);
        Assert.Equal("hello", result["note"]);
    }

    [Fact]
    public void ProtectJson_IsIdempotentAndUnprotectsRoundTrip()
    {
        var protector = new FakeProtector();
        const string json = """{"value":"s3cret"}""";

        var protectedJson = InboxSensitiveData.ProtectJson(json, protector);
        var protectedAgain = InboxSensitiveData.ProtectJson(protectedJson, protector);

        Assert.StartsWith(InboxSensitiveData.Prefix, protectedJson);
        Assert.Equal(protectedJson, protectedAgain);
        Assert.Equal(json, InboxSensitiveData.UnprotectJson(protectedJson, protector));
        Assert.Equal(json, InboxSensitiveData.UnprotectJson(json, protector));
    }

    private sealed class FakeProtector : IInboxSensitiveDataProtector
    {
        public bool IsProtected(string? value) => InboxSensitiveData.IsProtected(value);

        public string Protect(string plaintext)
            => InboxSensitiveData.Prefix + Encode(plaintext);

        public string Unprotect(string protectedValue)
            => IsProtected(protectedValue)
                ? Decode(protectedValue[InboxSensitiveData.Prefix.Length..])
                : protectedValue;

        private static string Encode(string value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        private static string Decode(string value)
            => Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}
