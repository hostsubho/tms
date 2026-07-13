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
`SlaPolicies`, and `RefreshTokens` in the Neon `tms` database, each with the
`TenantId` column and composite indexes described in the feature spec.

After that, optionally run `docs/seed-dev-tenant.sql` against the same
database (`psql "<connection string>" -f docs/seed-dev-tenant.sql`) to create
one test tenant (`acme`) so you can call `/api/auth/register` locally —
there's no self-serve tenant signup or Super Admin "create tenant" endpoint
yet (Module 2 / Module 5.1), so a tenant has to exist before anyone can
register into it.

## Run it

```
dotnet run --project src/Tms.Api
```

Swagger UI is available at `/swagger` in Development.

## API surface (this iteration)

**Auth** (`/api/auth`) — Module 1
- `POST /register` — `{ tenantSlug, email, password }`. First user for a tenant becomes `Admin`, others default to `Agent`.
- `POST /login` — `{ tenantSlug, email, password }`. Blocked if the tenant is `Suspended`/`Churned`.
- `POST /refresh` — `{ refreshToken }`. Rotates the refresh token on every use.
- `POST /logout` — `{ refreshToken }`. Revokes it.

All return `{ accessToken, accessTokenExpiresAtUtc, refreshToken, userId, email, role }`.
Access tokens are short-lived JWTs (15 min default); send as `Authorization: Bearer <token>`.

**Tickets** (`/api/tickets`) — Module 3, requires auth
- `GET /` — filterable by `status`, `assigneeId`
- `GET /{id}`
- `POST /` — creates a ticket; `TenantId`/`RequesterId` are always set server-side, never from the request body
- `PATCH /{id}` — partial update (status, priority, assignee, etc.)
- `GET /{id}/comments`, `POST /{id}/comments` — `isInternal` flag separates agent notes from customer-visible replies

**Categories** (`/api/categories`)
- `GET /`
- `POST /` — restricted to `Admin`/`Manager` roles

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
- Super Admin endpoints (`/api/platform/*`) are not built yet — they'll need
  a separate `PlatformUser` auth scheme, never reachable with a tenant JWT.
  The `PlatformAdmin` authorization policy in `Program.cs` is a placeholder
  for that.

## Deploy

`.github/workflows/azure-deploy.yml` builds and deploys to Azure App Service
on push to `main`. Requires `AZURE_WEBAPP_PUBLISH_PROFILE` repo secret, plus
`ConnectionStrings__TmsDb` and `Auth__SigningKey` set in the App Service's
own configuration (Azure Portal → Configuration → Application settings) —
never in source control.
