using Tms.Api.Models;

namespace Tms.Api.Dtos.Tickets;

// Client-facing DTOs so nobody can set TenantId, Id, or CreatedAt from the
// request body - those are always assigned server-side from ITenantContext.
public record CreateTicketRequest(
    string Subject,
    string? Description,
    TicketPriority Priority,
    Guid? CategoryId,
    Guid? AssigneeId,
    Guid? SlaPolicyId);

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
    DateTime CreatedAt,
    DateTime? DueAt)
{
    public static TicketResponse FromEntity(Ticket t) => new(
        t.Id, t.Subject, t.Description, t.Status, t.Priority,
        t.CategoryId, t.RequesterId, t.AssigneeId, t.SlaPolicyId, t.CreatedAt, t.DueAt);
}

public record AddCommentRequest(string Body, bool IsInternal);

public record CommentResponse(Guid Id, Guid TicketId, Guid AuthorId, string Body, bool IsInternal, DateTime CreatedAt)
{
    public static CommentResponse FromEntity(TicketComment c) => new(
        c.Id, c.TicketId, c.AuthorId, c.Body, c.IsInternal, c.CreatedAt);
}
