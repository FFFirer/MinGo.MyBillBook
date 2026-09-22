using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;
using Microsoft.EntityFrameworkCore;

namespace MinGo.MyBillBook.Services;

public class BillQueryService(AppDbContext db) : IBillQueryService
{
    public async Task<PagedResult<BillDto>> QueryAsync(BillQueryFilter filter, CancellationToken ct = default)
    {
        var query = db.BillRecords
            .Include(r => r.Platform)
            .Include(r => r.Category)
            .Include(r => r.FundAccount)
            .AsQueryable();

        if (filter.PlatformId.HasValue) query = query.Where(r => r.PlatformId == filter.PlatformId);
        if (filter.CategoryId.HasValue) query = query.Where(r => r.CategoryId == filter.CategoryId);
        if (filter.FundAccountId.HasValue) query = query.Where(r => r.FundAccountId == filter.FundAccountId);
        if (filter.StartDate.HasValue) query = query.Where(r => r.TransactionDate >= filter.StartDate.Value);
        if (filter.EndDate.HasValue) query = query.Where(r => r.TransactionDate <= filter.EndDate.Value);
        if (filter.MinAmount.HasValue)
        {
            var min = (long)Math.Round(filter.MinAmount.Value * 100m, MidpointRounding.AwayFromZero);
            query = query.Where(r => r.AmountMinor >= min);
        }
        if (filter.MaxAmount.HasValue)
        {
            var max = (long)Math.Round(filter.MaxAmount.Value * 100m, MidpointRounding.AwayFromZero);
            query = query.Where(r => r.AmountMinor <= max);
        }
        if (filter.TransactionType.HasValue) query = query.Where(r => r.TransactionType == filter.TransactionType);
        if (filter.TagIds is { Count: > 0 })
        {
            var tagIds = filter.TagIds;
            query = query.Where(r => r.Tags.Any(t => tagIds.Contains(t.TagId)));
        }
        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword;
            query = query.Where(r => r.Counterparty.Contains(kw) || r.Merchant.Contains(kw) || r.ProductName.Contains(kw));
        }

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(r => r.TransactionDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(r => new
            {
                r.Id, r.TransactionDate, r.Counterparty, r.Merchant,
                CategoryName = r.Category != null ? r.Category.Name : null,
                r.CategoryId, r.ProductName, r.AmountMinor,
                TransactionType = r.TransactionType.ToString(), PlatformName = r.Platform.Name,
                FundAccountName = r.FundAccount != null ? r.FundAccount.Name : null,
                r.Status, r.IsManualAdjusted,
                Tags = r.Tags.Select(t => t.Tag.Name).ToList()
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new BillDto
        {
            Id = r.Id, TransactionDate = r.TransactionDate, Counterparty = r.Counterparty,
            Merchant = r.Merchant, CategoryName = r.CategoryName, CategoryId = r.CategoryId,
            ProductName = r.ProductName, Amount = r.AmountMinor / 100m,
            TransactionType = r.TransactionType, PlatformName = r.PlatformName,
            FundAccountName = r.FundAccountName, Status = r.Status, IsManualAdjusted = r.IsManualAdjusted,
            Tags = r.Tags
        }).ToList();

        return new PagedResult<BillDto>(items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<BillDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var r = await db.BillRecords.Include(x => x.Platform).Include(x => x.Category).Include(x => x.FundAccount)
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.TransactionDate, x.Counterparty, x.Merchant,
                CategoryName = x.Category != null ? x.Category.Name : null,
                x.CategoryId, x.ProductName, x.AmountMinor,
                TransactionType = x.TransactionType.ToString(), PlatformName = x.Platform.Name,
                FundAccountName = x.FundAccount != null ? x.FundAccount.Name : null,
                x.Status, x.IsManualAdjusted,
                Tags = x.Tags.Select(t => t.Tag.Name).ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (r is null) return null;
        return new BillDto
        {
            Id = r.Id, TransactionDate = r.TransactionDate, Counterparty = r.Counterparty,
            Merchant = r.Merchant, CategoryName = r.CategoryName, CategoryId = r.CategoryId,
            ProductName = r.ProductName, Amount = r.AmountMinor / 100m,
            TransactionType = r.TransactionType, PlatformName = r.PlatformName,
            FundAccountName = r.FundAccountName, Status = r.Status, IsManualAdjusted = r.IsManualAdjusted,
            Tags = r.Tags
        };
    }

    public async Task UpdateCategoryAsync(int billId, int categoryId, CancellationToken ct = default)
    {
        var record = await db.BillRecords.FindAsync([billId], ct)
            ?? throw new InvalidOperationException($"账单记录 {billId} 不存在");
        record.CategoryId = categoryId;
        record.IsManualAdjusted = true;
        record.SyncedToDuckDb = false;

        // 手工调整写入 User 来源的溯源记录，优先级高于任何自动规则（设计第 7 节）。
        db.ClassificationResults.Add(new ClassificationResult
        {
            TransactionId = billId,
            Field = ClassificationField.Category,
            Value = categoryId.ToString(),
            Source = ClassificationSource.User,
            Confidence = 1.0,
            CreatedAt = DateTime.Now
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<List<TransactionTagDto>> GetTagsAsync(int billId, CancellationToken ct = default)
    {
        return await db.TransactionTags
            .AsNoTracking()
            .Where(t => t.TransactionId == billId)
            .OrderBy(t => t.Tag.SortOrder)
            .Select(t => new TransactionTagDto(t.TagId, t.Tag.Name, t.Source.ToString(), t.Confidence))
            .ToListAsync(ct);
    }

    public async Task<List<ClassificationResultDto>> GetClassificationsAsync(int billId, CancellationToken ct = default)
    {
        return await db.ClassificationResults
            .AsNoTracking()
            .Where(c => c.TransactionId == billId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ClassificationResultDto(c.Field.ToString(), c.Value, c.Source.ToString(), c.RuleId, c.Confidence, c.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task AddTagAsync(int billId, int tagId, CancellationToken ct = default)
    {
        var exists = await db.TransactionTags.AnyAsync(t => t.TransactionId == billId && t.TagId == tagId, ct);
        if (exists)
        {
            // 已存在（可能来自规则）：手工标记升级为 User 来源，体现最高优先级。
            var link = await db.TransactionTags.FirstAsync(t => t.TransactionId == billId && t.TagId == tagId, ct);
            link.Source = TagSource.User;
            link.Confidence = 1.0;
        }
        else
        {
            db.TransactionTags.Add(new TransactionTag
            {
                TransactionId = billId,
                TagId = tagId,
                Source = TagSource.User,
                Confidence = 1.0,
                CreatedAt = DateTime.Now
            });
        }

        var record = await db.BillRecords.FindAsync([billId], ct);
        if (record is not null) record.SyncedToDuckDb = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveTagAsync(int billId, int tagId, CancellationToken ct = default)
    {
        var link = await db.TransactionTags.FirstOrDefaultAsync(t => t.TransactionId == billId && t.TagId == tagId, ct);
        if (link is null) return;
        db.TransactionTags.Remove(link);
        var record = await db.BillRecords.FindAsync([billId], ct);
        if (record is not null) record.SyncedToDuckDb = false;
        await db.SaveChangesAsync(ct);
    }
}
