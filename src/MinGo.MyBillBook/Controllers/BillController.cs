using Microsoft.AspNetCore.Mvc;
using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Interfaces;

namespace MinGo.MyBillBook.Controllers;

[ApiController]
[Route("api/bills")]
public class BillController(IBillQueryService queryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<BillDto>>> GetBills(
        [FromQuery] BillQueryFilter filter, CancellationToken ct = default)
    {
        var result = await queryService.QueryAsync(filter, ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BillDto>> GetById(int id, CancellationToken ct = default)
    {
        var bill = await queryService.GetByIdAsync(id, ct);
        if (bill == null) return NotFound();
        return Ok(bill);
    }

    [HttpPut("{id:int}/category")]
    public async Task<ActionResult> UpdateCategory(int id, [FromBody] UpdateCategoryRequest request, CancellationToken ct = default)
    {
        await queryService.UpdateCategoryAsync(id, request.CategoryId, ct);
        return NoContent();
    }

    /// <summary>获取账单的全部标签（含来源溯源）。</summary>
    [HttpGet("{id:int}/tags")]
    public async Task<ActionResult<List<TransactionTagDto>>> GetTags(int id, CancellationToken ct = default)
    {
        var tags = await queryService.GetTagsAsync(id, ct);
        return Ok(tags);
    }

    /// <summary>为账单手工打标（Source=User，优先级最高，不被自动规则覆盖）。</summary>
    [HttpPost("{id:int}/tags")]
    public async Task<ActionResult> AddTag(int id, [FromBody] AddTagRequest request, CancellationToken ct = default)
    {
        await queryService.AddTagAsync(id, request.TagId, ct);
        return NoContent();
    }

    /// <summary>移除账单上的某标签。</summary>
    [HttpDelete("{id:int}/tags/{tagId:int}")]
    public async Task<ActionResult> RemoveTag(int id, int tagId, CancellationToken ct = default)
    {
        await queryService.RemoveTagAsync(id, tagId, ct);
        return NoContent();
    }

    /// <summary>获取账单的分类/商户决策溯源（设计第 8 节优先级链）。</summary>
    [HttpGet("{id:int}/classifications")]
    public async Task<ActionResult<List<ClassificationResultDto>>> GetClassifications(int id, CancellationToken ct = default)
    {
        var rows = await queryService.GetClassificationsAsync(id, ct);
        return Ok(rows);
    }
}

public record UpdateCategoryRequest(int CategoryId);
public record AddTagRequest(int TagId);
