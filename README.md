# TMS — Enterprise Ticket Management System (SaaS)

Multi-tenant ticket management platform.

- `frontend/` — Node.js (Next.js) app, deployed on Vercel
- `backend/` — .NET 8 Web API, deployed on Azure App Service
- `docs/TMS_Enterprise_Feature_Spec.pdf` — full module-wise feature spec (super admin, tenant management, architecture, roadmap). Use this as the implementation reference when working module-by-module with Claude.

## Architecture summary
- Database: single Neon Postgres database `TMS`, shared schema, `TenantId` column + EF Core global query filters + Postgres RLS for tenant isolation.
- Frontend and backend are deployed independently; frontend talks to the backend over `NEXT_PUBLIC_API_BASE_URL`.

See the spec PDF for the full module list, super admin capabilities, and phased implementation roadmap.
