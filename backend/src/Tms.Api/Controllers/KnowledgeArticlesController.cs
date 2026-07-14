using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Knowledge;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 6 - Knowledge Base. Read (including internal-only articles) is
// open to any authenticated tenant staff member; write is restricted to
// Admin/Manager, same pattern as SlaPoliciesController/AutomationRulesController.
[ApiController]
[Route("api/knowledge-articles")]
[Authorize(Policy = "TenantStaff")]
public class KnowledgeArticlesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService _auditLog;
    private readonly IModuleAccessService _moduleAccess;

    public KnowledgeArticlesController(TmsDbContext db, ITenantContext tenantContext, IAuditLogService auditLog, IModuleAccessService moduleAccess)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditLog = auditLog;
        _moduleAccess = moduleAccess;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ArticleResponse>>> GetArticles(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.KnowledgeBase, ct)) return ModuleDisabled();

        var articles = await _db.KnowledgeArticles.OrderByDescending(a => a.UpdatedAt).ToListAsync(ct);
        return Ok(articles.Select(ArticleResponse.FromEntity));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ArticleResponse>> GetArticle(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.KnowledgeBase, ct)) return ModuleDisabled();

        var article = await _db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return NotFound();
        return Ok(ArticleResponse.FromEntity(article));
    }

    [HttpPost]
    [Authorize(Policy = "Permission:ManageKnowledgeArticles")]
    public async Task<ActionResult<ArticleResponse>> CreateArticle([FromBody] CreateArticleRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.KnowledgeBase, ct)) return ModuleDisabled();

        var utcNow = DateTime.UtcNow;
        var article = new KnowledgeArticle
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = request.Title,
            Body = request.Body,
            IsPublic = request.IsPublic,
            CategoryId = request.CategoryId,
            CreatedByUserId = User.GetUserId(),
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };

        _db.KnowledgeArticles.Add(article);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Created,
            AuditEntityType.KnowledgeArticle, article.Id, $"Created knowledge article '{article.Title}'.");

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetArticle), new { id = article.Id }, ArticleResponse.FromEntity(article));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "Permission:ManageKnowledgeArticles")]
    public async Task<ActionResult<ArticleResponse>> UpdateArticle(Guid id, [FromBody] UpdateArticleRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.KnowledgeBase, ct)) return ModuleDisabled();

        var article = await _db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return NotFound();

        // Snapshot the pre-edit state before anything changes - this is the
        // "versioning" the spec asks for: a plain history of what the
        // article used to say, not a diff/rollback UI.
        if (request.Title is not null || request.Body is not null)
        {
            _db.KnowledgeArticleVersions.Add(new KnowledgeArticleVersion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ArticleId = article.Id,
                Title = article.Title,
                Body = article.Body,
                EditedByUserId = User.GetUserId(),
                EditedAt = DateTime.UtcNow,
            });
        }

        if (request.Title is not null) article.Title = request.Title;
        if (request.Body is not null) article.Body = request.Body;
        if (request.IsPublic is not null) article.IsPublic = request.IsPublic.Value;
        if (request.CategoryId is not null) article.CategoryId = request.CategoryId;
        article.UpdatedAt = DateTime.UtcNow;

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Updated,
            AuditEntityType.KnowledgeArticle, article.Id, $"Updated knowledge article '{article.Title}'.");

        await _db.SaveChangesAsync(ct);
        return Ok(ArticleResponse.FromEntity(article));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Permission:ManageKnowledgeArticles")]
    public async Task<IActionResult> DeleteArticle(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.KnowledgeBase, ct)) return ModuleDisabled();

        var article = await _db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return NotFound();

        // Version history for this article is left in place with a now-
        // dangling ArticleId, same convention as AutomationRuleLog keeping
        // its RuleId after the rule that produced it is deleted - the
        // history of what existed is a separate concern from whether the
        // article itself still does.
        _db.KnowledgeArticles.Remove(article);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Deleted,
            AuditEntityType.KnowledgeArticle, article.Id, $"Deleted knowledge article '{article.Title}'.");

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<IEnumerable<ArticleVersionResponse>>> GetVersions(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");
        if (!await _moduleAccess.IsEnabledAsync(tenantId, ModuleKey.KnowledgeBase, ct)) return ModuleDisabled();

        var versions = await _db.KnowledgeArticleVersions
            .Where(v => v.ArticleId == id)
            .OrderByDescending(v => v.EditedAt)
            .ToListAsync(ct);

        return Ok(versions.Select(ArticleVersionResponse.FromEntity));
    }

    private ObjectResult ModuleDisabled() =>
        StatusCode(StatusCodes.Status403Forbidden,
            new { message = "Knowledge Base isn't enabled for this workspace - contact WMX to turn it on." });
}
