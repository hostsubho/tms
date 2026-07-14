"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { platformAuth } from "@/lib/auth";

interface Invoice {
  id: string;
  amountDue: number;
  amountPaid: number;
  currency: string;
  status: string;
  periodStart: string;
  periodEnd: string;
  hostedInvoiceUrl: string | null;
}

interface BillingCredit {
  id: string;
  amountCents: number;
  reason: string;
  createdAt: string;
}

interface Overview {
  tenantId: string;
  tenantName: string;
  planId: string;
  planName: string;
  status: string;
  hasBillingSetUp: boolean;
  currentPeriodEnd: string | null;
  invoices: Invoice[];
  credits: BillingCredit[];
}

interface Plan {
  id: string;
  name: string;
  priceMonthly: number;
}

interface ModuleFlag {
  moduleKey: string;
  enabled: boolean;
  monthlyCostCents: number | null;
}

interface ModuleLicensing {
  modules: ModuleFlag[];
  basePlanPriceMonthlyCents: number;
  suggestedTotalCents: number;
  totalOverrideCents: number | null;
  effectiveTotalCents: number;
}

// "Module Licensing" - client customization & module cost negotiation.
// Friendly labels for the fixed ModuleKey enum on the backend (see
// Models/TenantModuleFlag.cs) - kept as a plain lookup, not fetched, since
// the set of modules is small and fixed in code on both sides.
const MODULE_LABELS: Record<string, string> = {
  SlaPolicies: "SLA Policies",
  Automation: "Automation Rules",
  KnowledgeBase: "Knowledge Base",
  AdvancedReports: "Advanced Reports & Analytics",
  Cmdb: "Asset Management / CMDB",
  IntegrationsApi: "Integrations & Public API",
  CustomRoles: "Custom Roles / RBAC",
  Sso: "SSO (SAML/OIDC)",
};

function centsToDollarsInput(cents: number | null): string {
  return cents === null ? "" : (cents / 100).toString();
}

const STATUS_STYLES: Record<string, string> = {
  Trial: "bg-blue-100 text-blue-700",
  Active: "bg-green-100 text-green-700",
  PastDue: "bg-amber-100 text-amber-700",
  Suspended: "bg-red-100 text-red-700",
  Churned: "bg-zinc-200 text-zinc-500",
};

// Applying a credit or overriding a plan directly moves the tenant's real
// billing state (or Stripe's ledger) - restricted to the backend's
// PlatformBilling policy (Owner/PlatformAdmin/BillingAdmin), narrower than
// PlatformAdmin's "any role" used for the read-only overview below.
const BILLING_ROLES = new Set(["Owner", "PlatformAdmin", "BillingAdmin"]);

// Enabling/disabling a module is a functionality decision for the client,
// not purely billing - matches the backend's PlatformManage policy on
// UpdateModuleFlag (narrower than PlatformBilling, excludes BillingAdmin).
const MODULE_MANAGE_ROLES = new Set(["Owner", "PlatformAdmin"]);

export default function TenantBillingPage() {
  const router = useRouter();
  const params = useParams<{ tenantId: string }>();
  const tenantId = params.tenantId;

  const [role, setRole] = useState<string | null>(null);
  const [data, setData] = useState<Overview | null>(null);
  const [plans, setPlans] = useState<Plan[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const [creditAmount, setCreditAmount] = useState("");
  const [creditReason, setCreditReason] = useState("");
  const [applyingCredit, setApplyingCredit] = useState(false);

  const [overridePlanId, setOverridePlanId] = useState("");
  const [overriding, setOverriding] = useState(false);

  const [licensing, setLicensing] = useState<ModuleLicensing | null>(null);
  const [moduleCostInputs, setModuleCostInputs] = useState<Record<string, string>>({});
  const [savingModule, setSavingModule] = useState<string | null>(null);
  const [totalOverrideInput, setTotalOverrideInput] = useState("");
  const [savingTotalOverride, setSavingTotalOverride] = useState(false);

  const load = useCallback(async (token: string) => {
    try {
      const overview = await apiFetch<Overview>(`/api/platform/billing/tenants/${tenantId}`, { token });
      setData(overview);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        platformAuth.clear();
        router.replace("/admin/login");
        return;
      }
      setError(err instanceof ApiError ? err.message : "Couldn't load billing overview.");
    }
  }, [router, tenantId]);

  const loadLicensing = useCallback(async (token: string) => {
    try {
      const result = await apiFetch<ModuleLicensing>(`/api/platform/tenants/${tenantId}/module-flags`, { token });
      setLicensing(result);
      setModuleCostInputs(
        Object.fromEntries(result.modules.map((m) => [m.moduleKey, centsToDollarsInput(m.monthlyCostCents)])),
      );
      setTotalOverrideInput(centsToDollarsInput(result.totalOverrideCents));
    } catch {
      // Non-fatal - the rest of the billing page still renders.
    }
  }, [tenantId]);

  useEffect(() => {
    const auth = platformAuth.get();
    if (!auth) {
      router.replace("/admin/login");
      return;
    }
    setRole(auth.role);
    load(auth.accessToken);
    loadLicensing(auth.accessToken);

    apiFetch<Plan[]>("/api/platform/plans", { token: auth.accessToken })
      .then((data) => {
        setPlans(data);
        if (data.length > 0) setOverridePlanId(data[0].id);
      })
      .catch(() => {
        // Non-fatal: the override dropdown just won't have options.
      });
  }, [router, load, loadLicensing]);

  const canManageBilling = role !== null && BILLING_ROLES.has(role);
  const canManageModules = role !== null && MODULE_MANAGE_ROLES.has(role);

  async function handleToggleModule(m: ModuleFlag) {
    const auth = platformAuth.get();
    if (!auth) return;

    setActionError(null);
    setSavingModule(m.moduleKey);
    try {
      const dollars = moduleCostInputs[m.moduleKey];
      const monthlyCostCents = dollars === "" || dollars === undefined ? null : Math.round(Number(dollars) * 100);
      await apiFetch(`/api/platform/tenants/${tenantId}/module-flags/${m.moduleKey}`, {
        method: "PUT",
        token: auth.accessToken,
        body: JSON.stringify({ enabled: !m.enabled, monthlyCostCents }),
      });
      await loadLicensing(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't update module.");
    } finally {
      setSavingModule(null);
    }
  }

  async function handleSaveModuleCost(m: ModuleFlag) {
    const auth = platformAuth.get();
    if (!auth) return;

    const dollars = moduleCostInputs[m.moduleKey];
    if (dollars !== "" && (!Number.isFinite(Number(dollars)) || Number(dollars) < 0)) {
      setActionError("Enter a non-negative dollar amount, or leave it blank to clear the price.");
      return;
    }

    setActionError(null);
    setSavingModule(m.moduleKey);
    try {
      const monthlyCostCents = dollars === "" ? null : Math.round(Number(dollars) * 100);
      await apiFetch(`/api/platform/tenants/${tenantId}/module-flags/${m.moduleKey}`, {
        method: "PUT",
        token: auth.accessToken,
        body: JSON.stringify({ enabled: m.enabled, monthlyCostCents }),
      });
      await loadLicensing(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't update module price.");
    } finally {
      setSavingModule(null);
    }
  }

  async function handleSaveTotalOverride(e: React.FormEvent) {
    e.preventDefault();
    const auth = platformAuth.get();
    if (!auth) return;

    if (totalOverrideInput !== "" && (!Number.isFinite(Number(totalOverrideInput)) || Number(totalOverrideInput) < 0)) {
      setActionError("Enter a non-negative dollar amount for the negotiated total.");
      return;
    }

    setActionError(null);
    setSavingTotalOverride(true);
    try {
      const totalOverrideCents = totalOverrideInput === "" ? null : Math.round(Number(totalOverrideInput) * 100);
      await apiFetch(`/api/platform/tenants/${tenantId}/billing-total-override`, {
        method: "PUT",
        token: auth.accessToken,
        body: JSON.stringify({ totalOverrideCents }),
      });
      await loadLicensing(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't update the negotiated total.");
    } finally {
      setSavingTotalOverride(false);
    }
  }

  async function handleApplyCredit(e: React.FormEvent) {
    e.preventDefault();
    const auth = platformAuth.get();
    if (!auth) return;

    const dollars = Number(creditAmount);
    if (!Number.isFinite(dollars) || dollars <= 0) {
      setActionError("Enter a positive dollar amount.");
      return;
    }

    setActionError(null);
    setApplyingCredit(true);
    try {
      await apiFetch(`/api/platform/billing/tenants/${tenantId}/credit`, {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({ amountCents: Math.round(dollars * 100), reason: creditReason }),
      });
      setCreditAmount("");
      setCreditReason("");
      await load(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't apply credit.");
    } finally {
      setApplyingCredit(false);
    }
  }

  async function handleOverridePlan(e: React.FormEvent) {
    e.preventDefault();
    const auth = platformAuth.get();
    if (!auth || !overridePlanId) return;

    setActionError(null);
    setOverriding(true);
    try {
      await apiFetch(`/api/platform/billing/tenants/${tenantId}/override-plan`, {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({ planId: overridePlanId }),
      });
      await load(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't override plan.");
    } finally {
      setOverriding(false);
    }
  }

  return (
    <main className="min-h-screen bg-zinc-950 text-zinc-100">
      <header className="border-b border-zinc-800 px-8 py-4 flex items-center justify-between">
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-zinc-500">TMS Platform</p>
          <h1 className="text-lg font-semibold">{data ? data.tenantName : "Tenant billing"}</h1>
        </div>
        <button
          onClick={() => router.push("/admin/tenants")}
          className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm text-zinc-300 hover:bg-zinc-900"
        >
          Back to tenants
        </button>
      </header>

      <div className="p-8 space-y-6 max-w-3xl">
        {error && <p className="text-sm text-red-400">{error}</p>}
        {actionError && <p className="text-sm text-red-400">{actionError}</p>}
        {!data && !error && <p className="text-sm text-zinc-500">Loading…</p>}

        {data && (
          <>
            <section className="rounded-lg border border-zinc-800 bg-zinc-900 p-6">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-xs font-medium uppercase text-zinc-500">Plan</p>
                  <p className="mt-1 text-xl font-semibold">{data.planName}</p>
                  <p className="text-sm text-zinc-500">
                    {data.hasBillingSetUp ? "Stripe customer on file" : "No Stripe customer yet"}
                  </p>
                </div>
                <span
                  className={`inline-block rounded-full px-2.5 py-1 text-xs ${
                    STATUS_STYLES[data.status] ?? "bg-zinc-800 text-zinc-400"
                  }`}
                >
                  {data.status}
                </span>
              </div>
              {data.currentPeriodEnd && (
                <p className="mt-3 text-sm text-zinc-500">
                  Current period ends {new Date(data.currentPeriodEnd).toLocaleDateString()}
                </p>
              )}
            </section>

            {licensing && (
              <section className="rounded-lg border border-zinc-800 bg-zinc-900 p-6">
                <h2 className="mb-1 text-sm font-semibold">Module licensing</h2>
                <p className="mb-4 text-xs text-zinc-500">
                  Turn optional modules on or off for this client and negotiate a monthly price for each -
                  running cost updates automatically as modules are enabled, and can be overridden below.
                </p>

                <table className="w-full text-sm">
                  <thead className="text-left text-xs font-medium uppercase text-zinc-500">
                    <tr>
                      <th className="py-2">Module</th>
                      <th className="py-2">Enabled</th>
                      <th className="py-2">Price / mo</th>
                      <th className="py-2"></th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-zinc-800">
                    {licensing.modules.map((m) => (
                      <tr key={m.moduleKey}>
                        <td className="py-2 text-zinc-300">{MODULE_LABELS[m.moduleKey] ?? m.moduleKey}</td>
                        <td className="py-2">
                          <button
                            disabled={!canManageModules || savingModule === m.moduleKey}
                            onClick={() => handleToggleModule(m)}
                            className={`rounded-full px-2.5 py-1 text-xs disabled:opacity-50 ${
                              m.enabled ? "bg-green-900/50 text-green-400" : "bg-zinc-800 text-zinc-500"
                            }`}
                          >
                            {m.enabled ? "On" : "Off"}
                          </button>
                        </td>
                        <td className="py-2">
                          <div className="flex items-center gap-1">
                            <span className="text-zinc-500">$</span>
                            <input
                              type="number"
                              min={0}
                              step="0.01"
                              placeholder="unpriced"
                              disabled={!canManageModules}
                              value={moduleCostInputs[m.moduleKey] ?? ""}
                              onChange={(e) =>
                                setModuleCostInputs((prev) => ({ ...prev, [m.moduleKey]: e.target.value }))
                              }
                              className="w-24 rounded-md border border-zinc-700 bg-zinc-950 px-2 py-1 text-sm focus:border-zinc-500 focus:outline-none disabled:opacity-50"
                            />
                          </div>
                        </td>
                        <td className="py-2 text-right">
                          {canManageModules && (
                            <button
                              disabled={savingModule === m.moduleKey}
                              onClick={() => handleSaveModuleCost(m)}
                              className="text-xs text-zinc-400 hover:underline disabled:opacity-50"
                            >
                              Save price
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>

                <div className="mt-6 grid grid-cols-1 gap-4 border-t border-zinc-800 pt-4 sm:grid-cols-2">
                  <div className="space-y-1 text-sm">
                    <p className="text-zinc-500">
                      Base plan: <span className="text-zinc-300">${(licensing.basePlanPriceMonthlyCents / 100).toFixed(2)}/mo</span>
                    </p>
                    <p className="text-zinc-500">
                      Suggested total (plan + enabled modules):{" "}
                      <span className="text-zinc-300">${(licensing.suggestedTotalCents / 100).toFixed(2)}/mo</span>
                    </p>
                    <p className="font-medium text-zinc-100">
                      Effective total: ${(licensing.effectiveTotalCents / 100).toFixed(2)}/mo
                      {licensing.totalOverrideCents !== null && (
                        <span className="ml-2 rounded-full bg-amber-900/50 px-2 py-0.5 text-xs text-amber-400">
                          negotiated
                        </span>
                      )}
                    </p>
                  </div>

                  {canManageBilling && (
                    <form onSubmit={handleSaveTotalOverride} className="space-y-2">
                      <label className="block text-xs text-zinc-500">
                        Override the final total (leave blank to use the suggested total)
                      </label>
                      <div className="flex gap-2">
                        <div className="flex flex-1 items-center gap-1 rounded-md border border-zinc-700 bg-zinc-950 px-2">
                          <span className="text-zinc-500">$</span>
                          <input
                            type="number"
                            min={0}
                            step="0.01"
                            value={totalOverrideInput}
                            onChange={(e) => setTotalOverrideInput(e.target.value)}
                            className="w-full bg-transparent px-1 py-1.5 text-sm focus:outline-none"
                          />
                        </div>
                        <button
                          type="submit"
                          disabled={savingTotalOverride}
                          className="rounded-md bg-white px-3 py-1.5 text-sm font-medium text-zinc-900 hover:bg-zinc-200 disabled:opacity-50"
                        >
                          {savingTotalOverride ? "Saving…" : "Save"}
                        </button>
                      </div>
                    </form>
                  )}
                </div>
              </section>
            )}

            {canManageBilling && (
              <section className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <form onSubmit={handleApplyCredit} className="rounded-lg border border-zinc-800 bg-zinc-900 p-6 space-y-3">
                  <h2 className="text-sm font-semibold">Apply billing credit</h2>
                  <p className="text-xs text-zinc-500">
                    Requires an existing Stripe customer. Capped at $50,000 per credit.
                  </p>
                  <div>
                    <label className="block text-sm text-zinc-400 mb-1">Amount (USD)</label>
                    <input
                      type="number"
                      min={0}
                      step="0.01"
                      value={creditAmount}
                      onChange={(e) => setCreditAmount(e.target.value)}
                      disabled={!data.hasBillingSetUp}
                      className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none disabled:opacity-50"
                    />
                  </div>
                  <div>
                    <label className="block text-sm text-zinc-400 mb-1">Reason</label>
                    <input
                      required
                      value={creditReason}
                      onChange={(e) => setCreditReason(e.target.value)}
                      disabled={!data.hasBillingSetUp}
                      className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none disabled:opacity-50"
                    />
                  </div>
                  <button
                    type="submit"
                    disabled={applyingCredit || !data.hasBillingSetUp}
                    className="rounded-md bg-white px-4 py-2 text-sm font-medium text-zinc-900 hover:bg-zinc-200 disabled:opacity-50"
                  >
                    {applyingCredit ? "Applying…" : "Apply credit"}
                  </button>
                </form>

                <form onSubmit={handleOverridePlan} className="rounded-lg border border-zinc-800 bg-zinc-900 p-6 space-y-3">
                  <h2 className="text-sm font-semibold">Manually override plan</h2>
                  <p className="text-xs text-zinc-500">
                    Sets the plan directly with no Stripe charge - for negotiated/comped deals.
                  </p>
                  <div>
                    <label className="block text-sm text-zinc-400 mb-1">Plan</label>
                    <select
                      value={overridePlanId}
                      onChange={(e) => setOverridePlanId(e.target.value)}
                      className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                    >
                      {plans.map((p) => (
                        <option key={p.id} value={p.id}>
                          {p.name} {p.priceMonthly > 0 ? `($${p.priceMonthly}/mo)` : "(Free)"}
                        </option>
                      ))}
                    </select>
                  </div>
                  <button
                    type="submit"
                    disabled={overriding || !overridePlanId}
                    className="rounded-md border border-zinc-700 px-4 py-2 text-sm text-zinc-200 hover:bg-zinc-800 disabled:opacity-50"
                  >
                    {overriding ? "Applying…" : "Override plan"}
                  </button>
                </form>
              </section>
            )}

            <section className="rounded-lg border border-zinc-800 bg-zinc-900 p-6">
              <h2 className="mb-4 text-sm font-semibold">Invoices</h2>
              {data.invoices.length === 0 ? (
                <p className="text-sm text-zinc-500">No invoices yet.</p>
              ) : (
                <table className="w-full text-sm">
                  <thead className="text-left text-xs font-medium uppercase text-zinc-500">
                    <tr>
                      <th className="py-2">Period</th>
                      <th className="py-2">Amount</th>
                      <th className="py-2">Status</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-zinc-800">
                    {data.invoices.map((inv) => (
                      <tr key={inv.id}>
                        <td className="py-2 text-zinc-400">
                          {new Date(inv.periodStart).toLocaleDateString()} – {new Date(inv.periodEnd).toLocaleDateString()}
                        </td>
                        <td className="py-2">
                          {inv.currency.toUpperCase()} {inv.amountPaid > 0 ? inv.amountPaid.toFixed(2) : inv.amountDue.toFixed(2)}
                        </td>
                        <td className="py-2 text-zinc-400">{inv.status}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </section>

            <section className="rounded-lg border border-zinc-800 bg-zinc-900 p-6">
              <h2 className="mb-4 text-sm font-semibold">Credit history</h2>
              {data.credits.length === 0 ? (
                <p className="text-sm text-zinc-500">No credits applied.</p>
              ) : (
                <ul className="space-y-2 text-sm">
                  {data.credits.map((c) => (
                    <li key={c.id} className="flex items-center justify-between border-b border-zinc-800 pb-2">
                      <span className="text-zinc-300">{c.reason}</span>
                      <span className="text-zinc-500">
                        ${(c.amountCents / 100).toFixed(2)} · {new Date(c.createdAt).toLocaleDateString()}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </>
        )}
      </div>
    </main>
  );
}
