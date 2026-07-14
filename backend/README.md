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

Module 5 (Workflow Automation & Business Rules) adds `AutomationRules` and `AutomationRuleLogs`:
`dotnet ef migrations add AddAutomationRules --output-dir Migrations && dotnet ef database update`

Module 6 (Knowledge Base) adds `KnowledgeArticles` and `KnowledgeArticleVersions`:
`dotnet ef migrations add AddKnowledgeBase --output-dir Migrations && dotnet ef database update`

Module 5.4 (Audit Logging) adds `AuditLogs`:
`dotnet ef migrations add AddAuditLogs --output-dir Migrations && dotnet ef database update`

Module 12 (Roles & Permissions, custom roles) adds `CustomRoles`, `CustomRolePermissions`, and `CustomRoleId` on `Users`:
`dotnet ef migrations add AddCustomRoles --output-dir Migrations && dotnet ef database update`

Module 11 (Integrations & Public API) adds `ApiKeys`, `WebhookSubscriptions`, and `WebhookDeliveryLogs`:
`dotnet ef migrations add AddIntegrationsApiKeysAndWebhooks --output-dir Migrations && dotnet ef database update`

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

**Users** (`/api/users`) — requires the `TenantStaff` policy
- `GET /` — active tenant staff (id, email, role, and as of Module 12 their assigned custom role id/name if any). Still no invite/deactivate/base-role-change endpoints - this exists so a UI can let someone pick a specific teammate (the automation rule builder's "assign to agent" action, below) instead of requiring a hand-typed GUID.
- `PATCH /{id}/custom-role` — `{ customRoleId: Guid | null }`. `Admin` only, and deliberately **not** gated by any `Permission` - see Custom Roles below for why.

**Automation Rules** (`/api/automation-rules`) — Module 5, requires the `TenantStaff` policy; write (`POST`/`PATCH`/`DELETE`) restricted to `Admin`/`Manager`
- Scoped down from the full spec (see `docs/tms_spec.md` Module 5): one condition per rule (field + value), not full AND/OR condition groups; triggers limited to `TicketCreated`, `TicketUpdated`, `CustomerReplyReceived` — "SLA about to breach" needs proactive scanning ahead of a deadline, which this deployment has no scheduler to run, so it's left out. "Keyword in description" from the spec's trigger list is modeled as a *condition* (`DescriptionContains`) on `TicketCreated`, not a standalone trigger.
- Actions: `SetPriority`, `SetStatus`, `AssignToAgent` (a specific user), `AssignRoundRobin` (least currently-open-ticket-count active `Agent`-role user), `Notify` (a message to every `Admin`). "Run webhook" is left out — arbitrary outbound HTTP from tenant-configurable rules is an SSRF surface needing its own security review before shipping. Approval workflows are a distinct enough feature (sign-off state, not a trigger/action pair) for a later pass.
- Rules run synchronously, inline, at the exact point each event already happens — `POST /api/tickets`, `PATCH /api/tickets/{id}`, `POST /api/portal/tickets`, `POST /api/portal/tickets/{id}/comments` — same lazy/no-cron convention as SLA breach detection and Notifications. They run *before* the existing notification logic at each call site, so a rule-driven reassignment is picked up by the same "notify the assignee" check a manual reassignment triggers, rather than needing its own separate notification path.
- `GET /` — list rules (any tenant staff). `POST /` — create (`Trigger` fixed at creation, like `SlaPolicy.Priority`). `PATCH /{id}` — edit name/condition/action/active state. `DELETE /{id}` — removes the rule; past log entries for it remain, now showing "(deleted rule)".
- `GET /logs` — most recent 100 rule firings tenant-wide, newest first — the audit trail the spec's "done when" bar for this module asks for ("a tenant admin can build a rule with no code and see it fire correctly in the audit log").

**Knowledge Articles** (`/api/knowledge-articles`) — Module 6, requires the `TenantStaff` policy; write (`POST`/`PATCH`/`DELETE`) restricted to `Admin`/`Manager`
- Scoped down from the full spec (see `docs/tms_spec.md` Module 6): "versioning" is a plain history list, not diff/rollback; "suggested articles" is in-memory keyword scoring (`KnowledgeSuggestionMatcher`), not a real search index; helpfulness feedback is an anonymous counter, not deduplicated per customer.
- `GET /` — every article (public and internal), any tenant staff. `GET /{id}` — single article. `POST /` — `{ title, body, isPublic, categoryId? }`. `PATCH /{id}` — partial update; if `title` or `body` changes, the *previous* values are snapshotted to `KnowledgeArticleVersions` first. `DELETE /{id}` — removes the article; its version history stays, now pointing at a deleted article, same convention as `AutomationRuleLog` keeping a dangling `RuleId`.
- `GET /{id}/versions` — history for one article, newest first.

**Portal Knowledge** (`/api/portal/knowledge-articles`) — Module 6, requires a portal customer token; only ever returns `IsPublic` articles
- `GET /?query=...` — keyword-ranked suggestions for a ticket subject (title matches weighted 3x over body matches); with no `query`, falls back to the tenant's most-viewed public articles. Powers the portal's "these articles might already answer your question" panel as a customer types a new ticket's subject.
- `GET /{id}` — full article body; increments `ViewCount`.
- `POST /{id}/feedback` — `{ helpful: bool }`; increments `HelpfulYesCount`/`HelpfulNoCount`.

**Audit Logs** (`/api/audit-logs`) — Module 5.4 (Security & Compliance), requires the `TenantStaff` policy **and** the `ViewAuditLog` permission
- `GET /?entityType=&action=` — most recent 200 entries tenant-wide, newest first; both query params optional (`entityType` one of `Ticket`/`Category`/`SlaPolicy`/`AutomationRule`/`KnowledgeArticle`/`CustomRole`/`User`, `action` one of `Created`/`Updated`/`Deleted`). Read-only — there is no write endpoint anywhere; every row is a side effect of the action it describes, recorded by the other controllers below via `IAuditLogService.Record(...)` in the same DB transaction as the change itself (so an entry can never exist for a change that failed to save, and never be missing for one that succeeded).
- Restricted to the `ViewAuditLog` permission (Admin/Manager always have it; as of Module 12, so does anyone holding a custom role that grants it), unlike most other staff `GET`s in this app (which are open to any `TenantStaff`-authenticated role) — an org-wide "who did what" compliance trail isn't something every agent needs visibility into by default.
- Scoped down from the full spec (see `docs/tms_spec.md` section 5.4): this is the tenant-level audit trail only — a global, cross-tenant audit log of platform-level actions (tenant created/suspended, plan changed, impersonation) is a distinct Super Admin concern that needs the impersonation/plan-change features it would describe to exist first, and is left for a later pass.
- Recorded today: ticket create/update (staff and portal-submitted), category create, SLA policy create/update/delete, automation rule create/update/delete, knowledge article create/update/delete, custom role create/update/delete, custom role assignment/removal on a user, and every automation rule firing (actor `"System (Automation)"`, satisfying Module 5's own "done when" bar: "a tenant admin can build a rule with no code and see it fire correctly in the audit log"). Ticket comments and CSAT submissions are not recorded — kept out of scope to avoid a noisy, low-signal trail; the version history already covers article edits in more detail, and comment content isn't a compliance-relevant "what changed."
- `ActorLabel` is a denormalized snapshot (an email, `"Customer: {email}"` for portal-submitted tickets, or `"System (Automation)"`) rather than a live join to `Users` — a row still reads sensibly even if the acting user is later deactivated or deleted.

**Custom Roles** (`/api/custom-roles`) — Module 12 (Roles & Permissions, tenant-level RBAC), requires the `TenantStaff` policy **and** the `Admin` role - the whole controller, both read and write
- The fixed `Admin`/`Manager`/`Agent`/`ReadOnly` roles from Phase 1 are untouched and keep working exactly as before - this is purely additive. A `Permission` is a coarse, per-module capability, not a per-endpoint ACL: `ManageCategories`, `ManageSlaPolicies`, `ManageAutomationRules`, `ManageKnowledgeArticles`, `ViewAuditLog` - matching the spec's own framing for the done-when bar ("a custom role restricted to specific modules"). Ticket read/write itself isn't part of this set - every tenant staff member already works tickets regardless of role.
- `GET /` — list custom roles with their granted permissions. `POST /` — `{ name, permissions: Permission[] }`. `PATCH /{id}` — `{ name?, permissions? }`; sending `permissions` replaces the full set (not a merge). `DELETE /{id}` — removes the role and its permission grants, and nulls `CustomRoleId` on every user currently holding it in the same transaction (unlike `AutomationRuleLog`/`KnowledgeArticleVersion`'s "dangling reference is harmless history" convention, silently stranding a user's permissions on a deleted role would be a correctness problem, not just cosmetic).
- **Deliberately Admin-only, not gated by any `Permission`** — same for `PATCH /api/users/{id}/custom-role` (see Users above). This is the one place in the module where the design is intentionally *not* a superset of the old behavior: letting a permission-holder (or even a Manager) manage role definitions or assign them would open a privilege-escalation path - a custom role with even one granted permission could otherwise be used to create a broader role and assign it to itself or anyone else. Only the built-in `Admin` role can touch the RBAC surface itself.
- Enforcement mechanism: `PermissionAuthorizationHandler` backs one `[Authorize(Policy = "Permission:X")]` per `Permission` value (registered in `Program.cs`). Admin/Manager succeed unconditionally, preserving 100% of pre-Module-12 behavior on every endpoint that used to be `[Authorize(Roles = "Admin,Manager")]` (Categories/SlaPolicies/AutomationRules/KnowledgeArticles write actions, plus the Audit Logs `GET`) - anyone else needs the specific permission. Permissions are snapshotted into a `permissions` JWT claim at login (`AuthController.IssueTokensAsync`), same staleness tradeoff already accepted for the `Role` claim itself - a reassignment takes effect on the affected user's next login/refresh, not instantly.

**Platform Auth** (`/api/platform/auth`) — Super Admin console (spec section 5, distinct from Module 5's workflow automation above)
- `POST /bootstrap` — `{ name, email, password }`. Only works once, while `PlatformUsers` is empty; creates the first `Owner`. 409 after that.
- `POST /login` — `{ email, password }`.

Both return `{ accessToken, accessTokenExpiresAtUtc, userId, email, role }`.
No refresh token here yet (platform sessions are short-lived by design for
this iteration — re-login when the 15 min access token expires).

**Platform Tenants** (`/api/platform/tenants`) — Super Admin section 5.1, requires a platform token
- `GET /`, `GET /{id}` — any platform role (`Owner`, `PlatformAdmin`, `SupportEngineer`, `BillingAdmin`, `ReadOnlyAnalyst`)
- `POST /` — `{ name, subdomain, planId, trialDays? }`. `Owner`/`PlatformAdmin` only.
- `POST /{id}/suspend`, `POST /{id}/reactivate` — `Owner`/`PlatformAdmin` only.

A platform token carries `scope=platform_admin` + `platform_role=<PlatformRole>`
and never a `tenant_id` claim, so it can't be used against `/api/tickets` etc.,
and a tenant AppUser's token can't be used here — completely separate
authorization surfaces by design.

**API Keys** (`/api/api-keys`) — Module 11 (Integrations & Public API), requires the `TenantStaff` policy **and** the `Admin` role
- An API key is a standing credential granting the versioned public REST API (below) broad, tenant-wide access to tickets — at least as sensitive as the RBAC surface itself, so this is Admin-only for both read and write, same reasoning as Custom Roles.
- `GET /` — list keys (name, key prefix, created/last-used/revoked timestamps) — the full key is never retrievable after creation. `POST /` — `{ name }`, returns the plaintext key **exactly once**; only a SHA-256 hash is ever persisted (same convention `RefreshToken` already uses). `DELETE /{id}` — soft-revoke (`RevokedAt` stamped, not a hard delete) so `CreatedAt`/`LastUsedAt` stay reviewable.

**Webhooks** (`/api/webhooks`) — Module 11, requires the `TenantStaff` policy **and** the `Admin` role
- `GET /` — list subscriptions (url, event, active state) — the signing secret is never retrievable after creation. `POST /` — `{ url, event }`; `url` must resolve to a public, non-private/loopback/link-local address over `https` (`WebhookUrlValidator`) or this 400s; returns the signing secret **exactly once**, stored server-side in reversible form (not hashed, unlike API keys) since every delivery needs to reuse it to compute an HMAC. `PATCH /{id}` — `{ isActive }` only; changing the URL or event is delete-and-recreate (same convention `SlaPolicy.Priority` uses for its own immutable-after-creation field), which also forces a fresh secret. `DELETE /{id}` — removes the subscription; past delivery logs stay, now pointing at a deleted subscription, same convention as `AutomationRuleLog`.
- `GET /{id}/logs` — most recent 50 delivery attempts (success, HTTP status, error, timestamp).
- Delivery: `WebhookService` POSTs a JSON payload to every active subscription matching the event, synchronously and inline within the same request that triggered it (ticket create/update) — same no-background-worker constraint as SLA breach detection and Notifications elsewhere in this codebase. Each request carries an `X-Tms-Signature` header: `HMAC-SHA256(secret, rawRequestBody)`, hex-encoded, so a receiver can verify the payload wasn't tampered with or spoofed. A 5-second per-delivery timeout keeps one slow/unreachable subscriber from stalling the triggering request indefinitely; a failed delivery is logged, never thrown back to the caller.
- **Known limitation, documented rather than silently shipped**: the private/loopback IP check runs once, at subscription-creation time, not re-checked before every delivery — a DNS-rebinding attack (a hostname that resolves to a safe public IP when the admin adds it, then to an internal IP later) would slip past this. Closing that fully needs either re-resolving and re-validating immediately before every single delivery, or an `HttpClient` that validates the connected socket's IP at connect time. Same category of tradeoff as Module 5's own "run webhook" automation action being cut entirely for this reason — this module accepts the risk at a narrower, admin-configured (not tenant-user-configured) surface instead.

**Public Tickets API** (`/api/v1/tickets`) — Module 11, a separate `X-Api-Key` authentication scheme (not a JWT)
- Send the plaintext key from `POST /api/api-keys` as an `X-Api-Key` header — no `Authorization: Bearer` involved. Authenticated via `ApiKeyAuthenticationHandler`, which also stamps `LastUsedAt` on the key. Independent authorization surface from staff/portal JWTs: neither can substitute for the other (a valid staff JWT can't call this API, and an API key can't call any staff/portal endpoint).
- Deliberately a narrower surface than the staff `/api/tickets`: no subject/description edits, no comments, no CSAT — scoped to exactly the spec's done-when bar. `GET /`, `GET /{id}` — read tickets (filterable by `status`). `POST /` — `{ subject, description?, priority?, categoryId?, assigneeId? }`; same server-side SLA-policy matching as every other intake path; fires the same automation-rule/notification pipeline as the staff and portal create endpoints; audit-logged with an actor label of `"API key: {name}"` (no `AppUser`/`PortalCustomer` to attribute it to — `RequesterId`/`CustomerId` stay null). `PATCH /{id}` — `{ status?, priority?, assigneeId? }`; a status change here (or one made through the staff UI) fires the `TicketStatusChanged` webhook event above.
- Versioned (`v1`) so the response shape can evolve later without breaking an existing integration.
- Implementation note: `"ApiKey"` is a non-default authentication scheme, only invoked on demand inside the authorization middleware — which runs *after* `TenantResolutionMiddleware` in the pipeline. Rather than depend on pipeline ordering (which every staff/portal request also relies on for the default JwtBearer scheme), `ApiKeyAuthenticationHandler` sets `ITenantContext.TenantId` itself, directly, the moment it resolves the key, instead of leaving it to be picked up from a claim later.

**Scoped out of this module**: native chat-app integrations (Slack/Teams/Jira) and full scoped-OAuth app registrations — both need a real third-party app registration and external accounts to build against, not something a coding pass alone can stand up.

## Multi-tenancy

- Every tenant-scoped table has a `TenantId` column + composite index.
- `TmsDbContext` applies a global EF Core query filter per table via `ITenantContext`.
- `TenantResolutionMiddleware` resolves the tenant from the JWT `tenant_id` claim
  (authenticated requests). `AuthController` resolves it directly from the
  tenant slug for the pre-auth register/login/refresh calls, using the same
  `ITenantContext`.
- `docs/rls-policies.sql` adds Postgres Row-Level Security as a second layer
  (apply it after running migrations — it references tables that must already exist).
- Roles are the fixed `Role` enum (`Admin`, `Manager`, `Agent`, `ReadOnly`),
  now extended (Module 12) by an optional per-tenant `CustomRole` a user can
  additionally hold, granting specific module permissions on top of their
  base role — see Custom Roles above. Team/group structures for ticket
  routing (also listed under Module 12 in the spec) are not built in this
  iteration.
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
  table/auth scheme (spec section 5), never reachable with a tenant JWT. The
  `PlatformAdmin` policy allows any platform role (read access); `PlatformManage`
  additionally requires `platform_role` to be `Owner` or `PlatformAdmin` (write access).

## Deploy

`.github/workflows/azure-deploy.yml` builds and deploys to Azure App Service
on push to `main`. Requires `AZURE_WEBAPP_PUBLISH_PROFILE` repo secret, plus
`ConnectionStrings__TmsDb` and `Auth__SigningKey` set in the App Service's
own configuration (Azure Portal → Configuration → Application settings) —
never in source control.
