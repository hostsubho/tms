using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

public record CreateCategoryRequest(string Name, Guid? ParentCategoryId);

[ApiController]
[Route("api/categories")]
[Authorize(Policy = "TenantStaff")]
public class CategoriesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService _auditLog;

    public CategoriesController(TmsDbContext db, ITenantContext tenantContext, IAuditLogService auditLog)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditLog = auditLog;
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

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Created,
            AuditEntityType.Category, category.Id, $"Created category '{category.Name}'.");

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetCategories), category);
    }
}
