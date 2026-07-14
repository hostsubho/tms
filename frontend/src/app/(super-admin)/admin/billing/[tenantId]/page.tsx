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

  useEffect(() => {
    const auth = platformAuth.get();
    if (!auth) {
      router.replace("/admin/login");
      return;
    }
    setRole(auth.role);
    load(auth.accessToken);

    apiFetch<Plan[]>("/api/platform/plans", { token: auth.accessToken })
      .then((data) => {
        setPlans(data);
        if (data.length > 0) setOverridePlanId(data[0].id);
      })
      .catch(() => {
        // Non-fatal: the override dropdown just won't have options.
      });
  }, [router, load]);

  const canManageBilling = role !== null && BILLING_ROLES.has(role);

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
