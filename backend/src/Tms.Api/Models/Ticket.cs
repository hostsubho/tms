namespace Tms.Api.Models;

public enum TicketStatus { New, Open, Pending, Resolved, Closed }
public enum TicketPriority { Low, Medium, High, Urgent }

public class Ticket
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.New;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public Guid? CategoryId { get; set; }
    public Guid? RequesterId { get; set; }
    public Guid? AssigneeId { get; set; }
    public Guid? SlaPolicyId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Module 4 - SLA Management. DueAt is the resolution target; ResponseDueAt
    // the (earlier) first-response target. Both are computed once at creation
    // time from the matched SlaPolicy and never recalculated afterwards, even
    // if priority changes later - re-deriving them on every priority change
    // would make the SLA a moving target instead of a commitment made at intake.
    public DateTime? DueAt { get; set; }
    public DateTime? ResponseDueAt { get; set; }

    // Set the first time any comment is added to the ticket - our proxy for
    // "an agent responded" since comments don't yet distinguish agent vs
    // requester authorship beyond the IsInternal flag.
    public DateTime? FirstRespondedAt { get; set; }

    // Flipped once by SlaEvaluator the first time a breach is detected
    // (lazily, on read) - prevents re-escalating priority over and over on
    // every subsequent view of an already-escalated ticket.
    public bool Escalated { get; set; }
}
