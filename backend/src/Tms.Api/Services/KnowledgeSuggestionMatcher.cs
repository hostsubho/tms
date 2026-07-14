using Tms.Api.Models;

namespace Tms.Api.Services;

// Module 6 - Knowledge Base. Deliberately simple keyword scoring, not a real
// search index (Postgres full-text search, Elasticsearch, etc.) - see the
// scoping note on KnowledgeArticle. Callers load candidate articles into
// memory first (there's no DB-side ranking here), same tradeoff already made
// for Module 9's report aggregates.
public static class KnowledgeSuggestionMatcher
{
    // Short, common words are excluded so a subject like "please help with my
    // login issue" scores articles on "login"/"issue", not "please"/"with".
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "was", "were", "to", "for", "of", "in", "on", "and", "or",
        "my", "i", "it", "its", "this", "that", "with", "not", "can", "cant", "please", "help",
        "need", "how", "why", "what", "when", "does", "do",
    };

    public static IEnumerable<KnowledgeArticle> Rank(IEnumerable<KnowledgeArticle> articles, string query, int take)
    {
        var words = Tokenize(query);
        if (words.Count == 0) return Enumerable.Empty<KnowledgeArticle>();

        return articles
            .Select(a => (Article: a, Score: ScoreArticle(a, words)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Article.ViewCount)
            .Take(take)
            .Select(x => x.Article);
    }

    private static List<string> Tokenize(string text) =>
        text.Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .Distinct()
            .ToList();

    // Title matches count for more than body matches - a keyword in the
    // title is a much stronger relevance signal than the same word buried
    // somewhere in a long article body.
    private static int ScoreArticle(KnowledgeArticle a, List<string> words)
    {
        var titleLower = a.Title.ToLowerInvariant();
        var bodyLower = a.Body.ToLowerInvariant();
        var score = 0;
        foreach (var w in words)
        {
            if (titleLower.Contains(w, StringComparison.Ordinal)) score += 3;
            if (bodyLower.Contains(w, StringComparison.Ordinal)) score += 1;
        }
        return score;
    }

    public static string Snippet(string body, int maxLength = 160)
    {
        var normalized = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd() + "…";
    }
}
