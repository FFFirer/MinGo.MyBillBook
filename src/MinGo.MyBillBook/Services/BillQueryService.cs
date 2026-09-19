using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Interfaces;
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
        if (filter.MinAmount.HasValue) query = query.Where(r => r.Amount >= filter.MinAmount.Value);
        if (filter.MaxAmount.HasValue) query = query.Where(r => r.Amount <= filter.MaxAmount.Value);
        if (filter.TransactionType.HasValue) query = query.Where(r => r.TransactionType == filter.TransactionType);
        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword;
            query = query.Where(r => r.Counterparty.Contains(kw) || r.Merchant.Contains(kw) || r.ProductName.Contains(kw));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.TransactionDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(r => new BillDto
            {
                Id = r.Id, TransactionDate = r.TransactionDate, Counterparty = r.Counterparty,
                Merchant = r.Merchant, CategoryName = r.Category != null ? r.Category.Name : null,
                CategoryId = r.CategoryId, ProductName = r.ProductName, Amount = r.Amount,
                TransactionType = r.TransactionType.ToString(), PlatformName = r.Platform.Name,
                FundAccountName = r.FundAccount != null ? r.FundAccount.Name : null,
                Status = r.Status, IsManualAdjusted = r.IsManualAdjusted
            })
            .ToListAsync(ct);

        return new PagedResult<BillDto>(items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<BillDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await db.BillRecords.Include(r => r.Platform).Include(r => r.Category).Include(r => r.FundAccount)
            .Where(r => r.Id == id)
            .Select(r => new BillDto
            {
                Id = r.Id, TransactionDate = r.TransactionDate, Counterparty = r.Counterparty,
                Merchant = r.Merchant, CategoryName = r.Category != null ? r.Category.Name : null,
                CategoryId = r.CategoryId, ProductName = r.ProductName, Amount = r.Amount,
                TransactionType = r.TransactionType.ToString(), PlatformName = r.Platform.Name,
                FundAccountName = r.FundAccount != null ? r.FundAccount.Name : null,
                Status = r.Status, IsManualAdjusted = r.IsManualAdjusted
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task UpdateCategoryAsync(int billId, int categoryId, CancellationToken ct = default)
    {
        var record = await db.BillRecords.FindAsync([billId], ct)
            ?? throw new InvalidOperationException($"账单记录 {billId} 不存在");
        record.CategoryId = categoryId;
        record.IsManualAdjusted = true;
        record.SyncedToDuckDb = false;
        await db.SaveChangesAsync(ct);
    }
}
