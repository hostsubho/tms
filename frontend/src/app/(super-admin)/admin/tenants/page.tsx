"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { platformAuth } from "@/lib/auth";

interface Tenant {
  id: string;
  name: string;
  subdomain: string;
  planId: string;
  status: string;
  createdAt: string;
  trialEndsAt: string | null;
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

// Only Owner/PlatformAdmin roles can create/suspend/reactivate (backend's
// PlatformManage policy) - SupportEngineer, BillingAdmin, ReadOnlyAnalyst can
// still view via PlatformAdmin policy, so hide mutation controls for them
// rather than showing actions that will 403.
const MANAGE_ROLES = new Set(["Owner", "PlatformAdmin"]);

export default function SuperAdminTenantsPage() {
  const router = useRouter();
  const [tenants, setTenants] = useState<Tenant[] | null>(null);
  const [plans, setPlans] = useState<Plan[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [role, setRole] = useState<string | null>(null);
  const [email, setEmail] = useState<string | null>(null);

  const [showCreate, setShowCreate] = useState(false);
  const [name, setName] = useState("");
  const [subdomain, setSubdomain] = useState("");
  const [planId, setPlanId] = useState("");
  const [trialDays, setTrialDays] = useState(14);
  const [creating, setCreating] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);

  const loadTenants = useCallback(async (token: string) => {
    try {
      const data = await apiFetch<Tenant[]>("/api/platform/tenants", { token });
      setTenants(data);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        platformAuth.clear();
        router.replace("/admin/login");
        return;
      }
      setError(err instanceof ApiError ? err.message : "Couldn't load tenants.");
    }
  }, [router]);

  useEffect(() => {
    const auth = platformAuth.get();
    if (!auth) {
      router.replace("/admin/login");
      return;
    }
    setRole(auth.role);
    setEmail(auth.email);
    loadTenants(auth.accessToken);

    apiFetch<Plan[]>("/api/plans")
      .then((data) => {
        setPlans(data);
        if (data.length > 0) setPlanId(data[0].id);
      })
      .catch(() => {
        // Non-fatal: the create-tenant form just won't have plan options.
      });
  }, [router, loadTenants]);

  function handleLogout() {
    platformAuth.clear();
    router.replace("/admin/login");
  }

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    const auth = platformAuth.get();
    if (!auth) return;

    setActionError(null);
    setCreating(true);
    try {
      await apiFetch("/api/platform/tenants", {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({ name, subdomain, planId, trialDays }),
      });
      setName("");
      setSubdomain("");
      setShowCreate(false);
      await loadTenants(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't create tenant.");
    } finally {
      setCreating(false);
    }
  }

  async function handleSuspend(id: string) {
    const auth = platformAuth.get();
    if (!auth) return;
    setBusyId(id);
    setActionError(null);
    try {
      await apiFetch(`/api/platform/tenants/${id}/suspend`, { method: "POST", token: auth.accessToken });
      await loadTenants(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't suspend tenant.");
    } finally {
      setBusyId(null);
    }
  }

  async function handleReactivate(id: string) {
    const auth = platformAuth.get();
    if (!auth) return;
    setBusyId(id);
    setActionError(null);
    try {
      await apiFetch(`/api/platform/tenants/${id}/reactivate`, { method: "POST", token: auth.accessToken });
      await loadTenants(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't reactivate tenant.");
    } finally {
      setBusyId(null);
    }
  }

  const canManage = role !== null && MANAGE_ROLES.has(role);

  return (
    <main className="min-h-screen bg-zinc-950 text-zinc-100">
      <header className="border-b border-zinc-800 px-8 py-4 flex items-center justify-between">
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-zinc-500">TMS Platform</p>
          <h1 className="text-lg font-semibold">Tenants</h1>
          {email && (
            <p className="text-sm text-zinc-500">
              {email} · {role}
            </p>
          )}
        </div>
        <div className="flex items-center gap-2">
          {canManage && (
            <button
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-white px-3 py-1.5 text-sm font-medium text-zinc-900 hover:bg-zinc-200"
            >
              {showCreate ? "Cancel" : "New tenant"}
            </button>
          )}
          {/* Module 5.2 - Plans & Billing Administration */}
          <button
            onClick={() => router.push("/admin/plans")}
            className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm text-zinc-300 hover:bg-zinc-900"
          >
            Plans
          </button>
          <button
            onClick={() => router.push("/admin/revenue")}
            className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm text-zinc-300 hover:bg-zinc-900"
          >
            Revenue
          </button>
          <button
            onClick={handleLogout}
            className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm text-zinc-300 hover:bg-zinc-900"
          >
            Sign out
          </button>
        </div>
      </header>

      <div className="p-8 space-y-6">
        {showCreate && canManage && (
          <form
            onSubmit={handleCreate}
            className="rounded-lg border border-zinc-800 bg-zinc-900 p-6 grid grid-cols-1 gap-4 sm:grid-cols-2"
          >
            <div>
              <label className="block text-sm text-zinc-400 mb-1">Company name</label>
              <input
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-sm text-zinc-400 mb-1">Subdomain</label>
              <input
                required
                value={subdomain}
                onChange={(e) => setSubdomain(e.target.value.toLowerCase())}
                className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-sm text-zinc-400 mb-1">Plan</label>
              <select
                required
                value={planId}
                onChange={(e) => setPlanId(e.target.value)}
                className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              >
                {plans.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm text-zinc-400 mb-1">Trial days</label>
              <input
                type="number"
                min={0}
                value={trialDays}
                onChange={(e) => setTrialDays(Number(e.target.value))}
                className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div className="sm:col-span-2">
              <button
                type="submit"
                disabled={creating || !planId}
                className="rounded-md bg-white px-4 py-2 text-sm font-medium text-zinc-900 hover:bg-zinc-200 disabled:opacity-50"
              >
                {creating ? "Creating…" : "Create tenant"}
              </button>
            </div>
          </form>
        )}

        {actionError && <p className="text-sm text-red-400">{actionError}</p>}
        {error && <p className="text-sm text-red-400">{error}</p>}
        {tenants === null && !error && <p className="text-sm text-zinc-500">Loading tenants…</p>}
        {tenants !== null && tenants.length === 0 && (
          <p className="text-sm text-zinc-500">No tenants yet.</p>
        )}

        {tenants !== null && tenants.length > 0 && (
          <div className="overflow-hidden rounded-lg border border-zinc-800">
            <table className="w-full text-sm">
              <thead className="bg-zinc-900 text-left text-xs font-medium uppercase text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">Subdomain</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Trial ends</th>
                  <th className="px-4 py-3">Billing</th>
                  {canManage && <th className="px-4 py-3">Actions</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {tenants.map((t) => (
                  <tr key={t.id}>
                    <td className="px-4 py-3 font-medium">{t.name}</td>
                    <td className="px-4 py-3 text-zinc-400">{t.subdomain}</td>
                    <td className="px-4 py-3">
                      <span
                        className={`inline-block rounded-full px-2 py-0.5 text-xs ${
                          STATUS_STYLES[t.status] ?? "bg-zinc-800 text-zinc-400"
                        }`}
                      >
                        {t.status}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-zinc-400">
                      {t.trialEndsAt ? new Date(t.trialEndsAt).toLocaleDateString() : "—"}
                    </td>
                    <td className="px-4 py-3">
                      <button
                        onClick={() => router.push(`/admin/billing/${t.id}`)}
                        className="rounded-md border border-zinc-700 px-2 py-1 text-xs text-zinc-200 hover:bg-zinc-800"
                      >
                        View
                      </button>
                    </td>
                    {canManage && (
                      <td className="px-4 py-3">
                        {t.status === "Suspended" || t.status === "PastDue" ? (
                          <button
                            onClick={() => handleReactivate(t.id)}
                            disabled={busyId === t.id}
                            className="rounded-md border border-zinc-700 px-2 py-1 text-xs text-zinc-200 hover:bg-zinc-800 disabled:opacity-50"
                          >
                            Reactivate
                          </button>
                        ) : t.status !== "Churned" ? (
                          <button
                            onClick={() => handleSuspend(t.id)}
                            disabled={busyId === t.id}
                            className="rounded-md border border-red-900 px-2 py-1 text-xs text-red-400 hover:bg-red-950 disabled:opacity-50"
                          >
                            Suspend
                          </button>
                        ) : null}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </main>
  );
}
