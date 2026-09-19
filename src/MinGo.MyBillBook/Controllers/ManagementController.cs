using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Controllers;

[ApiController]
[Route("api")]
public class ManagementController(AppDbContext db) : ControllerBase
{
    // === 支付平台 ===
    [HttpGet("platforms")]
    public async Task<ActionResult> GetPlatforms(CancellationToken ct)
    {
        var platforms = await db.PaymentPlatforms
            .Select(p => new { p.Id, p.Name, p.Code, p.IconUrl, AccountCount = p.FundAccounts.Count })
            .ToListAsync(ct);
        return Ok(platforms);
    }

    // === 资金账户 ===
    [HttpGet("accounts")]
    public async Task<ActionResult> GetAccounts(CancellationToken ct)
    {
        var accounts = await db.FundAccounts
            .Include(a => a.Platform)
            .Select(a => new { a.Id, a.Name, a.PlatformId, PlatformName = a.Platform.Name, a.AccountType, a.Balance, a.IsActive })
            .ToListAsync(ct);
        return Ok(accounts);
    }

    [HttpPost("accounts")]
    public async Task<ActionResult> CreateAccount([FromBody] CreateAccountRequest req, CancellationToken ct)
    {
        var account = new FundAccount { Name = req.Name, PlatformId = req.PlatformId, AccountType = req.AccountType };
        db.FundAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return Created($"/api/accounts/{account.Id}", new { account.Id });
    }

    [HttpPut("accounts/{id:int}")]
    public async Task<ActionResult> UpdateAccount(int id, [FromBody] CreateAccountRequest req, CancellationToken ct)
    {
        var account = await db.FundAccounts.FindAsync([id], ct);
        if (account == null) return NotFound();
        account.Name = req.Name;
        account.PlatformId = req.PlatformId;
        account.AccountType = req.AccountType;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("accounts/{id:int}")]
    public async Task<ActionResult> DeleteAccount(int id, CancellationToken ct)
    {
        var account = await db.FundAccounts.FindAsync([id], ct);
        if (account == null) return NotFound();
        db.FundAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // === 消费分类 ===
    [HttpGet("categories")]
    public async Task<ActionResult> GetCategories(CancellationToken ct)
    {
        var categories = await db.BillCategories
            .OrderBy(c => c.SortOrder)
            .Select(c => new { c.Id, c.Name, c.Icon, c.ParentId, c.SortOrder })
            .ToListAsync(ct);
        return Ok(categories);
    }

    [HttpPost("categories")]
    public async Task<ActionResult> CreateCategory([FromBody] CreateCategoryRequest req, CancellationToken ct)
    {
        var cat = new BillCategory { Name = req.Name, Icon = req.Icon, ParentId = req.ParentId, SortOrder = req.SortOrder };
        db.BillCategories.Add(cat);
        await db.SaveChangesAsync(ct);
        return Created($"/api/categories/{cat.Id}", new { cat.Id });
    }

    [HttpPut("categories/{id:int}")]
    public async Task<ActionResult> UpdateCategory(int id, [FromBody] CreateCategoryRequest req, CancellationToken ct)
    {
        var cat = await db.BillCategories.FindAsync([id], ct);
        if (cat == null) return NotFound();
        cat.Name = req.Name;
        cat.Icon = req.Icon;
        cat.ParentId = req.ParentId;
        cat.SortOrder = req.SortOrder;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // === 分类规则 ===
    [HttpGet("category-rules")]
    public async Task<ActionResult> GetRules(CancellationToken ct)
    {
        var rules = await db.CategoryRules
            .Include(r => r.Category)
            .OrderByDescending(r => r.Priority)
            .Select(r => new { r.Id, r.CategoryId, CategoryName = r.Category.Name, r.MatchField, r.MatchPattern, r.Priority, r.IsActive })
            .ToListAsync(ct);
        return Ok(rules);
    }

    [HttpPost("category-rules")]
    public async Task<ActionResult> CreateRule([FromBody] CreateRuleRequest req, CancellationToken ct)
    {
        var rule = new CategoryRule { CategoryId = req.CategoryId, MatchField = req.MatchField, MatchPattern = req.MatchPattern, Priority = req.Priority };
        db.CategoryRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return Created($"/api/category-rules/{rule.Id}", new { rule.Id });
    }

    [HttpDelete("category-rules/{id:int}")]
    public async Task<ActionResult> DeleteRule(int id, CancellationToken ct)
    {
        var rule = await db.CategoryRules.FindAsync([id], ct);
        if (rule == null) return NotFound();
        db.CategoryRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record CreateAccountRequest(string Name, int PlatformId, AccountType AccountType);
public record CreateCategoryRequest(string Name, string Icon, int? ParentId, int SortOrder);
public record CreateRuleRequest(int CategoryId, MatchField MatchField, string MatchPattern, int Priority);
