using Tms.Api.Models;

namespace Tms.Api.Services;

// Extracted from TicketsController.UpdateTicket so the Status/ResolvedAt
// pairing lives in exactly one place. Module 5's automation engine can also
// set a ticket's status (the SetStatus action) and needs the exact same
// ResolvedAt bookkeeping - duplicating this logic a second time risks the
// two copies drifting apart, the same class of bug already fixed once for
// Module 9's SLA compliance numbers (see SlaEvaluator.IsResolutionBreached).
public static class TicketStatusTransition
{
    public static void ApplyStatus(Ticket ticket, TicketStatus newStatus)
    {
        ticket.Status = newStatus;

        if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            ticket.ResolvedAt ??= DateTime.UtcNow;
        }
        else
        {
            ticket.ResolvedAt = null;
        }
    }
}
