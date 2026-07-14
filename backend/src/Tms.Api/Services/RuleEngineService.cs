using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Models;

namespace Tms.Api.Services;

public class RuleEngineService : IRuleEngineService
{
    private readonly TmsDbContext _db;
    private readonly INotificationService _notifications;

    public RuleEngineService(TmsDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task RunTriggerAsync(Guid tenantId, AutomationTrigger trigger, Ticket ticket, CancellationToken ct)
    {
        // Tenant-scoped automatically via the DbContext global query filter.
        // Ordered by CreatedAt so if two rules on the same trigger both touch
        // the same field (e.g. two rules both setting Priority), the
        // earliest-created rule's effect is the one a later rule can still
        // see and override - a predictable, if simple, resolution order for
        // what a real no-code builder would otherwise need explicit
        // priority/ordering controls for.
        var rules = await _db.AutomationRules
            .Where(r => r.IsActive && r.Trigger == trigger)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        foreach (var rule in rules)
        {
            if (!ConditionMatches(rule, ticket)) continue;

            var summary = await ApplyActionAsync(rule, tenantId, ticket, ct);
            if (summary is null) continue; // action couldn't be resolved (bad/missing ActionValue) - not logged as a no-op firing.

            _db.AutomationRuleLogs.Add(new AutomationRuleLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RuleId = rule.Id,
                TicketId = ticket.Id,
                Summary = summary,
                FiredAt = DateTime.UtcNow,
            });
        }
    }

    private static bool ConditionMatches(AutomationRule rule, Ticket ticket) => rule.ConditionField switch
    {
        AutomationConditionField.None => true,
        AutomationConditionField.Priority =>
            Enum.TryParse<TicketPriority>(rule.ConditionValue, ignoreCase: true, out var wantedPriority)
            && ticket.Priority == wantedPriority,
        AutomationConditionField.Category =>
            Guid.TryParse(rule.ConditionValue, out var wantedCategoryId)
            && ticket.CategoryId == wantedCategoryId,
        AutomationConditionField.SubjectContains =>
            !string.IsNullOrEmpty(rule.ConditionValue)
            && ticket.Subject.Contains(rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
        AutomationConditionField.DescriptionContains =>
            !string.IsNullOrEmpty(rule.ConditionValue)
            && (ticket.Description ?? string.Empty).Contains(rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    // Returns a human-readable summary of what happened if the action was
    // applied, or null if it couldn't be (bad ActionValue, no agents
    // available, etc.) - callers treat null as "don't log a firing".
    private async Task<string?> ApplyActionAsync(AutomationRule rule, Guid tenantId, Ticket ticket, CancellationToken ct)
    {
        switch (rule.ActionType)
        {
            case AutomationActionType.SetPriority:
                if (!Enum.TryParse<TicketPriority>(rule.ActionValue, ignoreCase: true, out var newPriority)) return null;
                ticket.Priority = newPriority;
                return $"Rule '{rule.Name}' set priority to {newPriority}.";

            case AutomationActionType.SetStatus:
                if (!Enum.TryParse<TicketStatus>(rule.ActionValue, ignoreCase: true, out var newStatus)) return null;
                TicketStatusTransition.ApplyStatus(ticket, newStatus);
                return $"Rule '{rule.Name}' set status to {newStatus}.";

            case AutomationActionType.AssignToAgent:
                if (!Guid.TryParse(rule.ActionValue, out var agentId)) return null;
                var agent = await _db.Users.FirstOrDefaultAsync(u => u.Id == agentId && u.IsActive, ct);
                if (agent is null) return null;
                ticket.AssigneeId = agent.Id;
                return $"Rule '{rule.Name}' assigned to {agent.Email}.";

            case AutomationActionType.AssignRoundRobin:
                var chosen = await PickLeastLoadedAgentAsync(ct);
                if (chosen is null) return "Round-robin assignment skipped: no active agents.";
                ticket.AssigneeId = chosen.Id;
                return $"Rule '{rule.Name}' round-robin assigned to {chosen.Email}.";

            case AutomationActionType.Notify:
                var message = string.IsNullOrWhiteSpace(rule.ActionValue)
                    ? $"Automation rule '{rule.Name}' matched ticket '{ticket.Subject}'."
                    : rule.ActionValue;
                await _notifications.NotifyAdminsAsync(tenantId, NotificationType.NewTicket, message, ticket.Id, ct);
                return $"Rule '{rule.Name}' sent a notification.";

            default:
                return null;
        }
    }

    // "Least loaded" = fewest tickets currently in an unresolved state
    // (New/Open/Pending) assigned to them - a resolved/closed ticket no
    // longer counts against an agent's active workload. Ties fall to
    // whichever agent LINQ's stable sort returns first; a real round-robin
    // (cycling through a fixed order) would need to persist "who's next,"
    // which is more state than this deployment's rule model carries today.
    private async Task<AppUser?> PickLeastLoadedAgentAsync(CancellationToken ct)
    {
        // Role.Agent only - an Admin/Manager account exists to configure the
        // tenant, not to be round-robined a ticket queue alongside its
        // actual support staff.
        var agents = await _db.Users.Where(u => u.IsActive && u.Role == Role.Agent).ToListAsync(ct);
        if (agents.Count == 0) return null;

        var openCounts = await _db.Tickets
            .Where(t => t.AssigneeId != null && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .GroupBy(t => t.AssigneeId!.Value)
            .Select(g => new { AgentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AgentId, x => x.Count, ct);

        return agents.OrderBy(a => openCounts.GetValueOrDefault(a.Id)).First();
    }
}
