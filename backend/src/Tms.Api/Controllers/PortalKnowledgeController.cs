using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Knowledge;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 6 - Knowledge Base, customer-facing half. Gated behind the
// PortalCustomer policy (not a public/unauthenticated endpoint) - a portal
// customer is already logged in by the time they reach the "new ticket"
// form this powers, so there's no separate anonymous-browsing surface to
// build here. Only ever returns IsPublic articles - internal-only articles
// are invisible to this controller by construction, not just by UI choice.
[ApiController]
[Route("api/portal/knowledge-articles")]
[Authorize(Policy = "PortalCustomer")]
public class PortalKnowledgeController : ControllerBase
{
    private readonly TmsDbContext _db;

    public PortalKnowledgeController(TmsDbContext db)
    {
        _db = db;
    }

    // Powers the "suggested articles" deflection the spec's done-when bar
    // asks for: called as the customer types a ticket subject. With no
    // query, falls back to the tenant's most-viewed public articles so the
    // panel isn't just empty before the customer starts typing.
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PortalArticleSummary>>> Search([FromQuery] string? query, CancellationToken ct)
    {
        var publicArticles = await _db.KnowledgeArticles.Where(a => a.IsPublic).ToListAsync(ct);

        var results = string.IsNullOrWhiteSpace(query)
            ? publicArticles.OrderByDescending(a => a.ViewCount).Take(5)
            : KnowledgeSuggestionMatcher.Rank(publicArticles, query, take: 5);

        return Ok(results.Select(a => new PortalArticleSummary(a.Id, a.Title, KnowledgeSuggestionMatcher.Snippet(a.Body))));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PortalArticleDetail>> GetArticle(Guid id, CancellationToken ct)
    {
        var article = await _db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id && a.IsPublic, ct);
        if (article is null) return NotFound();

        article.ViewCount++;
        await _db.SaveChangesAsync(ct);

        return Ok(new PortalArticleDetail(article.Id, article.Title, article.Body, article.HelpfulYesCount, article.HelpfulNoCount));
    }

    [HttpPost("{id:guid}/feedback")]
    public async Task<IActionResult> SubmitFeedback(Guid id, [FromBody] PortalFeedbackRequest request, CancellationToken ct)
    {
        var article = await _db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id && a.IsPublic, ct);
        if (article is null) return NotFound();

        if (request.Helpful) article.HelpfulYesCount++;
        else article.HelpfulNoCount++;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
