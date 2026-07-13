using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Models;

namespace Tms.Api.Controllers;

public record CreateCategoryRequest(string Name, Guid? ParentCategoryId);

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public CategoriesController(TmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Category>>> GetCategories(CancellationToken ct)
    {
        var categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync(ct);
        return Ok(categories);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<Category>> CreateCategory([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            ParentCategoryId = request.ParentCategoryId,
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetCategories), category);
    }
}
