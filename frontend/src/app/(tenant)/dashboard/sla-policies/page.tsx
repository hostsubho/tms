"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface SlaPolicy {
  id: string;
  name: string;
  responseTargetMinutes: number;
  resolutionTargetMinutes: number;
  priority: string | null;
}

const PRIORITIES = ["Low", "Medium", "High", "Urgent"];

function formatMinutes(minutes: number): string {
  if (minutes % 1440 === 0) return `${minutes / 1440}d`;
  if (minutes % 60 === 0) return `${minutes / 60}h`;
  return `${minutes}m`;
}

export default function SlaPoliciesPage() {
  const router = useRouter();
  const [policies, setPolicies] = useState<SlaPolicy[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [role, setRole] = useState<string | null>(null);

  const [showCreate, setShowCreate] = useState(false);
  const [name, setName] = useState("");
  const [priority, setPriority] = useState("");
  const [responseHours, setResponseHours] = useState(4);
  const [resolutionHours, setResolutionHours] = useState(24);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }
    setRole(auth.role);

    apiFetch<SlaPolicy[]>("/api/sla-policies", { token: auth.accessToken })
      .then(setPolicies)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          tenantAuth.clear();
          router.replace("/login");
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load SLA policies.");
      });
  }, [router]);

  const canManage = role === "Admin" || role === "Manager";

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth) return;

    setCreateError(null);
    setCreating(true);
    try {
      const policy = await apiFetch<SlaPolicy>("/api/sla-policies", {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({
          name,
          responseTargetMinutes: responseHours * 60,
          resolutionTargetMinutes: resolutionHours * 60,
          priority: priority || null,
        }),
      });
      setPolicies((prev) => (prev ? [...prev, policy] : [policy]));
      setName("");
      setPriority("");
      setResponseHours(4);
      setResolutionHours(24);
      setShowCreate(false);
    } catch (err) {
      setCreateError(err instanceof ApiError ? err.message : "Couldn't create policy.");
    } finally {
      setCreating(false);
    }
  }

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4 flex items-center justify-between">
        <div>
          <button onClick={() => router.push("/dashboard")} className="text-sm text-zinc-500 hover:underline">
            ← Back to tickets
          </button>
          <h1 className="mt-1 text-lg font-semibold">SLA Policies</h1>
        </div>
        {canManage && (
          <button
            onClick={() => setShowCreate((v) => !v)}
            className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-zinc-800"
          >
            {showCreate ? "Cancel" : "New policy"}
          </button>
        )}
      </header>

      <div className="p-8 space-y-6">
        <p className="text-sm text-zinc-500 max-w-2xl">
          A policy with no priority set is the default, applied to any ticket whose
          priority doesn&apos;t match a more specific policy. Response and resolution
          targets are measured from ticket creation; breaching tickets are
          automatically escalated one priority level.
        </p>

        {showCreate && canManage && (
          <form
            onSubmit={handleCreate}
            className="rounded-lg border border-zinc-200 bg-white p-6 grid grid-cols-1 gap-4 sm:grid-cols-2"
          >
            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">Name</label>
              <input
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">Applies to priority</label>
              <select
                value={priority}
                onChange={(e) => setPriority(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              >
                <option value="">Default (any priority not otherwise matched)</option>
                {PRIORITIES.map((p) => (
                  <option key={p} value={p}>
                    {p}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">
                Response target (hours)
              </label>
              <input
                type="number"
                min={0}
                step={0.5}
                value={responseHours}
                onChange={(e) => setResponseHours(Number(e.target.value))}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">
                Resolution target (hours)
              </label>
              <input
                type="number"
                min={0}
                step={0.5}
                value={resolutionHours}
                onChange={(e) => setResolutionHours(Number(e.target.value))}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>

            {createError && <p className="sm:col-span-2 text-sm text-red-600">{createError}</p>}

            <div className="sm:col-span-2">
              <button
                type="submit"
                disabled={creating}
                className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
              >
                {creating ? "Creating…" : "Create policy"}
              </button>
            </div>
          </form>
        )}

        {error && <p className="text-sm text-red-600">{error}</p>}
        {policies === null && !error && <p className="text-sm text-zinc-500">Loading policies…</p>}
        {policies !== null && policies.length === 0 && (
          <p className="text-sm text-zinc-500">
            No SLA policies yet. Tickets won&apos;t get due dates or auto-escalation
            until at least a default policy exists.
          </p>
        )}

        {policies !== null && policies.length > 0 && (
          <div className="overflow-hidden rounded-lg border border-zinc-200 bg-white">
            <table className="w-full text-sm">
              <thead className="bg-zinc-50 text-left text-xs font-medium uppercase text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">Priority</th>
                  <th className="px-4 py-3">Response target</th>
                  <th className="px-4 py-3">Resolution target</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-100">
                {policies.map((p) => (
                  <tr key={p.id}>
                    <td className="px-4 py-3 font-medium text-zinc-900">{p.name}</td>
                    <td className="px-4 py-3 text-zinc-600">{p.priority ?? "Default"}</td>
                    <td className="px-4 py-3 text-zinc-600">
                      {formatMinutes(p.responseTargetMinutes)}
                    </td>
                    <td className="px-4 py-3 text-zinc-600">
                      {formatMinutes(p.resolutionTargetMinutes)}
                    </td>
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
