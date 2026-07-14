using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Dtos.Tickets;

// Client-facing DTOs so nobody can set TenantId, Id, or CreatedAt from the
// request body - those are always assigned server-side from ITenantContext.
// SlaPolicyId is likewise never accepted from the client (Module 4 - SLA
// Management): it's always derived server-side from the tenant's policies
// matched against Priority, so a caller can't hand-pick a more lenient SLA.
public record CreateTicketRequest(
    string Subject,
    string? Description,
    TicketPriority Priority,
    Guid? CategoryId,
    Guid? AssigneeId);

public record UpdateTicketRequest(
    string? Subject,
    string? Description,
    TicketStatus? Status,
    TicketPriority? Priority,
    Guid? CategoryId,
    Guid? AssigneeId);

public record TicketResponse(
    Guid Id,
    string Subject,
    string? Description,
    TicketStatus Status,
    TicketPriority Priority,
    Guid? CategoryId,
    Guid? RequesterId,
    Guid? AssigneeId,
    Guid? SlaPolicyId,
    Guid? CustomerId,
    DateTime CreatedAt,
    DateTime? DueAt,
    DateTime? ResponseDueAt,
    DateTime? FirstRespondedAt,
    bool Escalated,
    bool IsResolutionBreached,
    bool IsResponseBreached,
    int? CsatRating,
    DateTime? CsatSubmittedAt,
    DateTime? ResolvedAt)
{
    // utcNow threaded through explicitly (rather than each call re-reading
    // DateTime.UtcNow) so a whole ticket list evaluates breach status against
    // one consistent point in time instead of drifting mid-request.
    public static TicketResponse FromEntity(Ticket t, DateTime utcNow) => new(
        t.Id, t.Subject, t.Description, t.Status, t.Priority,
        t.CategoryId, t.RequesterId, t.AssigneeId, t.SlaPolicyId, t.CustomerId, t.CreatedAt, t.DueAt,
        t.ResponseDueAt, t.FirstRespondedAt, t.Escalated,
        SlaEvaluator.IsResolutionBreached(t, utcNow),
        SlaEvaluator.IsResponseBreached(t, utcNow),
        t.CsatRating, t.CsatSubmittedAt, t.ResolvedAt);
}

public record AddCommentRequest(string Body, bool IsInternal);

public record CommentResponse(Guid Id, Guid TicketId, Guid AuthorId, string Body, bool IsInternal, bool IsFromCustomer, DateTime CreatedAt)
{
    public static CommentResponse FromEntity(TicketComment c) => new(
        c.Id, c.TicketId, c.AuthorId, c.Body, c.IsInternal, c.IsFromCustomer, c.CreatedAt);
}
