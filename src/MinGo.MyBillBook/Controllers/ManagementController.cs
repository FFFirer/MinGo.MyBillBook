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
        await EnqueueRebuildAsync("ClassifyCategory", ct);
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
        await EnqueueRebuildAsync("ClassifyCategory", ct);
        return NoContent();
    }

    [HttpDelete("category-rules/{id:int}")]
    public async Task<ActionResult> DeleteRule(int id, CancellationToken ct)
    {
        var rule = await db.CategoryRules.FindAsync([id], ct);
        if (rule == null) return NotFound();
        db.CategoryRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        await EnqueueRebuildAsync("ClassifyCategory", ct);
        return NoContent();
    }

    // === 商户 ===
    [HttpGet("merchants")]
    public async Task<ActionResult> GetMerchants(CancellationToken ct)
    {
        var merchants = await db.Merchants
            .OrderBy(m => m.CanonicalName)
            .Select(m => new { m.Id, m.CanonicalName, m.CreatedAt, m.DefaultCategoryId, DefaultCategoryName = m.DefaultCategory != null ? m.DefaultCategory.Name : null, AliasCount = m.Aliases.Count, BillCount = m.BillRecords.Count })
            .ToListAsync(ct);
        return Ok(merchants);
    }

    [HttpPost("merchants")]
    public async Task<ActionResult> CreateMerchant([FromBody] CreateMerchantRequest req, CancellationToken ct)
    {
        var merchant = new Merchant { CanonicalName = req.CanonicalName.Trim(), CreatedAt = DateTime.Now, DefaultCategoryId = req.DefaultCategoryId };
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(ct);
        return Created($"/api/merchants/{merchant.Id}", new { merchant.Id });
    }

    [HttpPut("merchants/{id:int}")]
    public async Task<ActionResult> UpdateMerchant(int id, [FromBody] CreateMerchantRequest req, CancellationToken ct)
    {
        var merchant = await db.Merchants.FindAsync([id], ct);
        if (merchant == null) return NotFound();
        merchant.CanonicalName = req.CanonicalName.Trim();
        merchant.DefaultCategoryId = req.DefaultCategoryId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("merchants/{id:int}")]
    public async Task<ActionResult> DeleteMerchant(int id, CancellationToken ct)
    {
        var merchant = await db.Merchants.Include(m => m.Aliases).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (merchant == null) return NotFound();
        var hasBills = await db.BillRecords.AnyAsync(b => b.MerchantId == id, ct);
        if (hasBills)
        {
            // 解除关联而非拒绝删除：账单保留原始 Merchant 快照，仅置空归一化 FK。
            var bills = await db.BillRecords.Where(b => b.MerchantId == id).ToListAsync(ct);
            foreach (var b in bills) { b.MerchantId = null; b.SyncedToDuckDb = false; }
        }
        db.MerchantAliases.RemoveRange(merchant.Aliases);
        db.Merchants.Remove(merchant);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // === 商户别名 ===
    [HttpGet("merchant-aliases")]
    public async Task<ActionResult> GetMerchantAliases(CancellationToken ct)
    {
        var aliases = await db.MerchantAliases
            .Include(a => a.Merchant)
            .OrderByDescending(a => a.Priority)
            .Select(a => new { a.Id, a.Pattern, a.MatchType, a.MerchantId, MerchantName = a.Merchant.CanonicalName, a.Priority, a.IsActive })
            .ToListAsync(ct);
        return Ok(aliases);
    }

    [HttpPost("merchant-aliases")]
    public async Task<ActionResult> CreateMerchantAlias([FromBody] CreateMerchantAliasRequest req, CancellationToken ct)
    {
        var alias = new MerchantAlias { Pattern = req.Pattern, MatchType = req.MatchType, MerchantId = req.MerchantId, Priority = req.Priority, IsActive = req.IsActive };
        db.MerchantAliases.Add(alias);
        await db.SaveChangesAsync(ct);
        await EnqueueRebuildAsync("ResolveMerchant", ct);
        return Created($"/api/merchant-aliases/{alias.Id}", new { alias.Id });
    }

    [HttpPut("merchant-aliases/{id:int}")]
    public async Task<ActionResult> UpdateMerchantAlias(int id, [FromBody] CreateMerchantAliasRequest req, CancellationToken ct)
    {
        var alias = await db.MerchantAliases.FindAsync([id], ct);
        if (alias == null) return NotFound();
        alias.Pattern = req.Pattern;
        alias.MatchType = req.MatchType;
        alias.MerchantId = req.MerchantId;
        alias.Priority = req.Priority;
        alias.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
        await EnqueueRebuildAsync("ResolveMerchant", ct);
        return NoContent();
    }

    [HttpDelete("merchant-aliases/{id:int}")]
    public async Task<ActionResult> DeleteMerchantAlias(int id, CancellationToken ct)
    {
        var alias = await db.MerchantAliases.FindAsync([id], ct);
        if (alias == null) return NotFound();
        db.MerchantAliases.Remove(alias);
        await db.SaveChangesAsync(ct);
        await EnqueueRebuildAsync("ResolveMerchant", ct);
        return NoContent();
    }

    // === 标签 ===
    [HttpGet("tags")]
    public async Task<ActionResult> GetTags(CancellationToken ct)
    {
        var tags = await db.Tags
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, TagType = t.TagType.ToString(), Scope = t.Scope.ToString(), t.ScopeId, t.SortOrder, TaggedCount = t.TransactionTags.Count })
            .ToListAsync(ct);
        return Ok(tags);
    }

    [HttpPost("tags")]
    public async Task<ActionResult> CreateTag([FromBody] CreateTagRequest req, CancellationToken ct)
    {
        var tag = new Tag { Name = req.Name.Trim(), TagType = req.TagType, Scope = req.Scope, ScopeId = req.ScopeId, SortOrder = req.SortOrder };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);
        return Created($"/api/tags/{tag.Id}", new { tag.Id });
    }

    [HttpPut("tags/{id:int}")]
    public async Task<ActionResult> UpdateTag(int id, [FromBody] CreateTagRequest req, CancellationToken ct)
    {
        var tag = await db.Tags.FindAsync([id], ct);
        if (tag == null) return NotFound();
        tag.Name = req.Name.Trim();
        tag.TagType = req.TagType;
        tag.Scope = req.Scope;
        tag.ScopeId = req.ScopeId;
        tag.SortOrder = req.SortOrder;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("tags/{id:int}")]
    public async Task<ActionResult> DeleteTag(int id, CancellationToken ct)
    {
        var tag = await db.Tags.Include(t => t.Rules).Include(t => t.CategoryTags).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag == null) return NotFound();
        // TransactionTag 与 TagRule/CategoryTag 均配置为级联删除。
        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // === 标签规则 ===
    [HttpGet("tag-rules")]
    public async Task<ActionResult> GetTagRules(CancellationToken ct)
    {
        var rules = await db.TagRules
            .Include(r => r.Tag)
            .OrderByDescending(r => r.Priority)
            .Select(r => new { r.Id, Condition = r.Condition.ToString(), r.ConditionValue, r.TagId, TagName = r.Tag.Name, r.Priority, r.IsActive })
            .ToListAsync(ct);
        return Ok(rules);
    }

    [HttpPost("tag-rules")]
    public async Task<ActionResult> CreateTagRule([FromBody] CreateTagRuleRequest req, CancellationToken ct)
    {
        var rule = new TagRule { Condition = req.Condition, ConditionValue = req.ConditionValue, TagId = req.TagId, Priority = req.Priority, IsActive = req.IsActive };
        db.TagRules.Add(rule);
        await db.SaveChangesAsync(ct);
        await EnqueueRebuildAsync("ApplyTags", ct);
        return Created($"/api/tag-rules/{rule.Id}", new { rule.Id });
    }

    [HttpPut("tag-rules/{id:int}")]
    public async Task<ActionResult> UpdateTagRule(int id, [FromBody] CreateTagRuleRequest req, CancellationToken ct)
    {
        var rule = await db.TagRules.FindAsync([id], ct);
        if (rule == null) return NotFound();
        rule.Condition = req.Condition;
        rule.ConditionValue = req.ConditionValue;
        rule.TagId = req.TagId;
        rule.Priority = req.Priority;
        rule.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
        await EnqueueRebuildAsync("ApplyTags", ct);
        return NoContent();
    }

    [HttpDelete("tag-rules/{id:int}")]
    public async Task<ActionResult> DeleteTagRule(int id, CancellationToken ct)
    {
        var rule = await db.TagRules.FindAsync([id], ct);
        if (rule == null) return NotFound();
        db.TagRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        await EnqueueRebuildAsync("ApplyTags", ct);
        return NoContent();
    }

    /// <summary>
    /// 规则变更后自动入队对应局部重建（设计第 20 节）：Merchant 别名→ResolveMerchant，
    /// Category/Tag 规则→ClassifyCategory（含重打标签）。Worker 异步执行，接口立即返回。
    /// </summary>
    private async Task EnqueueRebuildAsync(string from, CancellationToken ct)
    {
        db.PipelineJobs.Add(new PipelineJob
        {
            Type = PipelineJobType.Rebuild,
            Payload = System.Text.Json.JsonSerializer.Serialize(new { from, batchId = (int?)null }),
            Status = PipelineJobStatus.Pending,
            CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync(ct);
    }
}

public record CreatePlatformRequest(string Name, string Code, string? IconUrl);
public record CreateAccountRequest(string Name, int PlatformId, AccountType AccountType, decimal Balance = 0);
public record CreateCategoryRequest(string Name, string Icon, int? ParentId, int SortOrder);
public record CreateRuleRequest(int CategoryId, MatchField MatchField, string MatchPattern, int Priority);
public record CreateMerchantRequest(string CanonicalName, int? DefaultCategoryId = null);
public record CreateMerchantAliasRequest(string Pattern, AliasMatchType MatchType, int MerchantId, int Priority, bool IsActive = true);
public record CreateTagRequest(string Name, TagType TagType, TagScope Scope, int? ScopeId, int SortOrder);
public record CreateTagRuleRequest(TagRuleCondition Condition, string ConditionValue, int TagId, int Priority, bool IsActive = true);
