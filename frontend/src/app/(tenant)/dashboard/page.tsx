"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";
import NotificationBell from "@/components/NotificationBell";

interface Ticket {
  id: string;
  subject: string;
  description: string | null;
  status: string;
  priority: string;
  createdAt: string;
  dueAt: string | null;
  escalated: boolean;
  isResolutionBreached: boolean;
}

interface Category {
  id: string;
  name: string;
}

const STATUS_STYLES: Record<string, string> = {
  New: "bg-blue-100 text-blue-700",
  Open: "bg-amber-100 text-amber-700",
  Pending: "bg-purple-100 text-purple-700",
  Resolved: "bg-green-100 text-green-700",
  Closed: "bg-zinc-100 text-zinc-600",
};

const PRIORITY_STYLES: Record<string, string> = {
  Low: "text-zinc-500",
  Medium: "text-zinc-700",
  High: "text-orange-600",
  Urgent: "text-red-600 font-semibold",
};

export default function TenantDashboardPage() {
  const router = useRouter();
  const [tickets, setTickets] = useState<Ticket[] | null>(null);
  const [categories, setCategories] = useState<Category[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [email, setEmail] = useState<string | null>(null);
  const [token, setToken] = useState<string | null>(null);

  const [showCreate, setShowCreate] = useState(false);
  const [subject, setSubject] = useState("");
  const [description, setDescription] = useState("");
  const [priority, setPriority] = useState("Medium");
  const [categoryId, setCategoryId] = useState("");
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }
    setEmail(auth.email);
    setToken(auth.accessToken);

    apiFetch<Ticket[]>("/api/tickets", { token: auth.accessToken })
      .then(setTickets)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          tenantAuth.clear();
          router.replace("/login");
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load tickets.");
      });

    apiFetch<Category[]>("/api/categories", { token: auth.accessToken })
      .then(setCategories)
      .catch(() => {
        // Non-fatal: category dropdown just stays empty.
      });
  }, [router]);

  function handleLogout() {
    tenantAuth.clear();
    router.replace("/login");
  }

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth) return;

    setCreateError(null);
    setCreating(true);
    try {
      const ticket = await apiFetch<Ticket>("/api/tickets", {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({
          subject,
          description: description || null,
          priority,
          categoryId: categoryId || null,
          assigneeId: null,
        }),
      });
      setTickets((prev) => (prev ? [ticket, ...prev] : [ticket]));
      setSubject("");
      setDescription("");
      setPriority("Medium");
      setCategoryId("");
      setShowCreate(false);
    } catch (err) {
      setCreateError(err instanceof ApiError ? err.message : "Couldn't create ticket.");
    } finally {
      setCreating(false);
    }
  }

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4 flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold">Tickets</h1>
          {email && <p className="text-sm text-zinc-500">Signed in as {email}</p>}
        </div>
        <div className="flex items-center gap-2">
          {token && (
            <NotificationBell
              token={token}
              basePath="/api/notifications"
              ticketLinkPrefix="/dashboard/tickets"
              onAuthError={() => {
                tenantAuth.clear();
                router.replace("/login");
              }}
            />
          )}
          <button
            onClick={() => router.push("/dashboard/reports")}
            className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm text-zinc-700 hover:bg-zinc-100"
          >
            Reports
          </button>
          <button
            onClick={() => router.push("/dashboard/sla-policies")}
            className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm text-zinc-700 hover:bg-zinc-100"
          >
            SLA Policies
          </button>
          <button
            onClick={() => setShowCreate((v) => !v)}
            className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-zinc-800"
          >
            {showCreate ? "Cancel" : "New ticket"}
          </button>
          <button
            onClick={handleLogout}
            className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm text-zinc-700 hover:bg-zinc-100"
          >
            Sign out
          </button>
        </div>
      </header>

      <div className="p-8 space-y-6">
        {showCreate && (
          <form
            onSubmit={handleCreate}
            className="rounded-lg border border-zinc-200 bg-white p-6 grid grid-cols-1 gap-4 sm:grid-cols-2"
          >
            <div className="sm:col-span-2">
              <label className="block text-sm font-medium text-zinc-700 mb-1">Subject</label>
              <input
                required
                value={subject}
                onChange={(e) => setSubject(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div className="sm:col-span-2">
              <label className="block text-sm font-medium text-zinc-700 mb-1">Description</label>
              <textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                rows={3}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">Priority</label>
              <select
                value={priority}
                onChange={(e) => setPriority(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              >
                <option value="Low">Low</option>
                <option value="Medium">Medium</option>
                <option value="High">High</option>
                <option value="Urgent">Urgent</option>
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">Category</label>
              <select
                value={categoryId}
                onChange={(e) => setCategoryId(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              >
                <option value="">No category</option>
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </div>

            {createError && <p className="sm:col-span-2 text-sm text-red-600">{createError}</p>}

            <div className="sm:col-span-2">
              <button
                type="submit"
                disabled={creating}
                className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
              >
                {creating ? "Creating…" : "Create ticket"}
              </button>
            </div>
          </form>
        )}

        {error && <p className="text-sm text-red-600 mb-4">{error}</p>}

        {tickets === null && !error && <p className="text-sm text-zinc-500">Loading tickets…</p>}

        {tickets !== null && tickets.length === 0 && (
          <p className="text-sm text-zinc-500">No tickets yet.</p>
        )}

        {tickets !== null && tickets.length > 0 && (
          <div className="overflow-hidden rounded-lg border border-zinc-200 bg-white">
            <table className="w-full text-sm">
              <thead className="bg-zinc-50 text-left text-xs font-medium uppercase text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Subject</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Priority</th>
                  <th className="px-4 py-3">Created</th>
                  <th className="px-4 py-3">SLA</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-100">
                {tickets.map((t) => (
                  <tr
                    key={t.id}
                    onClick={() => router.push(`/dashboard/tickets/${t.id}`)}
                    className="cursor-pointer hover:bg-zinc-50"
                  >
                    <td className="px-4 py-3 font-medium text-zinc-900">{t.subject}</td>
                    <td className="px-4 py-3">
                      <span
                        className={`inline-block rounded-full px-2 py-0.5 text-xs ${
                          STATUS_STYLES[t.status] ?? "bg-zinc-100 text-zinc-600"
                        }`}
                      >
                        {t.status}
                      </span>
                    </td>
                    <td className={`px-4 py-3 ${PRIORITY_STYLES[t.priority] ?? ""}`}>{t.priority}</td>
                    <td className="px-4 py-3 text-zinc-500">
                      {new Date(t.createdAt).toLocaleDateString()}
                    </td>
                    <td className="px-4 py-3">
                      {t.isResolutionBreached ? (
                        <span className="inline-block rounded-full bg-red-100 px-2 py-0.5 text-xs text-red-700">
                          {t.escalated ? "Breached · Escalated" : "Breached"}
                        </span>
                      ) : t.dueAt ? (
                        <span className="text-xs text-zinc-500">
                          Due {new Date(t.dueAt).toLocaleString()}
                        </span>
                      ) : (
                        <span className="text-xs text-zinc-400">No SLA</span>
                      )}
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
