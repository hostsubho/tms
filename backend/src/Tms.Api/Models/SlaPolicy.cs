namespace Tms.Api.Models;

public class SlaPolicy
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ResponseTargetMinutes { get; set; }
    public int ResolutionTargetMinutes { get; set; }

    // Null = fallback/default policy applied when no policy targets the
    // ticket's specific priority. At most one policy per (TenantId, Priority)
    // is enforced at the application level (see SlaPoliciesController) -
    // EF Core can't express a partial unique index across nullable Priority
    // cleanly without raw SQL, so it's checked in code instead.
    public TicketPriority? Priority { get; set; }
}
