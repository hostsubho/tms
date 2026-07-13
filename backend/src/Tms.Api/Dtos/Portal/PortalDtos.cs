using Tms.Api.Models;
using Tms.Api.Services;

namespace Tms.Api.Dtos.Portal;

// TenantSlug maps to Tenant.Subdomain, same convention as the staff
// RegisterRequest/LoginRequest in Dtos/Auth - a customer signs up into an
// already-existing tenant's portal, never creates one.
public record PortalRegisterRequest(string TenantSlug, string Name, string Email, string Password);

public record PortalLoginRequest(string TenantSlug, string Email, string Password);

// No refresh token here, same design choice as PlatformAuthResponse - short
// access-token lifetime, re-login on expiry. Simpler than tenant AppUser
// auth and appropriate for a low-frequency, low-privilege surface.
public record PortalAuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    Guid CustomerId,
    string Name,
    string Email)
{
    public static PortalAuthResponse FromToken(AccessTokenResult token, PortalCustomer customer) => new(
        token.Token, token.ExpiresAtUtc, customer.Id, customer.Name, customer.Email);
}

// No CategoryId/AssigneeId - those are staff-facing triage concerns a
// customer submitting a ticket has no reason to set.
public record PortalCreateTicketRequest(string Subject, string? Description, TicketPriority Priority);

public record PortalAddCommentRequest(string Body);

public record PortalCsatRequest(int Rating);

// Deliberately narrower than the staff-facing TicketResponse (Dtos/Tickets) -
// no Escalated/IsResolutionBreached/IsResponseBreached/AssigneeId. Those are
// internal SLA/ops details; a customer just needs to know status, priority,
// when to expect resolution, and their own CSAT submission.
public record PortalTicketResponse(
    Guid Id,
    string Subject,
    string? Description,
    TicketStatus Status,
    TicketPriority Priority,
    DateTime CreatedAt,
    DateTime? DueAt,
    int? CsatRating,
    DateTime? CsatSubmittedAt)
{
    public static PortalTicketResponse FromEntity(Ticket t) => new(
        t.Id, t.Subject, t.Description, t.Status, t.Priority, t.CreatedAt, t.DueAt,
        t.CsatRating, t.CsatSubmittedAt);
}

public record PortalCommentResponse(Guid Id, Guid TicketId, string Body, bool IsFromCustomer, DateTime CreatedAt)
{
    public static PortalCommentResponse FromEntity(TicketComment c) => new(
        c.Id, c.TicketId, c.Body, c.IsFromCustomer, c.CreatedAt);
}
