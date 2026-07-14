"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { platformAuth } from "@/lib/auth";

interface Plan {
  id: string;
  name: string;
  maxAgents: number;
  maxTicketsPerMonth: number;
  priceMonthly: number;
  stripePriceId: string | null;
}

// Editing what a plan costs/includes and wiring its Stripe Price is a
// pricing decision, not day-to-day billing ops - restricted to the
// backend's PlatformManage policy (Owner/PlatformAdmin), same reasoning as
// SuperAdminTenantsController's create/suspend actions.
const MANAGE_ROLES = new Set(["Owner", "PlatformAdmin"]);

export default function PlatformPlansPage() {
  const router = useRouter();
  const [plans, setPlans] = useState<Plan[] | null>(null);
  const [role, setRole] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const [editing, setEditing] = useState<Record<string, Partial<Plan>>>({});
  const [savingId, setSavingId] = useState<string | null>(null);

  const [showCreate, setShowCreate] = useState(false);
  const [name, setName] = useState("");
  const [maxAgents, setMaxAgents] = useState(1);
  const [maxTicketsPerMonth, setMaxTicketsPerMonth] = useState(100);
  const [priceMonthly, setPriceMonthly] = useState(0);
  const [stripePriceId, setStripePriceId] = useState("");
  const [creating, setCreating] = useState(false);

  const load = useCallback(async (token: string) => {
    try {
      const data = await apiFetch<Plan[]>("/api/platform/plans", { token });
      setPlans(data);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        platformAuth.clear();
        router.replace("/admin/login");
        return;
      }
      setError(err instanceof ApiError ? err.message : "Couldn't load plans.");
    }
  }, [router]);

  useEffect(() => {
    const auth = platformAuth.get();
    if (!auth) {
      router.replace("/admin/login");
      return;
    }
    setRole(auth.role);
    load(auth.accessToken);
  }, [router, load]);

  const canManage = role !== null && MANAGE_ROLES.has(role);

  function fieldValue<K extends keyof Plan>(plan: Plan, key: K): Plan[K] {
    const draft = editing[plan.id];
    return draft && key in draft ? (draft[key] as Plan[K]) : plan[key];
  }

  function setField<K extends keyof Plan>(planId: string, key: K, value: Plan[K]) {
    setEditing((prev) => ({ ...prev, [planId]: { ...prev[planId], [key]: value } }));
  }

  async function handleSave(plan: Plan) {
    const auth = platformAuth.get();
    if (!auth) return;

    setActionError(null);
    setSavingId(plan.id);
    try {
      const draft = editing[plan.id] ?? {};
      await apiFetch(`/api/platform/plans/${plan.id}`, {
        method: "PATCH",
        token: auth.accessToken,
        body: JSON.stringify({
          name: draft.name ?? null,
          maxAgents: draft.maxAgents ?? null,
          maxTicketsPerMonth: draft.maxTicketsPerMonth ?? null,
          priceMonthly: draft.priceMonthly ?? null,
          stripePriceId: draft.stripePriceId !== undefined ? draft.stripePriceId : null,
        }),
      });
      setEditing((prev) => {
        const next = { ...prev };
        delete next[plan.id];
        return next;
      });
      await load(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't save plan.");
    } finally {
      setSavingId(null);
    }
  }

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    const auth = platformAuth.get();
    if (!auth) return;

    setActionError(null);
    setCreating(true);
    try {
      await apiFetch("/api/platform/plans", {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({
          name,
          maxAgents,
          maxTicketsPerMonth,
          priceMonthly,
          stripePriceId: stripePriceId || null,
        }),
      });
      setName("");
      setMaxAgents(1);
      setMaxTicketsPerMonth(100);
      setPriceMonthly(0);
      setStripePriceId("");
      setShowCreate(false);
      await load(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't create plan.");
    } finally {
      setCreating(false);
    }
  }

  return (
    <main className="min-h-screen bg-zinc-950 text-zinc-100">
      <header className="border-b border-zinc-800 px-8 py-4 flex items-center justify-between">
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-zinc-500">TMS Platform</p>
          <h1 className="text-lg font-semibold">Plans</h1>
        </div>
        <div className="flex items-center gap-2">
          {canManage && (
            <button
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-white px-3 py-1.5 text-sm font-medium text-zinc-900 hover:bg-zinc-200"
            >
              {showCreate ? "Cancel" : "New plan"}
            </button>
          )}
          <button
            onClick={() => router.push("/admin/tenants")}
            className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm text-zinc-300 hover:bg-zinc-900"
          >
            Tenants
          </button>
          <button
            onClick={() => router.push("/admin/revenue")}
            className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm text-zinc-300 hover:bg-zinc-900"
          >
            Revenue
          </button>
        </div>
      </header>

      <div className="p-8 space-y-6">
        {showCreate && canManage && (
          <form
            onSubmit={handleCreate}
            className="rounded-lg border border-zinc-800 bg-zinc-900 p-6 grid grid-cols-1 gap-4 sm:grid-cols-3"
          >
            <div>
              <label className="block text-sm text-zinc-400 mb-1">Name</label>
              <input
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-sm text-zinc-400 mb-1">Max agents</label>
              <input
                type="number"
                min={0}
                value={maxAgents}
                onChange={(e) => setMaxAgents(Number(e.target.value))}
                className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-sm text-zinc-400 mb-1">Max tickets/mo</label>
              <input
                type="number"
                min={0}
                value={maxTicketsPerMonth}
                onChange={(e) => setMaxTicketsPerMonth(Number(e.target.value))}
                className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-sm text-zinc-400 mb-1">Price/mo (USD)</label>
              <input
                type="number"
                min={0}
                step="0.01"
                value={priceMonthly}
                onChange={(e) => setPriceMonthly(Number(e.target.value))}
                className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div className="sm:col-span-2">
              <label className="block text-sm text-zinc-400 mb-1">Stripe Price ID (optional)</label>
              <input
                value={stripePriceId}
                onChange={(e) => setStripePriceId(e.target.value)}
                placeholder="price_..."
                className="w-full rounded-md border border-zinc-700 bg-zinc-950 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div className="sm:col-span-3">
              <button
                type="submit"
                disabled={creating}
                className="rounded-md bg-white px-4 py-2 text-sm font-medium text-zinc-900 hover:bg-zinc-200 disabled:opacity-50"
              >
                {creating ? "Creating…" : "Create plan"}
              </button>
            </div>
          </form>
        )}

        {actionError && <p className="text-sm text-red-400">{actionError}</p>}
        {error && <p className="text-sm text-red-400">{error}</p>}
        {plans === null && !error && <p className="text-sm text-zinc-500">Loading plans…</p>}

        {plans !== null && (
          <div className="overflow-hidden rounded-lg border border-zinc-800">
            <table className="w-full text-sm">
              <thead className="bg-zinc-900 text-left text-xs font-medium uppercase text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">Max agents</th>
                  <th className="px-4 py-3">Max tickets/mo</th>
                  <th className="px-4 py-3">Price/mo</th>
                  <th className="px-4 py-3">Stripe Price ID</th>
                  {canManage && <th className="px-4 py-3"></th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {plans.map((plan) => (
                  <tr key={plan.id}>
                    <td className="px-4 py-3">
                      {canManage ? (
                        <input
                          value={fieldValue(plan, "name")}
                          onChange={(e) => setField(plan.id, "name", e.target.value)}
                          className="w-32 rounded-md border border-zinc-700 bg-zinc-950 px-2 py-1 text-sm focus:border-zinc-500 focus:outline-none"
                        />
                      ) : (
                        plan.name
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {canManage ? (
                        <input
                          type="number"
                          min={0}
                          value={fieldValue(plan, "maxAgents")}
                          onChange={(e) => setField(plan.id, "maxAgents", Number(e.target.value))}
                          className="w-20 rounded-md border border-zinc-700 bg-zinc-950 px-2 py-1 text-sm focus:border-zinc-500 focus:outline-none"
                        />
                      ) : (
                        plan.maxAgents
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {canManage ? (
                        <input
                          type="number"
                          min={0}
                          value={fieldValue(plan, "maxTicketsPerMonth")}
                          onChange={(e) => setField(plan.id, "maxTicketsPerMonth", Number(e.target.value))}
                          className="w-24 rounded-md border border-zinc-700 bg-zinc-950 px-2 py-1 text-sm focus:border-zinc-500 focus:outline-none"
                        />
                      ) : (
                        plan.maxTicketsPerMonth
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {canManage ? (
                        <input
                          type="number"
                          min={0}
                          step="0.01"
                          value={fieldValue(plan, "priceMonthly")}
                          onChange={(e) => setField(plan.id, "priceMonthly", Number(e.target.value))}
                          className="w-24 rounded-md border border-zinc-700 bg-zinc-950 px-2 py-1 text-sm focus:border-zinc-500 focus:outline-none"
                        />
                      ) : (
                        `$${plan.priceMonthly}`
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {canManage ? (
                        <input
                          value={fieldValue(plan, "stripePriceId") ?? ""}
                          onChange={(e) => setField(plan.id, "stripePriceId", e.target.value)}
                          placeholder="price_..."
                          className="w-40 rounded-md border border-zinc-700 bg-zinc-950 px-2 py-1 text-sm focus:border-zinc-500 focus:outline-none"
                        />
                      ) : (
                        <span className="text-zinc-400">{plan.stripePriceId ?? "—"}</span>
                      )}
                    </td>
                    {canManage && (
                      <td className="px-4 py-3">
                        <button
                          onClick={() => handleSave(plan)}
                          disabled={savingId === plan.id || !editing[plan.id]}
                          className="rounded-md border border-zinc-700 px-2 py-1 text-xs text-zinc-200 hover:bg-zinc-800 disabled:opacity-50"
                        >
                          {savingId === plan.id ? "Saving…" : "Save"}
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <p className="text-xs text-zinc-500">
          A plan needs a Stripe Price ID before tenants can subscribe to it as a paid plan - create the matching
          Product/Price in the Stripe Dashboard (test mode) first, then paste its ID here.
        </p>
      </div>
    </main>
  );
}
