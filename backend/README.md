# TMS Backend (.NET 8 Web API)

Multi-tenant API for the TMS ticket management SaaS. See `/docs` at the repo
root for the full module-wise feature spec PDF.

## Prerequisites

- .NET 8 SDK
- EF Core CLI tools: `dotnet tool install --global dotnet-ef`
- A Neon Postgres connection string (the `tms` database — ask whoever set up
  the Neon project, or use your own dev branch)

## First-time setup

Secrets never go in `appsettings*.json` (those are committed to git). Use
`dotnet user-secrets` instead — it stores values outside the repo, keyed to
this project only:

```
cd src/Tms.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:TmsDb" "<your Neon connection string>"
dotnet user-secrets set "Auth:SigningKey" "<any random 32+ character string>"
```

Generate a signing key quickly with `openssl rand -base64 48`.

## Create the database schema

The database is currently empty (no migrations have been generated yet).
From `src/Tms.Api`:

```
dotnet ef migrations add InitialCreate --output-dir Migrations
dotnet ef database update
```

This reads the EF Core models (`Models/*.cs` + `Data/TmsDbContext.cs`) and
creates `Tenants`, `Users`, `Tickets`, `TicketComments`, `Categories`,
`SlaPolicies`, `RefreshTokens`, and `PlatformUsers` in the Neon `tms`
database, each tenant-scoped table with a `TenantId` column and composite
indexes described in the feature spec. `PlatformUsers` has no `TenantId` at
all - platform staff are never members of a tenant.

If you already have a database from an earlier iteration, run new migrations
incrementally instead of `InitialCreate`, e.g. the latest one adds `Plans`
and the branding/timezone columns on `Tenants`:
`dotnet ef migrations add AddPlansAndTenantBranding --output-dir Migrations && dotnet ef database update`

Module 4 (SLA Management) adds `ResponseDueAt`, `FirstRespondedAt`, `Escalated`
to `Tickets` and `Priority` to `SlaPolicies`:
`dotnet ef migrations add AddSlaFields --output-dir Migrations && dotnet ef database update`

Module 7 (Customer/End-User Portal) adds a new `PortalCustomers` table,
`CustomerId`/`CsatRating`/`CsatSubmittedAt` to `Tickets`, and `IsFromCustomer`
to `TicketComments`:
`dotnet ef migrations add AddCustomerPortal --output-dir Migrations && dotnet ef database update`

Module 8 (Notifications) adds a new `Notifications` table and
`NotificationsEnabled` to both `Users` and `PortalCustomers`:
`dotnet ef migrations add AddNotifications --output-dir Migrations && dotnet ef database update`

Module 9 (Reporting & Analytics) adds `ResolvedAt` to `Tickets`:
`dotnet ef migrations add AddTicketResolvedAt --output-dir Migrations && dotnet ef database update`

Then seed the default plans (`docs/seed-plans.sql`) — `Tenant.PlanId` is a
required FK and there's no admin UI for creating plans yet:
`psql "<connection string>" -f docs/seed-plans.sql`

For a real tenant, use `POST /api/onboarding/signup` (self-serve) or the
Super Admin API (sales-assisted, see below) instead of hand-editing the
database. `docs/seed-dev-tenant.sql` is still there for a quick throwaway
test tenant (`acme`) if you just want to poke at `/api/auth/register` without
going through either onboarding flow.

## Run it

```
dotnet run --project src/Tms.Api
```

Swagger UI is available at `/swagger` in Development.

## API surface (this iteration)

**Onboarding** (`/api/onboarding`) — Module 2, self-serve signup
- `POST /signup` — `{ companyName, subdomain, planId, adminEmail, adminPassword, timeZone? }`. Creates the tenant **and** its first Admin user in one call, returns tokens immediately (same shape as `/api/auth/register`/`login`) — this is the "signup to working workspace in one request" flow. Distinct from `/api/platform/tenants` (sales-assisted/manual provisioning by a Super Admin).

**Plans** (`/api/plans`) — public, no auth
- `GET /` — list of plans (id, name, limits, price) for the signup wizard to show. Seed data via `docs/seed-plans.sql`; no write endpoint yet.

**Tenant Settings** (`/api/tenant`) — requires a tenant AppUser token
- `GET /me` — the caller's own tenant (name, subdomain, timezone, branding, plan, status, trial end). Any authenticated tenant user.
- `PATCH /me` — update name/timezone/branding. `Admin` only.
- `POST /me/plan` — `{ planId }`, the upgrade/downgrade flow. `Admin` only.

**Auth** (`/api/auth`) — Module 1
- `POST /register` — `{ tenantSlug, email, password }`. First user for a tenant becomes `Admin`, others default to `Agent`.
- `POST /login` — `{ tenantSlug, email, password }`. Blocked if the tenant is `Suspended`/`Churned`.
- `POST /refresh` — `{ refreshToken }`. Rotates the refresh token on every use.
- `POST /logout` — `{ refreshToken }`. Revokes it.

All return `{ accessToken, accessTokenExpiresAtUtc, refreshToken, userId, email, role }`.
Access tokens are short-lived JWTs (15 min default); send as `Authorization: Bearer <token>`.

**Tickets** (`/api/tickets`) — Module 3, requires auth
- `GET /` — filterable by `status`, `assigneeId`. Response includes SLA fields (see Module 4 below); reading the list lazily checks every ticket for a new SLA breach and escalates it if so, persisting the change.
- `GET /{id}` — same lazy breach/escalation check as the list, scoped to this ticket.
- `POST /` — creates a ticket; `TenantId`/`RequesterId`/`SlaPolicyId` are always set server-side, never from the request body. `SlaPolicyId` is auto-matched from the tenant's SLA policies by the ticket's `Priority` (see Module 4).
- `PATCH /{id}` — partial update (status, priority, assignee, etc.). Does **not** recompute `DueAt`/`ResponseDueAt` — SLA due dates are a one-time commitment made at creation.
- `GET /{id}/comments`, `POST /{id}/comments` — `isInternal` flag separates agent notes from customer-visible replies; every comment posted here (the staff surface) sets `FirstRespondedAt` the first time, used for response-SLA breach detection. Comments posted through the customer portal (`/api/portal/tickets/{id}/comments`, Module 7) do **not** advance `FirstRespondedAt` — it measures how fast staff reply to the customer, not the customer's own messages.

**Categories** (`/api/categories`)
- `GET /`
- `POST /` — restricted to `Admin`/`Manager` roles

**SLA Policies** (`/api/sla-policies`) — Module 4, requires auth
- `GET /` — any authenticated tenant user.
- `POST /` — `{ name, responseTargetMinutes, resolutionTargetMinutes, priority? }`. `Admin`/`Manager` only. `priority` omitted/null makes this the tenant's default/fallback policy (applied when no policy targets the ticket's specific priority). At most one policy per priority (including at most one default) is enforced — a second attempt returns 409.
- `PATCH /{id}` — update name/targets. `Admin`/`Manager` only. Priority can't be changed after creation (delete and recreate instead).
- `DELETE /{id}` — `Admin`/`Manager` only. Tickets already assigned to a deleted policy keep their already-computed due dates.
- Every `TicketResponse` includes `dueAt` (resolution target), `responseDueAt`, `firstRespondedAt`, `escalated`, `isResolutionBreached`, `isResponseBreached`. Breach detection and escalation (bump priority one level, once) run lazily whenever a ticket is read via `GET /api/tickets` or `GET /api/tickets/{id}` — there's no background worker in this deployment, so a breach is only caught the next time someone views the ticket or list, not the instant it happens.

**Portal Auth** (`/api/portal/auth`) — Module 7, Customer/End-User Portal
- `POST /register` — `{ tenantSlug, name, email, password }`. Creates a `PortalCustomer` scoped to the tenant and returns a token immediately. Entirely separate table/identity from `AppUser` — a portal customer is an external end user, never tenant staff.
- `POST /login` — `{ tenantSlug, email, password }`.
- Both return `{ accessToken, accessTokenExpiresAtUtc, customerId, name, email }`. No refresh token (same short-lived-by-design tradeoff as Platform Auth below) — re-login when the 15 min access token expires.
- Tokens carry `scope=portal_customer` + `tenant_id` + `customer_id`, no `Role` claim — can't satisfy staff `[Authorize(Roles=...)]` checks or the `PlatformAdmin`/`PlatformManage` policies, and those tokens can't satisfy the `PortalCustomer` policy either. Three JWT scopes, one signing key, mutually exclusive by claim shape.

**Portal Tickets** (`/api/portal/tickets`) — Module 7, requires a portal customer token
- `GET /`, `GET /{id}` — only tickets where `customerId` matches the caller; same lazy SLA breach/escalation check as the staff surface, but the response (`PortalTicketResponse`) deliberately omits internal SLA-ops fields like `escalated`/`isResolutionBreached`/`assigneeId` — a customer sees status, priority, `dueAt`, and their own CSAT state, not internal escalation mechanics.
- `POST /` — `{ subject, description, priority }`. `customerId`/`tenantId` always set server-side; SLA policy matching works identically to the staff `POST /api/tickets`.
- `GET /{id}/comments`, `POST /{id}/comments` — `{ body }` only; `isInternal` is always `false` and can't be set by the customer, and `GET` never returns internal notes even if requested directly.
- `POST /{id}/csat` — `{ rating: 1-5 }`. Only allowed once the ticket is `Resolved`/`Closed`, and only once per ticket (409 on a second attempt).

**Notifications** (`/api/notifications`) — Module 8, requires auth · **Portal Notifications** (`/api/portal/notifications`) — same shape, requires a portal customer token
- In-app only for this iteration — no email/SMS/push provider is configured, and there's no background worker/queue in this deployment (same constraint that keeps SLA breach detection lazy-on-read rather than a cron sweep). Notifications are written synchronously, in the same DB transaction as the event that triggered them.
- `GET /` — the caller's own notifications (most recent 50), newest first.
- `POST /{id}/read` — mark one as read. `POST /read-all` — mark every unread one as read.
- `GET /preferences`, `PATCH /preferences` — `{ enabled }`. A single on/off switch per user/customer, not per-event-type — there's only one channel (in-app) to toggle in this iteration, so per-channel granularity would be UI for controls that don't do anything yet. Disabling means `NotificationService` skips writing new notifications for that recipient entirely; it doesn't hide existing ones.
- Trigger points: ticket created (notifies the assignee if pre-assigned, otherwise every `Admin`), ticket assigned/reassigned via `PATCH /api/tickets/{id}` (notifies the new assignee, only when the assignee actually changed), a staff reply on a ticket with a portal customer attached (notifies that customer, skipped for internal notes), a customer reply on an assigned ticket (notifies the assignee), and an SLA breach caught lazily on read (notifies the assignee, or every `Admin` if unassigned).

**Reports** (`/api/reports`) — Module 9, requires the `TenantStaff` policy (not reachable with a portal customer token)
- `GET /dashboard` — one aggregate payload: `ticketVolume` (totals by status, plus a 30-day daily-created series), `slaCompliance` (breached count + `compliancePercentage` over tickets that had an SLA policy matched at all — a tenant with none tracked reads as 100%, not 0%), `agentPerformance` (per active agent: assigned/resolved/breached counts and average resolution time in hours), `csat` (average rating, total ratings, a 1–5 star distribution, and a 30-day daily-average series).
- Computed on request, not pre-aggregated by a background job — same lazy-evaluation constraint as SLA breach detection (no worker/cron in this deployment). Fine at today's per-tenant ticket volumes; would need real rollup tables before scaling to millions of rows.
- Only tenant-level dashboards are built in this iteration. Scheduled PDF/CSV exports and the enterprise-tier custom report builder (also listed under Module 9 in the spec) need an email/scheduling backend this deployment doesn't have yet.
- `SlaEvaluator.IsResolutionBreached` was corrected alongside this: it now judges a resolved/closed ticket's breach status against its `ResolvedAt` timestamp, not `utcNow`. Previously, a ticket resolved comfortably inside its SLA window would flip to "breached" the moment someone viewed it after the due date had since passed in real time — purely an artifact of when it was looked at, not what actually happened. This affected the live `isResolutionBreached` field on every `TicketResponse`/`PortalTicketResponse` too, not just this report.

**Platform Auth** (`/api/platform/auth`) — Module 5, Super Admin console
- `POST /bootstrap` — `{ name, email, password }`. Only works once, while `PlatformUsers` is empty; creates the first `Owner`. 409 after that.
- `POST /login` — `{ email, password }`.

Both return `{ accessToken, accessTokenExpiresAtUtc, userId, email, role }`.
No refresh token here yet (platform sessions are short-lived by design for
this iteration — re-login when the 15 min access token expires).

**Platform Tenants** (`/api/platform/tenants`) — Module 5.1, requires a platform token
- `GET /`, `GET /{id}` — any platform role (`Owner`, `PlatformAdmin`, `SupportEngineer`, `BillingAdmin`, `ReadOnlyAnalyst`)
- `POST /` — `{ name, subdomain, planId, trialDays? }`. `Owner`/`PlatformAdmin` only.
- `POST /{id}/suspend`, `POST /{id}/reactivate` — `Owner`/`PlatformAdmin` only.

A platform token carries `scope=platform_admin` + `platform_role=<PlatformRole>`
and never a `tenant_id` claim, so it can't be used against `/api/tickets` etc.,
and a tenant AppUser's token can't be used here — completely separate
authorization surfaces by design.

## Multi-tenancy

- Every tenant-scoped table has a `TenantId` column + composite index.
- `TmsDbContext` applies a global EF Core query filter per table via `ITenantContext`.
- `TenantResolutionMiddleware` resolves the tenant from the JWT `tenant_id` claim
  (authenticated requests). `AuthController` resolves it directly from the
  tenant slug for the pre-auth register/login/refresh calls, using the same
  `ITenantContext`.
- `docs/rls-policies.sql` adds Postgres Row-Level Security as a second layer
  (apply it after running migrations — it references tables that must already exist).
- Roles are the fixed `Role` enum (`Admin`, `Manager`, `Agent`, `ReadOnly`) for
  now — full custom roles/permissions are Phase 3 (Module 12) per the spec.
- Staff-facing tenant controllers (`TicketsController`, `TenantController`,
  `SlaPoliciesController`, `CategoriesController`) use `[Authorize(Policy =
  "TenantStaff")]`, **not** bare `[Authorize]`. This matters as of Module 7:
  a bare `[Authorize]` only checks `IsAuthenticated`, and a portal customer's
  token now carries a valid `tenant_id` claim (same as staff), so without the
  `TenantStaff` policy a customer could reach the full staff ticket queue,
  including internal-only notes. `TenantStaff` requires the `Role` claim to
  be present at all — only `AppUser` tokens ever set it (see
  `JwtTokenService.CreateAccessToken`); `PlatformUser` and `PortalCustomer`
  tokens never do, so both are excluded by construction.
- Super Admin endpoints (`/api/platform/*`) use a separate `PlatformUser`
  table/auth scheme (Module 5), never reachable with a tenant JWT. The
  `PlatformAdmin` policy allows any platform role (read access); `PlatformManage`
  additionally requires `platform_role` to be `Owner` or `PlatformAdmin` (write access).

## Deploy

`.github/workflows/azure-deploy.yml` builds and deploys to Azure App Service
on push to `main`. Requires `AZURE_WEBAPP_PUBLISH_PROFILE` repo secret, plus
`ConnectionStrings__TmsDb` and `Auth__SigningKey` set in the App Service's
own configuration (Azure Portal → Configuration → Application settings) —
never in source control.
