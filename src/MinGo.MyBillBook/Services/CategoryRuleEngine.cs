using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services;

public class CategoryRuleEngine(AppDbContext db) : ICategoryRuleEngine
{
    public int? MatchCategory(string counterparty, string productName, string? merchant = null)
    {
        var rules = db.CategoryRules
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ToList();

        foreach (var rule in rules)
        {
            var text = rule.MatchField switch
            {
                MatchField.Counterparty => counterparty,
                MatchField.ProductName => productName,
                MatchField.Merchant => merchant ?? string.Empty,
                _ => string.Empty
            };

            if (text.Contains(rule.MatchPattern, StringComparison.OrdinalIgnoreCase))
                return rule.CategoryId;
        }

        return null;
    }
}
