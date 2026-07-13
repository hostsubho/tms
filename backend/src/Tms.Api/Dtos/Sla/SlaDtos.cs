using Tms.Api.Models;

namespace Tms.Api.Dtos.Sla;

// Module 4 - SLA Management. A policy with Priority = null is the tenant's
// default/fallback - applied to any ticket whose priority doesn't match a
// more specific policy. At most one policy per (tenant, priority) is allowed,
// enforced in SlaPoliciesController rather than a DB constraint.
public record CreateSlaPolicyRequest(
    string Name,
    int ResponseTargetMinutes,
    int ResolutionTargetMinutes,
    TicketPriority? Priority);

public record UpdateSlaPolicyRequest(
    string? Name,
    int? ResponseTargetMinutes,
    int? ResolutionTargetMinutes);

public record SlaPolicyResponse(
    Guid Id,
    string Name,
    int ResponseTargetMinutes,
    int ResolutionTargetMinutes,
    string? Priority)
{
    public static SlaPolicyResponse FromEntity(SlaPolicy p) => new(
        p.Id, p.Name, p.ResponseTargetMinutes, p.ResolutionTargetMinutes, p.Priority?.ToString());
}
