using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using MinGo.MyBillBook.Core.Inbox;

namespace MinGo.MyBillBook.Services.Inbox.Security;

/// <summary>
/// 基于 ASP.NET Core Data Protection 的敏感数据加密。密钥环持久化在
/// <c>Inbox:DataProtectionKeysPath</c>（默认 <c>data/keys</c>），必须与数据库一同备份。
/// </summary>
public sealed class InboxDataProtection : IInboxSensitiveDataProtector
{
    public const string Purpose = "MinGo.MyBillBook.Inbox.Sensitive.v1";

    private readonly IDataProtector _protector;

    public InboxDataProtection(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose);

    public bool IsProtected(string? value) => InboxSensitiveData.IsProtected(value);

    public string Protect(string plaintext)
        => InboxSensitiveData.Prefix + _protector.Protect(plaintext);

    public string Unprotect(string protectedValue)
    {
        if (!IsProtected(protectedValue))
            return protectedValue;

        try
        {
            return _protector.Unprotect(protectedValue[InboxSensitiveData.Prefix.Length..]);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "无法解密敏感数据：Data Protection 密钥缺失或不匹配，请检查 data/keys 是否保留", ex);
        }
    }
}
