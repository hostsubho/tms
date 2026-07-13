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
    public DateTime? DueAt { get; set; }
}
