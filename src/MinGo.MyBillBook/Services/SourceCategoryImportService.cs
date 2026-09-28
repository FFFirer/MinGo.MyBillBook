using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services;

public class SourceCategoryImportService(
    AppDbContext db,
    ICategoryNormalizer normalizer) : ISourceCategoryImportService
{
    public async Task<List<SourceCategoryScanItem>> ScanAsync(CancellationToken ct)
    {
        var allSourceCategories = await db.NormalizedTransactions
            .Where(t => !string.IsNullOrEmpty(t.SourceCategory))
            .GroupBy(t => t.SourceCategory)
            .Select(g => new SourceCategoryScanItem(g.Key, g.Count()))
            .ToListAsync(ct);

        var unmapped = new List<SourceCategoryScanItem>();
        foreach (var item in allSourceCategories)
        {
            var normalized = await normalizer.NormalizeAsync(item.SourceName, ct);
            if (normalized is null)
                unmapped.Add(item);
        }

        return unmapped.OrderByDescending(x => x.Count).ToList();
    }

    public async Task<List<int>> ImportAsync(IEnumerable<string> sourceNames, CancellationToken ct)
    {
        var maxSort = await db.BillCategories.MaxAsync(c => (int?)c.SortOrder, ct) ?? 0;
        var ids = new List<int>();

        foreach (var name in sourceNames)
        {
            var trimmed = name.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (await db.BillCategories.AnyAsync(c => c.SourceName == trimmed, ct))
                continue;

            var cat = new BillCategory
            {
                Name = trimmed,
                Icon = "category",
                SourceName = trimmed,
                SortOrder = ++maxSort,
            };
            db.BillCategories.Add(cat);
            await db.SaveChangesAsync(ct);
            ids.Add(cat.Id);
        }

        return ids;
    }
}
