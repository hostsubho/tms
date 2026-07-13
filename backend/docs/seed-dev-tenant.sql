CREATE EXTENSION IF NOT EXISTS pgcrypto; -- for gen_random_uuid()

-- One test tenant so you can exercise POST /api/auth/register and
-- /api/auth/login locally before the Super Admin "Create Tenant" endpoint
-- (Module 5.1) exists. Run this once against your dev database after
-- migrations have been applied.

INSERT INTO "Tenants" ("Id", "Name", "Subdomain", "PlanId", "Status", "CreatedAt", "TrialEndsAt")
VALUES (
    gen_random_uuid(),
    'Acme Test Co',
    'acme',
    gen_random_uuid(),
    'Trial',
    now(),
    now() + interval '14 days'
);

-- Then: POST /api/auth/register  { "tenantSlug": "acme", "email": "you@acme.test", "password": "..." }
-- The first user registered against a tenant is auto-promoted to Role.Admin.
