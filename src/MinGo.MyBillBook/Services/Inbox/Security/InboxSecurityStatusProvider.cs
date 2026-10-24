using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Inbox;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Inbox.Security;

public sealed record InboxEncryptionStatus(
    string Provider,
    string Purpose,
    string KeysPath,
    int Keys,
    bool Operational,
    string? Error);

public sealed record InboxSecretStatus(int Total, int Encrypted, int Plaintext);

public sealed record InboxHistoryStatus(int RetentionDays, DateTime Cutoff, int CandidateRuns);

public sealed record InboxSecurityStatus(
    InboxEncryptionStatus Encryption,
    InboxSecretStatus Secrets,
    int SensitiveTodosPending,
    int SensitiveTodosEncrypted,
    int SensitiveTodosPlaintext,
    int ActiveRuns,
    int AuditEvents,
    InboxHistoryStatus History);

/// <summary>
/// 汇总只读安全状态：加密组件是否可用、Secret 加密比例、敏感待办落库形态与历史保留。
/// 所有输出都不包含 Secret 或待办值本身。
/// </summary>
public sealed class InboxSecurityStatusProvider(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    IKeyManager keyManager,
    IInboxSensitiveDataProtector protector,
    InboxSecrets secrets,
    InboxHistoryCleaner historyCleaner,
    AppDbContext db)
{
    public async Task<InboxSecurityStatus> GetAsync(CancellationToken ct)
    {
        var keysPath = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            configuration["Inbox:DataProtectionKeysPath"] ?? "data/keys"));

        var keys = 0;
        try
        {
            keys = keyManager.GetAllKeys().Count;
        }
        catch
        {
            // 密钥环不可读时由 Operational 检测报告具体错误
        }

        var operational = false;
        string? error = null;
        try
        {
            var probe = Guid.NewGuid().ToString("N");
            operational = protector.Unprotect(protector.Protect(probe)) == probe;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        var sensitiveValues = await db.InboxTodos.AsNoTracking()
            .Where(t => t.IsSensitive && t.InputValue != null && t.InputValue != "")
            .Select(t => t.InputValue!)
            .ToListAsync(ct);

        var encryptedTodos = sensitiveValues.Count(InboxSensitiveData.IsProtected);
        var pendingTodos = await db.InboxTodos
            .CountAsync(t => t.IsSensitive && t.Status == InboxTodoStatus.Pending, ct);
        var activeRuns = await db.InboxRuns.CountAsync(r =>
            r.Status == InboxRunStatus.Queued
            || r.Status == InboxRunStatus.Running
            || r.Status == InboxRunStatus.WaitingInput, ct);

        var stats = secrets.Stats();
        var retentionDays = historyCleaner.RetentionDays;
        var cutoff = historyCleaner.GetCutoff();
        var candidates = retentionDays <= 0
            ? 0
            : await db.InboxRuns.CountAsync(r =>
                (r.Status == InboxRunStatus.Completed
                 || r.Status == InboxRunStatus.Failed
                 || r.Status == InboxRunStatus.Cancelled)
                && (r.CompletedAt ?? r.CreatedAt) < cutoff, ct);

        return new InboxSecurityStatus(
            new InboxEncryptionStatus(
                "ASP.NET Core Data Protection",
                InboxDataProtection.Purpose,
                keysPath,
                keys,
                operational,
                error),
            new InboxSecretStatus(stats.Total, stats.Encrypted, stats.Plaintext),
            pendingTodos,
            encryptedTodos,
            sensitiveValues.Count - encryptedTodos,
            activeRuns,
            await db.InboxAuditEvents.CountAsync(ct),
            new InboxHistoryStatus(retentionDays, cutoff, candidates));
    }
}
