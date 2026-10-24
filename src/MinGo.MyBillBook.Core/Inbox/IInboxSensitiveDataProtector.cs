namespace MinGo.MyBillBook.Core.Inbox;

/// <summary>
/// 敏感数据静态加密抽象。实现位于 Server（Data Protection），Core 只负责调用。
/// 加密后的值统一带 <see cref="InboxSensitiveData.Prefix"/> 前缀，便于识别与幂等处理。
/// </summary>
public interface IInboxSensitiveDataProtector
{
    bool IsProtected(string? value);
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
