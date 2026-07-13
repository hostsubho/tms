CREATE EXTENSION IF NOT EXISTS pgcrypto; -- for gen_random_uuid()

-- Default plans referenced by Tenant.PlanId. No admin UI for managing these
-- yet (Module 5.2 - Plans & Billing Administration is future work), so this
-- is the only way to add/change plans for now. Run once per database.

INSERT INTO "Plans" ("Id", "Name", "MaxAgents", "MaxTicketsPerMonth", "PriceMonthly") VALUES
    (gen_random_uuid(), 'Free Trial', 3, 200, 0),
    (gen_random_uuid(), 'Starter', 10, 1000, 49),
    (gen_random_uuid(), 'Growth', 50, 10000, 199),
    (gen_random_uuid(), 'Enterprise', 500, 100000, 999);

-- After running this, GET /api/plans lists them with their generated ids -
-- use one of those ids as planId in POST /api/onboarding/signup.
