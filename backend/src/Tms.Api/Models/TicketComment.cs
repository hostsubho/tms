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

    // True when AuthorId refers to a PortalCustomer rather than an AppUser
    // (agent). Comments and AppUsers/PortalCustomers don't share a table, so
    // this flag - not a lookup - is what lets either UI render "you" vs
    // "the support team" correctly without cross-referencing two tables.
    public bool IsFromCustomer { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
