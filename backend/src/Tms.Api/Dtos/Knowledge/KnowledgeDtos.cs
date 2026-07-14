using Tms.Api.Models;

namespace Tms.Api.Dtos.Knowledge;

public record CreateArticleRequest(string Title, string Body, bool IsPublic, Guid? CategoryId);

public record UpdateArticleRequest(string? Title, string? Body, bool? IsPublic, Guid? CategoryId);

public record ArticleResponse(
    Guid Id,
    string Title,
    string Body,
    bool IsPublic,
    Guid? CategoryId,
    int ViewCount,
    int HelpfulYesCount,
    int HelpfulNoCount,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static ArticleResponse FromEntity(KnowledgeArticle a) => new(
        a.Id, a.Title, a.Body, a.IsPublic, a.CategoryId,
        a.ViewCount, a.HelpfulYesCount, a.HelpfulNoCount, a.CreatedAt, a.UpdatedAt);
}

public record ArticleVersionResponse(Guid Id, string Title, string Body, DateTime EditedAt)
{
    public static ArticleVersionResponse FromEntity(KnowledgeArticleVersion v) => new(v.Id, v.Title, v.Body, v.EditedAt);
}

// Portal-facing shapes are deliberately narrower - a suggestion/search
// result is a title + short snippet (not the full body, which could be
// long), and the detail view omits internal fields like CreatedByUserId.
public record PortalArticleSummary(Guid Id, string Title, string Snippet);

public record PortalArticleDetail(Guid Id, string Title, string Body, int HelpfulYesCount, int HelpfulNoCount);

public record PortalFeedbackRequest(bool Helpful);
