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
}

public record UpdateCategoryRequest(int CategoryId);
