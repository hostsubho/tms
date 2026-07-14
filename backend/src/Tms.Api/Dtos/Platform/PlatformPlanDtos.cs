using Tms.Api.Models;

namespace Tms.Api.Dtos.Platform;

// Distinct from the public Dtos.Onboarding.PlanResponse used by GET
// /api/plans - this one additionally exposes StripePriceId, which the
// public signup-facing endpoint has no reason to return.
public record PlatformPlanResponse(
    Guid Id, string Name, int MaxAgents, int MaxTicketsPerMonth, decimal PriceMonthly, string? StripePriceId)
{
    public static PlatformPlanResponse FromEntity(Plan p) => new(
        p.Id, p.Name, p.MaxAgents, p.MaxTicketsPerMonth, p.PriceMonthly, p.StripePriceId);
}

public record CreatePlanRequest(
    string Name, int MaxAgents, int MaxTicketsPerMonth, decimal PriceMonthly, string? StripePriceId);

public record UpdatePlanRequest(
    string? Name, int? MaxAgents, int? MaxTicketsPerMonth, decimal? PriceMonthly, string? StripePriceId);
