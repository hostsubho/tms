using Tms.Api.Models;

namespace Tms.Api.Dtos.Automation;

// Trigger is accepted only on create, never on update - see AutomationRule.Trigger.
public record CreateAutomationRuleRequest(
    string Name,
    AutomationTrigger Trigger,
    AutomationConditionField ConditionField,
    string? ConditionValue,
    AutomationActionType ActionType,
    string? ActionValue);

public record UpdateAutomationRuleRequest(
    string? Name,
    bool? IsActive,
    AutomationConditionField? ConditionField,
    string? ConditionValue,
    AutomationActionType? ActionType,
    string? ActionValue);

public record AutomationRuleResponse(
    Guid Id,
    string Name,
    bool IsActive,
    AutomationTrigger Trigger,
    AutomationConditionField ConditionField,
    string? ConditionValue,
    AutomationActionType ActionType,
    string? ActionValue,
    DateTime CreatedAt)
{
    public static AutomationRuleResponse FromEntity(AutomationRule r) => new(
        r.Id, r.Name, r.IsActive, r.Trigger, r.ConditionField, r.ConditionValue,
        r.ActionType, r.ActionValue, r.CreatedAt);
}

public record AutomationRuleLogResponse(
    Guid Id,
    Guid RuleId,
    string RuleName,
    Guid TicketId,
    string TicketSubject,
    string Summary,
    DateTime FiredAt);
