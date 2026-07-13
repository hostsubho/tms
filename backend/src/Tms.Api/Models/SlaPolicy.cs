namespace Tms.Api.Models;

public class SlaPolicy
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ResponseTargetMinutes { get; set; }
    public int ResolutionTargetMinutes { get; set; }
}
