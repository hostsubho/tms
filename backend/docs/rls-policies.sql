-- Row-Level Security policies for the Neon "TMS" database.
-- Second layer of tenant isolation behind the EF Core global query filters
-- in Tms.Api/Data/TmsDbContext.cs. Run once per tenant table.

ALTER TABLE "Tickets" ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_tickets ON "Tickets"
    USING ("TenantId" = current_setting('app.tenant_id')::uuid);

ALTER TABLE "Users" ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_users ON "Users"
    USING ("TenantId" = current_setting('app.tenant_id')::uuid);

ALTER TABLE "SlaPolicies" ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_sla_policies ON "SlaPolicies"
    USING ("TenantId" = current_setting('app.tenant_id')::uuid);

-- The application sets `app.tenant_id` per request/connection, e.g.:
-- SELECT set_config('app.tenant_id', @tenantId, false);
