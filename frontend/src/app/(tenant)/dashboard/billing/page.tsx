"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface Subscription {
  planId: string;
  planName: string;
  priceMonthly: number;
  status: string;
  currentPeriodEnd: string | null;
  hasBillingSetUp: boolean;
}

interface Invoice {
  id: string;
  stripeInvoiceId: string;
  amountDue: number;
  amountPaid: number;
  currency: string;
  status: string;
  periodStart: string;
  periodEnd: string;
  hostedInvoiceUrl: string | null;
  createdAt: string;
}

interface Plan {
  id: string;
  name: string;
  maxAgents: number;
  maxTicketsPerMonth: number;
  priceMonthly: number;
}

const STATUS_STYLES: Record<string, string> = {
  Trial: "bg-blue-100 text-blue-700",
  Active: "bg-green-100 text-green-700",
  PastDue: "bg-amber-100 text-amber-700",
  Suspended: "bg-red-100 text-red-700",
  Churned: "bg-zinc-200 text-zinc-500",
};

const INVOICE_STATUS_STYLES: Record<string, string> = {
  Paid: "bg-green-100 text-green-700",
  Open: "bg-amber-100 text-amber-700",
  Uncollectible: "bg-red-100 text-red-700",
  Void: "bg-zinc-100 text-zinc-500",
};

export default function BillingPage() {
  const router = useRouter();
  const [role, setRole] = useState<string | null>(null);
  const [subscription, setSubscription] = useState<Subscription | null>(null);
  const [invoices, setInvoices] = useState<Invoice[] | null>(null);
  const [plans, setPlans] = useState<Plan[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const [selectedPlanId, setSelectedPlanId] = useState("");
  const [changing, setChanging] = useState(false);
  const [openingPortal, setOpeningPortal] = useState(false);

  const load = useCallback(async (token: string) => {
    try {
      const [sub, invs] = await Promise.all([
        apiFetch<Subscription>("/api/billing/subscription", { token }),
        apiFetch<Invoice[]>("/api/billing/invoices", { token }),
      ]);
      setSubscription(sub);
      setInvoices(invs);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        tenantAuth.clear();
        router.replace("/login");
        return;
      }
      setError(err instanceof ApiError ? err.message : "Couldn't load billing information.");
    }
  }, [router]);

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }
    setRole(auth.role);
    load(auth.accessToken);

    apiFetch<Plan[]>("/api/plans")
      .then((data) => {
        setPlans(data);
        if (data.length > 0) setSelectedPlanId(data[0].id);
      })
      .catch(() => {
        // Non-fatal: the plan picker just won't have options.
      });
  }, [router, load]);

  async function handleChangePlan(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth || !selectedPlanId) return;

    setActionError(null);
    setChanging(true);
    try {
      const returnUrl = window.location.href;
      const result = await apiFetch<{ requiresRedirect: boolean; redirectUrl: string | null; updatedPlanId: string | null }>(
        "/api/billing/change-plan",
        {
          method: "POST",
          token: auth.accessToken,
          body: JSON.stringify({ planId: selectedPlanId, successUrl: returnUrl, cancelUrl: returnUrl }),
        }
      );
      if (result.requiresRedirect && result.redirectUrl) {
        // Brand-new paid subscription - hand off to Stripe Checkout. The
        // plan doesn't actually change locally until Stripe confirms
        // payment via webhook, so there's nothing more to do here.
        window.location.href = result.redirectUrl;
        return;
      }
      await load(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't change plan.");
    } finally {
      setChanging(false);
    }
  }

  async function handleManageBilling() {
    const auth = tenantAuth.get();
    if (!auth) return;

    setActionError(null);
    setOpeningPortal(true);
    try {
      const result = await apiFetch<{ url: string }>("/api/billing/portal-session", {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({ returnUrl: window.location.href }),
      });
      window.location.href = result.url;
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't open the billing portal.");
      setOpeningPortal(false);
    }
  }

  const isAdmin = role === "Admin";

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4 flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold">Billing</h1>
          <p className="text-sm text-zinc-500">Plan, subscription, and invoices</p>
        </div>
        <button
          onClick={() => router.push("/dashboard")}
          className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm text-zinc-700 hover:bg-zinc-100"
        >
          Back to tickets
        </button>
      </header>

      <div className="p-8 space-y-6 max-w-3xl">
        {error && <p className="text-sm text-red-600">{error}</p>}
        {actionError && <p className="text-sm text-red-600">{actionError}</p>}
        {!subscription && !error && <p className="text-sm text-zinc-500">Loading…</p>}

        {subscription && (
          <section className="rounded-lg border border-zinc-200 bg-white p-6">
            <div className="flex items-center justify-between">
              <div>
                <p className="text-xs font-medium uppercase text-zinc-500">Current plan</p>
                <p className="mt-1 text-xl font-semibold text-zinc-900">{subscription.planName}</p>
                <p className="text-sm text-zinc-500">
                  {subscription.priceMonthly > 0 ? `$${subscription.priceMonthly}/mo` : "Free"}
                </p>
              </div>
              <span
                className={`inline-block rounded-full px-2.5 py-1 text-xs ${
                  STATUS_STYLES[subscription.status] ?? "bg-zinc-100 text-zinc-600"
                }`}
              >
                {subscription.status}
              </span>
            </div>

            {subscription.currentPeriodEnd && (
              <p className="mt-3 text-sm text-zinc-500">
                Current period ends {new Date(subscription.currentPeriodEnd).toLocaleDateString()}
              </p>
            )}

            {subscription.status === "PastDue" && (
              <p className="mt-3 rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-800">
                A recent payment failed. Update your card via &quot;Manage billing&quot; below to avoid suspension.
              </p>
            )}

            {isAdmin && (
              <div className="mt-5 flex flex-wrap items-center gap-3 border-t border-zinc-100 pt-4">
                <form onSubmit={handleChangePlan} className="flex items-center gap-2">
                  <select
                    value={selectedPlanId}
                    onChange={(e) => setSelectedPlanId(e.target.value)}
                    className="rounded-md border border-zinc-300 px-2 py-1.5 text-sm"
                  >
                    {plans.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name} {p.priceMonthly > 0 ? `($${p.priceMonthly}/mo)` : "(Free)"}
                      </option>
                    ))}
                  </select>
                  <button
                    type="submit"
                    disabled={changing || !selectedPlanId || selectedPlanId === subscription.planId}
                    className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
                  >
                    {changing ? "Working…" : "Change plan"}
                  </button>
                </form>

                {subscription.hasBillingSetUp && (
                  <button
                    onClick={handleManageBilling}
                    disabled={openingPortal}
                    className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm text-zinc-700 hover:bg-zinc-100 disabled:opacity-50"
                  >
                    {openingPortal ? "Opening…" : "Manage billing"}
                  </button>
                )}
              </div>
            )}
          </section>
        )}

        {invoices && (
          <section className="rounded-lg border border-zinc-200 bg-white p-6">
            <h2 className="mb-4 text-sm font-semibold text-zinc-900">Invoice history</h2>
            {invoices.length === 0 ? (
              <p className="text-sm text-zinc-500">No invoices yet.</p>
            ) : (
              <table className="w-full text-sm">
                <thead className="text-left text-xs font-medium uppercase text-zinc-500">
                  <tr>
                    <th className="py-2">Period</th>
                    <th className="py-2">Amount</th>
                    <th className="py-2">Status</th>
                    <th className="py-2"></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-100">
                  {invoices.map((inv) => (
                    <tr key={inv.id}>
                      <td className="py-2 text-zinc-600">
                        {new Date(inv.periodStart).toLocaleDateString()} – {new Date(inv.periodEnd).toLocaleDateString()}
                      </td>
                      <td className="py-2 font-medium text-zinc-900">
                        {inv.currency.toUpperCase()} {inv.amountPaid > 0 ? inv.amountPaid.toFixed(2) : inv.amountDue.toFixed(2)}
                      </td>
                      <td className="py-2">
                        <span
                          className={`inline-block rounded-full px-2 py-0.5 text-xs ${
                            INVOICE_STATUS_STYLES[inv.status] ?? "bg-zinc-100 text-zinc-600"
                          }`}
                        >
                          {inv.status}
                        </span>
                      </td>
                      <td className="py-2 text-right">
                        {inv.hostedInvoiceUrl && (
                          <a
                            href={inv.hostedInvoiceUrl}
                            target="_blank"
                            rel="noreferrer"
                            className="text-zinc-500 underline hover:text-zinc-800"
                          >
                            View
                          </a>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>
        )}
      </div>
    </main>
  );
}
