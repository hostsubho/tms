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

public record AgentPerformanceEntry(
    Guid AgentId,
    string AgentEmail,
    int AssignedCount,
    int ResolvedCount,
    int BreachedCount,
    double? AvgResolutionHours);

public record DailyCsat(DateOnly Date, double AverageRating, int Count);

public record CsatReport(
    double? AverageRating,
    int TotalRatings,
    IReadOnlyDictionary<int, int> Distribution,
    IReadOnlyList<DailyCsat> Last30Days);

public record ReportsDashboardResponse(
    TicketVolumeReport TicketVolume,
    SlaComplianceReport SlaCompliance,
    IReadOnlyList<AgentPerformanceEntry> AgentPerformance,
    CsatReport Csat);
