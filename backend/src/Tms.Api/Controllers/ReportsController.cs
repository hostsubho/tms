using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Reports;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 9 - Reporting & Analytics (tenant-level dashboards only - scheduled
// PDF/CSV exports and the enterprise-tier custom report builder need an
// email/scheduling backend this deployment doesn't have, so they're left for
// a later pass; see docs/tms_spec.md Module 9).
//
// TenantStaff-only, same as the other tenant controllers: a portal customer's
// token must never see aggregate figures across every ticket in the tenant.
//
// Everything here is computed on request rather than pre-aggregated in the
// background (no worker/cron in this deployment, same lazy-evaluation
// approach as SlaEvaluator) - fine at the ticket volumes a single tenant
// generates today, but would need real aggregation (materialized views, a
// scheduled rollup table) before this could scale to a tenant with millions
// of tickets.
[ApiController]
[Route("api/reports")]
[Authorize(Policy = "TenantStaff")]
public class ReportsController : ControllerBase
{
    private readonly TmsDbContext _db;

    public ReportsController(TmsDbContext db)
    {
        _db = db;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<ReportsDashboardResponse>> GetDashboard(CancellationToken ct)
    {
        var utcNow = DateTime.UtcNow;

        // Tenant-scoped automatically via the DbContext global query filter.
        // Pulled into memory once and reused for every section below rather
        // than issuing a separate aggregate query per metric - keeps this
        // readable and avoids re-deriving breach/resolution state (which
        // lives in C# via SlaEvaluator, not SQL) multiple times.
        var tickets = await _db.Tickets.AsNoTracking().ToListAsync(ct);
        var agents = await _db.Users.AsNoTracking().ToListAsync(ct);

        var ticketVolume = BuildTicketVolume(tickets, utcNow);
        var slaCompliance = BuildSlaCompliance(tickets, utcNow);
        var agentPerformance = BuildAgentPerformance(tickets, agents, utcNow);
        var csat = BuildCsat(tickets);

        return Ok(new ReportsDashboardResponse(ticketVolume, slaCompliance, agentPerformance, csat));
    }

    private static TicketVolumeReport BuildTicketVolume(List<Ticket> tickets, DateTime utcNow)
    {
        var windowStart = DateOnly.FromDateTime(utcNow.Date).AddDays(-29);
        var byDay = tickets
            .Where(t => DateOnly.FromDateTime(t.CreatedAt) >= windowStart)
            .GroupBy(t => DateOnly.FromDateTime(t.CreatedAt))
            .ToDictionary(g => g.Key, g => g.Count());

        var last30Days = Enumerable.Range(0, 30)
            .Select(offset => windowStart.AddDays(offset))
            .Select(date => new DailyCount(date, byDay.GetValueOrDefault(date)))
            .ToList();

        return new TicketVolumeReport(
            Total: tickets.Count,
            New: tickets.Count(t => t.Status == TicketStatus.New),
            Open: tickets.Count(t => t.Status == TicketStatus.Open),
            Pending: tickets.Count(t => t.Status == TicketStatus.Pending),
            Resolved: tickets.Count(t => t.Status == TicketStatus.Resolved),
            Closed: tickets.Count(t => t.Status == TicketStatus.Closed),
            Last30Days: last30Days);
    }

    private static SlaComplianceReport BuildSlaCompliance(List<Ticket> tickets, DateTime utcNow)
    {
        // Only tickets that actually had an SLA policy matched at intake
        // count toward the denominator - tickets created before any policy
        // existed, or with no matching policy, were never subject to a
        // target and shouldn't drag the percentage down.
        var withSla = tickets.Where(t => t.DueAt is not null).ToList();
        var breached = withSla.Count(t => SlaEvaluator.IsResolutionBreached(t, utcNow));

        // An empty denominator reads as 100% compliant rather than 0% - "no
        // SLA-tracked tickets yet" is not the same as "every one breached".
        var compliancePercentage = withSla.Count == 0
            ? 100.0
            : Math.Round((withSla.Count - breached) * 100.0 / withSla.Count, 1);

        return new SlaComplianceReport(withSla.Count, breached, compliancePercentage);
    }

    private static List<AgentPerformanceEntry> BuildAgentPerformance(List<Ticket> tickets, List<AppUser> agents, DateTime utcNow)
    {
        var byAssignee = tickets
            .Where(t => t.AssigneeId is not null)
            .GroupBy(t => t.AssigneeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Every active tenant user is listed, even with zero tickets - a
        // manager wants to see who has no load just as much as who's
        // overloaded, and an agent-count-0 row would otherwise silently
        // vanish from the report.
        return agents
            .Where(a => a.IsActive)
            .Select(agent =>
            {
                var assigned = byAssignee.GetValueOrDefault(agent.Id, new List<Ticket>());
                var resolved = assigned.Where(t => t.ResolvedAt is not null).ToList();
                var breached = assigned.Count(t => SlaEvaluator.IsResolutionBreached(t, utcNow));
                double? avgResolutionHours = resolved.Count == 0
                    ? null
                    : Math.Round(resolved.Average(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalHours), 1);

                return new AgentPerformanceEntry(
                    agent.Id, agent.Email, assigned.Count, resolved.Count, breached, avgResolutionHours);
            })
            .OrderByDescending(e => e.AssignedCount)
            .ToList();
    }

    private static CsatReport BuildCsat(List<Ticket> tickets)
    {
        var rated = tickets.Where(t => t.CsatRating is not null && t.CsatSubmittedAt is not null).ToList();

        var distribution = Enumerable.Range(1, 5).ToDictionary(star => star, _ => 0);
        foreach (var t in rated) distribution[t.CsatRating!.Value]++;

        var byDay = rated
            .GroupBy(t => DateOnly.FromDateTime(t.CsatSubmittedAt!.Value))
            .OrderBy(g => g.Key)
            .Select(g => new DailyCsat(g.Key, Math.Round(g.Average(t => t.CsatRating!.Value), 1), g.Count()))
            .ToList();

        double? average = rated.Count == 0 ? null : Math.Round(rated.Average(t => t.CsatRating!.Value), 1);

        return new CsatReport(average, rated.Count, distribution, byDay);
    }
}
