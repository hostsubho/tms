namespace Tms.Api.Models;

public class TicketComment
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;

    // Internal notes are visible to agents only; public replies are visible
    // to the requester through the customer portal (Module 7).
    public bool IsInternal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
