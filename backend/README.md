# TMS Backend (.NET 8 Web API)

Multi-tenant API for the TMS ticket management SaaS. See `/docs` at the repo
root for the full module-wise feature spec PDF.

## Local dev

Requires the .NET 8 SDK and a Postgres instance (or a Neon branch connection string).

```
dotnet restore
dotnet run --project src/Tms.Api
```

Set `ConnectionStrings:TmsDb` in `appsettings.Development.json` or via
`ConnectionStrings__TmsDb` env var — point it at a Neon dev branch, not the
production `TMS` database.

## Multi-tenancy

- Every tenant-scoped table has a `TenantId` column + composite index.
- `TmsDbContext` applies a global EF Core query filter per table via `ITenantContext`.
- `TenantResolutionMiddleware` resolves the tenant from the JWT `tenant_id` claim
  (browser sessions) or `X-Tenant-Slug` header (API clients).
- `docs/rls-policies.sql` adds Postgres Row-Level Security as a second layer.
- Super Admin endpoints (`/api/platform/*`) require the `PlatformAdmin` policy
  and must never be reachable with a regular tenant JWT.

## Deploy

`.github/workflows/azure-deploy.yml` builds and deploys to Azure App Service
on push to `main`. Requires `AZURE_WEBAPP_PUBLISH_PROFILE` repo secret.
