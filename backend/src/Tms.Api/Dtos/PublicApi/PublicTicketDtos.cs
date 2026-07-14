using Tms.Api.Models;

namespace Tms.Api.Dtos.PublicApi;

public record CreatePublicTicketRequest(
    string Subject,
    string? Description,
    TicketPriority Priority = TicketPriority.Medium,
    Guid? CategoryId = null,
    Guid? AssigneeId = null);

public record UpdatePublicTicketRequest(
    TicketStatus? Status,
    TicketPriority? Priority,
    Guid? AssigneeId);

public record PublicTicketResponse(
    Guid Id,
    string Subject,
    string? Description,
    TicketStatus Status,
    TicketPriority Priority,
    Guid? CategoryId,
    Guid? AssigneeId,
    DateTime CreatedAt,
    DateTime? DueAt,
    DateTime? ResponseDueAt,
    DateTime? ResolvedAt)
{
    public static PublicTicketResponse FromEntity(Ticket t) => new(
        t.Id, t.Subject, t.Description, t.Status, t.Priority, t.CategoryId,
        t.AssigneeId, t.CreatedAt, t.DueAt, t.ResponseDueAt, t.ResolvedAt);
}
