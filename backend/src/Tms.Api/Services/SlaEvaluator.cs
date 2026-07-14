using Tms.Api.Models;

namespace Tms.Api.Services;

// Module 4 - SLA Management. Pure logic, no DB access, so it's easy to reason
// about and unit test independently of EF Core. Callers (TicketsController)
// own persistence - this class only mutates the in-memory entity and reports
// back whether anything changed, so the caller knows whether a SaveChangesAsync
// is actually needed.
public static class SlaEvaluator
{
    // Prefers a policy matching the ticket's exact priority; falls back to the
    // tenant's default policy (Priority == null) if no exact match exists.
    public static SlaPolicy? FindMatchingPolicy(IEnumerable<SlaPolicy> policies, TicketPriority priority)
    {
        SlaPolicy? fallback = null;
        foreach (var policy in policies)
        {
            if (policy.Priority == priority) return policy;
            if (policy.Priority is null) fallback = policy;
        }
        return fallback;
    }

    // Called once, at ticket creation. Due dates are a commitment made at
    // intake and are deliberately not recalculated if priority changes later
    // (see comment on Ticket.DueAt).
    public static void ApplyPolicyToNewTicket(Ticket ticket, SlaPolicy? policy)
    {
        if (policy is null) return;

        ticket.SlaPolicyId = policy.Id;
        ticket.ResponseDueAt = ticket.CreatedAt.AddMinutes(policy.ResponseTargetMinutes);
        ticket.DueAt = ticket.CreatedAt.AddMinutes(policy.ResolutionTargetMinutes);
    }

    // Lazily checked whenever a ticket is read (GetTicket/GetTickets). If the
    // resolution target has passed and the ticket isn't already resolved,
    // closed, or previously escalated, bumps priority up one level and marks
    // it escalated. Returns true if the entity was mutated, so the caller
    // knows to persist the change.
    public static bool CheckAndEscalate(Ticket ticket, DateTime utcNow)
    {
        if (ticket.Escalated) return false;
        if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed) return false;
        if (ticket.DueAt is null || utcNow <= ticket.DueAt.Value) return false;

        ticket.Escalated = true;
        ticket.Priority = NextPriority(ticket.Priority);
        return true;
    }

    public static TicketPriority NextPriority(TicketPriority current) => current switch
    {
        TicketPriority.Low => TicketPriority.Medium,
        TicketPriority.Medium => TicketPriority.High,
        TicketPriority.High => TicketPriority.Urgent,
        TicketPriority.Urgent => TicketPriority.Urgent,
        _ => current,
    };

    // Historical fact, not mutated state - a resolved ticket that blew its
    // resolution target is still reported as having breached, even though
    // there's nothing left to escalate.
    //
    // For a resolved/closed ticket, breach is judged against ResolvedAt, not
    // utcNow - otherwise a ticket resolved well within its target would flip
    // to "breached" the moment someone views it after the due date has since
    // passed, purely because time kept moving after the ticket was already
    // done. An open ticket has no ResolvedAt yet, so it still falls back to
    // "is the deadline already behind us right now".
    public static bool IsResolutionBreached(Ticket ticket, DateTime utcNow)
    {
        if (ticket.DueAt is null) return false;
        var comparisonPoint = ticket.ResolvedAt ?? utcNow;
        return comparisonPoint > ticket.DueAt.Value;
    }

    public static bool IsResponseBreached(Ticket ticket, DateTime utcNow)
    {
        if (ticket.ResponseDueAt is null) return false;
        if (ticket.FirstRespondedAt is null) return utcNow > ticket.ResponseDueAt.Value;
        return ticket.FirstRespondedAt.Value > ticket.ResponseDueAt.Value;
    }
}
