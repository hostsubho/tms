namespace Tms.Api.Dtos.Reports;

// Module 9 - Reporting & Analytics. All shapes here are read-only aggregates
// computed on request (see ReportsController) - there's nothing to persist,
// so unlike the other Dtos files there's no "request" half.

public record DailyCount(DateOnly Date, int Count);

public record TicketVolumeReport(
    int Total,
    int New,
    int Open,
    int Pending,
    int Resolved,
    int Closed,
    IReadOnlyList<DailyCount> Last30Days);

public record SlaComplianceReport(
    int TotalWithSla,
    int Breached,
    double CompliancePercentage);

// "Team & Employee Performance." Extended beyond the original
// AssignedCount/ResolvedCount/BreachedCount/AvgResolutionHours (still here,
// unchanged) with response-time, SLA-compliance, and CSAT breakdowns per
// agent - the per-employee performance view. OpenCount is current workload
// (assigned, not yet resolved) - useful for spotting who's overloaded right
// now, distinct from ResolvedCount's all-time-in-range totals.
public record AgentPerformanceEntry(
    Guid AgentId,
    string AgentEmail,
    int AssignedCount,
    int OpenCount,
    int ResolvedCount,
    int BreachedCount,
    double? AvgResolutionHours,
    double? AvgFirstResponseHours,
    double SlaCompliancePercentage,
    double? AvgCsatRating);

// Team-wide aggregate across every active agent - the same underlying
// numbers as AgentPerformanceEntry, rolled up rather than broken out per
// person, for an at-a-glance "how's the team doing" summary above the
// per-agent breakdown.
public record TeamPerformanceSummary(
    int ActiveAgentCount,
    int TotalAssigned,
    int TotalResolved,
    int TotalBreached,
    double? AvgResolutionHours,
    double? AvgFirstResponseHours,
    double SlaCompliancePercentage,
    double? AvgCsatRating);

public record DailyCsat(DateOnly Date, double AverageRating, int Count);

public record CsatReport(
    double? AverageRating,
    int TotalRatings,
    IReadOnlyDictionary<int, int> Distribution,
    IReadOnlyList<DailyCsat> Last30Days);

public record ReportsDashboardResponse(
    TicketVolumeReport TicketVolume,
    SlaComplianceReport SlaCompliance,
    TeamPerformanceSummary TeamPerformance,
    IReadOnlyList<AgentPerformanceEntry> AgentPerformance,
    CsatReport Csat);
