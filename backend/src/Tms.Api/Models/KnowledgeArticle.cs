namespace Tms.Api.Models;

// Module 6 - Knowledge Base / Self-Service Portal. Scoped down from the full
// spec (see docs/tms_spec.md Module 6):
//   - "Versioning" is a plain history list (KnowledgeArticleVersion snapshots
//     taken of the *previous* state right before each edit is applied) - no
//     diff view or rollback-to-version action, just "what did this look
//     like before."
//   - "Suggested articles" deflection is keyword-matched in memory against
//     public articles (see KnowledgeArticlesController/PortalKnowledgeController),
//     not a real search index (Postgres full-text search, Elasticsearch,
//     etc.) - fine at the article counts a single tenant would realistically
//     author, same "load into memory, compute in C#" tradeoff already made
//     for Module 9's reports.
//   - Feedback ("was this helpful") is an anonymous increment, not tracked
//     per-customer - a customer could vote more than once. Deduplicating
//     would need a join table keyed on (ArticleId, CustomerId) that isn't
//     worth the complexity for a same-tenant helpfulness signal at this
//     stage.
public class KnowledgeArticle
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    // Public articles are visible through the customer portal (search,
    // suggestions, direct view); internal-only articles never leave the
    // staff-facing controller, regardless of how someone reaches for them.
    public bool IsPublic { get; set; } = true;

    public Guid? CategoryId { get; set; }
    public Guid CreatedByUserId { get; set; }

    public int ViewCount { get; set; }
    public int HelpfulYesCount { get; set; }
    public int HelpfulNoCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// A snapshot of an article's Title/Body immediately before an edit
// overwrote them - lets a tenant admin see what an article used to say
// without needing a full version-diff/rollback UI.
public class KnowledgeArticleVersion
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ArticleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Guid EditedByUserId { get; set; }
    public DateTime EditedAt { get; set; } = DateTime.UtcNow;
}
