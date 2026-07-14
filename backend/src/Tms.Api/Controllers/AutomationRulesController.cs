using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.Api.Data;
using Tms.Api.Dtos.Automation;
using Tms.Api.Extensions;
using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Controllers;

// Module 5 - Workflow Automation & Business Rules. Read is open to any
// authenticated tenant staff member (agents should be able to see why a
// ticket got auto-assigned/reprioritized); write is restricted to
// Admin/Manager, same pattern as SlaPoliciesController/CategoriesController.
[ApiController]
[Route("api/automation-rules")]
[Authorize(Policy = "TenantStaff")]
public class AutomationRulesController : ControllerBase
{
    private readonly TmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService _auditLog;

    public AutomationRulesController(TmsDbContext db, ITenantContext tenantContext, IAuditLogService auditLog)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AutomationRuleResponse>>> GetRules(CancellationToken ct)
    {
        var rules = await _db.AutomationRules.OrderBy(r => r.CreatedAt).ToListAsync(ct);
        return Ok(rules.Select(AutomationRuleResponse.FromEntity));
    }

    [HttpPost]
    [Authorize(Policy = "Permission:ManageAutomationRules")]
    public async Task<ActionResult<AutomationRuleResponse>> CreateRule([FromBody] CreateAutomationRuleRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var rule = new AutomationRule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            IsActive = true,
            Trigger = request.Trigger,
            ConditionField = request.ConditionField,
            ConditionValue = request.ConditionValue,
            ActionType = request.ActionType,
            ActionValue = request.ActionValue,
            CreatedAt = DateTime.UtcNow,
        };

        _db.AutomationRules.Add(rule);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Created,
            AuditEntityType.AutomationRule, rule.Id, $"Created automation rule '{rule.Name}'.");

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetRules), AutomationRuleResponse.FromEntity(rule));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "Permission:ManageAutomationRules")]
    public async Task<ActionResult<AutomationRuleResponse>> UpdateRule(Guid id, [FromBody] UpdateAutomationRuleRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var rule = await _db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();

        if (request.Name is not null) rule.Name = request.Name;
        if (request.IsActive is not null) rule.IsActive = request.IsActive.Value;
        if (request.ConditionField is not null) rule.ConditionField = request.ConditionField.Value;
        if (request.ConditionValue is not null) rule.ConditionValue = request.ConditionValue;
        if (request.ActionType is not null) rule.ActionType = request.ActionType.Value;
        if (request.ActionValue is not null) rule.ActionValue = request.ActionValue;

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Updated,
            AuditEntityType.AutomationRule, rule.Id, $"Updated automation rule '{rule.Name}'.");

        await _db.SaveChangesAsync(ct);
        return Ok(AutomationRuleResponse.FromEntity(rule));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Permission:ManageAutomationRules")]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant could not be resolved for this request.");

        var rule = await _db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();

        // Past executions stay in the log with a now-dangling RuleId - the
        // audit trail of what a rule did while it existed shouldn't
        // disappear just because the rule itself was later removed.
        _db.AutomationRules.Remove(rule);

        _auditLog.Record(tenantId, User.GetUserId(), User.GetEmail(), AuditAction.Deleted,
            AuditEntityType.AutomationRule, rule.Id, $"Deleted automation rule '{rule.Name}'.");

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("logs")]
    public async Task<ActionResult<IEnumerable<AutomationRuleLogResponse>>> GetLogs(CancellationToken ct)
    {
        var logs = await _db.AutomationRuleLogs
            .OrderByDescending(l => l.FiredAt)
            .Take(100)
            .ToListAsync(ct);

        if (logs.Count == 0) return Ok(Array.Empty<AutomationRuleLogResponse>());

        // Small, bounded (<=100) follow-up lookups rather than a SQL join -
        // rule/ticket names are just for display, and rules may have since
        // been deleted (see DeleteRule) so these are looked up defensively.
        var ruleIds = logs.Select(l => l.RuleId).Distinct().ToList();
        var ticketIds = logs.Select(l => l.TicketId).Distinct().ToList();
        var ruleNames = await _db.AutomationRules
            .Where(r => ruleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var ticketSubjects = await _db.Tickets
            .Where(t => ticketIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Subject, ct);

        return Ok(logs.Select(l => new AutomationRuleLogResponse(
            l.Id,
            l.RuleId,
            ruleNames.GetValueOrDefault(l.RuleId, "(deleted rule)"),
            l.TicketId,
            ticketSubjects.GetValueOrDefault(l.TicketId, "(unknown ticket)"),
            l.Summary,
            l.FiredAt)));
    }
}
