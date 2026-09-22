using System.Text.RegularExpressions;
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
        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.CanonicalName == fallbackName, ct);
        if (merchant is null)
        {
            merchant = new Merchant { CanonicalName = fallbackName, CreatedAt = DateTime.Now };
            db.Merchants.Add(merchant);
            await db.SaveChangesAsync(ct);
        }

        return new MerchantResolution(merchant.Id, merchant.CanonicalName, 0.3, null, ClassificationSource.Default);
    }

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
