using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services;

/// <summary>
/// 商户归一化解析器。参照 <see cref="CategoryRuleEngine"/> 模式，按 MerchantAlias 优先级
/// 匹配 Regex/Contains/Exact，命中返回统一 Merchant；未命中则按交易对方原值创建/复用低置信度 Merchant。
/// </summary>
public class MerchantResolver(AppDbContext db) : IMerchantResolver
{
    // 进程级互斥：序列化"查无则建"临界区，避免并发管道作用域同时插入同名商户触发唯一约束。
    private static readonly SemaphoreSlim FallbackCreateLock = new(1, 1);

    // 作用域内缓存：同一交易对方在一次管道运行中只查库/建商户一次。
    private readonly Dictionary<string, MerchantResolution> _fallbackCache = new(StringComparer.Ordinal);

    public async Task<MerchantResolution> ResolveAsync(string counterparty, string description, CancellationToken ct = default)
    {
        var aliases = await db.MerchantAliases
            .AsNoTracking()
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.Priority)
            .ToListAsync(ct);

        foreach (var alias in aliases)
        {
            if (Matches(alias, counterparty) || Matches(alias, description))
            {
                var name = await db.Merchants
                    .Where(m => m.Id == alias.MerchantId)
                    .Select(m => m.CanonicalName)
                    .FirstOrDefaultAsync(ct) ?? counterparty;

                return new MerchantResolution(alias.MerchantId, name, 0.95, alias.Id, ClassificationSource.ExplicitRule);
            }
        }

        // 未命中任何别名：按交易对方原值查找或创建 Merchant（低置信度，来源记为 Default）。
        var fallbackName = string.IsNullOrWhiteSpace(counterparty) ? "未知商户" : counterparty.Trim();
        if (_fallbackCache.TryGetValue(fallbackName, out var cached))
            return cached;

        var fallback = await GetOrCreateFallbackAsync(fallbackName, ct);
        _fallbackCache[fallbackName] = fallback;
        return fallback;
    }

    /// <summary>
    /// 查无则建低置信度商户。持进程级锁并在唯一约束冲突时回读已存在记录，保证并发/多实例下幂等。
    /// </summary>
    private async Task<MerchantResolution> GetOrCreateFallbackAsync(string fallbackName, CancellationToken ct)
    {
        await FallbackCreateLock.WaitAsync(ct);
        try
        {
            var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.CanonicalName == fallbackName, ct);
            if (merchant is null)
            {
                merchant = new Merchant { CanonicalName = fallbackName, CreatedAt = DateTime.Now };
                db.Merchants.Add(merchant);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    // 并发下其它作用域/进程已插入同名商户：放弃本次新增，回读已存在记录。
                    db.Entry(merchant).State = EntityState.Detached;
                    merchant = await db.Merchants.FirstAsync(m => m.CanonicalName == fallbackName, ct);
                }
            }

            return new MerchantResolution(merchant.Id, merchant.CanonicalName, 0.3, null, ClassificationSource.Default);
        }
        finally
        {
            FallbackCreateLock.Release();
        }
    }

    /// <summary>SQLite Error 19 = CONSTRAINT 违反（含唯一约束）。</summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19 };

    private static bool Matches(MerchantAlias alias, string text)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(alias.Pattern))
            return false;

        return alias.MatchType switch
        {
            AliasMatchType.Exact => string.Equals(text.Trim(), alias.Pattern.Trim(), StringComparison.OrdinalIgnoreCase),
            AliasMatchType.Contains => text.Contains(alias.Pattern, StringComparison.OrdinalIgnoreCase),
            AliasMatchType.Regex => Regex.IsMatch(text, alias.Pattern, RegexOptions.IgnoreCase),
            _ => false
        };
    }
}
