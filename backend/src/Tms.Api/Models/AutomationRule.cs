namespace Tms.Api.Models;

// Module 5 - Workflow Automation & Business Rules. Scoped down from the full
// spec (see docs/tms_spec.md Module 5) to what fits this deployment's
// constraints - no background worker/queue, no outbound webhook
// infrastructure:
//   - Triggers: TicketCreated, TicketUpdated, CustomerReplyReceived. Rules
//     run synchronously, inline, at the exact point each event already
//     happens in TicketsController/PortalTicketsController - same
//     lazy/no-cron pattern as SLA breach detection (SlaEvaluator) and
//     Notifications (INotificationService). "SLA about to breach" is left
//     out - it needs proactive scanning ahead of the deadline, which this
//     deployment has no scheduler to run.
//   - One condition per rule (field + value), not the full AND/OR condition
//     groups a true no-code builder would need - kept simple enough to ship
//     without a visual rule-group editor. "Keyword in description" from the
//     spec's trigger list is modeled here as a condition on TicketCreated,
//     not a standalone trigger - a keyword match reads more naturally as
//     something a rule checks, not an event that fires it.
//   - Actions: SetPriority, SetStatus, AssignToAgent, AssignRoundRobin,
//     Notify. "Run webhook" is left out - firing arbitrary outbound HTTP
//     from tenant-configurable rules is an SSRF surface that needs its own
//     security review before shipping. Approval workflows are a distinct
//     enough feature (sign-off state, not a trigger/action pair) to leave
//     for a later pass.
public enum AutomationTrigger { TicketCreated, TicketUpdated, CustomerReplyReceived }

public enum AutomationConditionField { None, Priority, Category, SubjectContains, DescriptionContains }

public enum AutomationActionType { SetPriority, SetStatus, AssignToAgent, AssignRoundRobin, Notify }

public class AutomationRule
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Not editable after creation (same rationale as SlaPolicy.Priority) -
    // changing which event a rule listens for changes its entire meaning;
    // delete and recreate instead of silently repurposing it.
    public AutomationTrigger Trigger { get; set; }

    public AutomationConditionField ConditionField { get; set; } = AutomationConditionField.None;
    public string? ConditionValue { get; set; }

    public AutomationActionType ActionType { get; set; }
    public string? ActionValue { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Execution audit trail - the spec's "done when" bar for this module is
// "a tenant admin can build a rule with no code and see it fire correctly
// in the audit log," so this is scoped as a rule-execution log rather than
// the tenant-wide change-audit log described separately under Module 5.4.
public class AutomationRuleLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RuleId { get; set; }
    public Guid TicketId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime FiredAt { get; set; } = DateTime.UtcNow;
}
