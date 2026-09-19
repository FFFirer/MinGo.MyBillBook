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

    [HttpPost("platforms")]
    public async Task<ActionResult> CreatePlatform([FromBody] CreatePlatformRequest req, CancellationToken ct)
    {
        var platform = new PaymentPlatform { Name = req.Name, Code = req.Code, IconUrl = req.IconUrl };
        db.PaymentPlatforms.Add(platform);
        await db.SaveChangesAsync(ct);
        return Created($"/api/platforms/{platform.Id}", new { platform.Id });
    }

    [HttpPut("platforms/{id:int}")]
    public async Task<ActionResult> UpdatePlatform(int id, [FromBody] CreatePlatformRequest req, CancellationToken ct)
    {
        var platform = await db.PaymentPlatforms.FindAsync([id], ct);
        if (platform == null) return NotFound();
        platform.Name = req.Name;
        platform.Code = req.Code;
        platform.IconUrl = req.IconUrl;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("platforms/{id:int}")]
    public async Task<ActionResult> DeletePlatform(int id, CancellationToken ct)
    {
        var platform = await db.PaymentPlatforms
            .Include(p => p.FundAccounts)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (platform == null) return NotFound();
        if (platform.FundAccounts.Count > 0)
            return BadRequest(new { message = "该平台下还有资金账户，无法删除" });
        db.PaymentPlatforms.Remove(platform);
        await db.SaveChangesAsync(ct);
        return NoContent();
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
        var account = new FundAccount { Name = req.Name, PlatformId = req.PlatformId, AccountType = req.AccountType, Balance = req.Balance };
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
        account.Balance = req.Balance;
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

    [HttpDelete("categories/{id:int}")]
    public async Task<ActionResult> DeleteCategory(int id, CancellationToken ct)
    {
        var cat = await db.BillCategories
            .Include(c => c.Children)
            .Include(c => c.Rules)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cat == null) return NotFound();
        if (cat.Children.Count > 0)
            return BadRequest(new { message = "该分类下有子分类，无法删除" });
        if (cat.Rules.Count > 0)
            return BadRequest(new { message = "该分类下有关联规则，请先删除相关规则" });
        var hasBills = await db.BillRecords.AnyAsync(b => b.CategoryId == id, ct);
        if (hasBills)
            return BadRequest(new { message = "该分类下有关联账单，无法删除" });
        db.BillCategories.Remove(cat);
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

    [HttpPut("category-rules/{id:int}")]
    public async Task<ActionResult> UpdateRule(int id, [FromBody] CreateRuleRequest req, CancellationToken ct)
    {
        var rule = await db.CategoryRules.FindAsync([id], ct);
        if (rule == null) return NotFound();
        rule.CategoryId = req.CategoryId;
        rule.MatchField = req.MatchField;
        rule.MatchPattern = req.MatchPattern;
        rule.Priority = req.Priority;
        await db.SaveChangesAsync(ct);
        return NoContent();
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

public record CreatePlatformRequest(string Name, string Code, string? IconUrl);
public record CreateAccountRequest(string Name, int PlatformId, AccountType AccountType, decimal Balance = 0);
public record CreateCategoryRequest(string Name, string Icon, int? ParentId, int SortOrder);
public record CreateRuleRequest(int CategoryId, MatchField MatchField, string MatchPattern, int Priority);
