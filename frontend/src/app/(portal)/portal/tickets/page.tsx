"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { portalAuth } from "@/lib/auth";
import NotificationBell from "@/components/NotificationBell";

interface PortalTicket {
  id: string;
  subject: string;
  description: string | null;
  status: string;
  priority: string;
  createdAt: string;
  dueAt: string | null;
  csatRating: number | null;
  csatSubmittedAt: string | null;
}

const STATUS_STYLES: Record<string, string> = {
  New: "bg-blue-100 text-blue-700",
  Open: "bg-amber-100 text-amber-700",
  Pending: "bg-purple-100 text-purple-700",
  Resolved: "bg-green-100 text-green-700",
  Closed: "bg-zinc-100 text-zinc-600",
};

export default function PortalTicketsPage() {
  const router = useRouter();
  const [tickets, setTickets] = useState<PortalTicket[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState<string | null>(null);
  const [token, setToken] = useState<string | null>(null);

  const [showCreate, setShowCreate] = useState(false);
  const [subject, setSubject] = useState("");
  const [description, setDescription] = useState("");
  const [priority, setPriority] = useState("Medium");
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  useEffect(() => {
    const auth = portalAuth.get();
    if (!auth) {
      router.replace("/portal/login");
      return;
    }
    setName(auth.name);
    setToken(auth.accessToken);

    apiFetch<PortalTicket[]>("/api/portal/tickets", { token: auth.accessToken })
      .then(setTickets)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          portalAuth.clear();
          router.replace("/portal/login");
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load your tickets.");
      });
  }, [router]);

  function handleLogout() {
    portalAuth.clear();
    router.replace("/portal/login");
  }

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    const auth = portalAuth.get();
    if (!auth) return;

    setCreateError(null);
    setCreating(true);
    try {
      const ticket = await apiFetch<PortalTicket>("/api/portal/tickets", {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({ subject, description: description || null, priority }),
      });
      setTickets((prev) => (prev ? [ticket, ...prev] : [ticket]));
      setSubject("");
      setDescription("");
      setPriority("Medium");
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
          <h1 className="text-lg font-semibold">My support tickets</h1>
          {name && <p className="text-sm text-zinc-500">Signed in as {name}</p>}
        </div>
        <div className="flex items-center gap-2">
          {token && (
            <NotificationBell
              token={token}
              basePath="/api/portal/notifications"
              ticketLinkPrefix="/portal/tickets"
              onAuthError={() => {
                portalAuth.clear();
                router.replace("/portal/login");
              }}
            />
          )}
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
              <label className="block text-sm font-medium text-zinc-700 mb-1">
                Describe your issue
              </label>
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

            {createError && <p className="sm:col-span-2 text-sm text-red-600">{createError}</p>}

            <div className="sm:col-span-2">
              <button
                type="submit"
                disabled={creating}
                className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
              >
                {creating ? "Submitting…" : "Submit ticket"}
              </button>
            </div>
          </form>
        )}

        {error && <p className="text-sm text-red-600 mb-4">{error}</p>}

        {tickets === null && !error && <p className="text-sm text-zinc-500">Loading tickets…</p>}

        {tickets !== null && tickets.length === 0 && (
          <p className="text-sm text-zinc-500">
            No tickets yet. Use &quot;New ticket&quot; above if you need help with something.
          </p>
        )}

        {tickets !== null && tickets.length > 0 && (
          <div className="overflow-hidden rounded-lg border border-zinc-200 bg-white">
            <table className="w-full text-sm">
              <thead className="bg-zinc-50 text-left text-xs font-medium uppercase text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Subject</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Priority</th>
                  <th className="px-4 py-3">Submitted</th>
                  <th className="px-4 py-3">Your rating</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-100">
                {tickets.map((t) => (
                  <tr
                    key={t.id}
                    onClick={() => router.push(`/portal/tickets/${t.id}`)}
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
                    <td className="px-4 py-3 text-zinc-600">{t.priority}</td>
                    <td className="px-4 py-3 text-zinc-500">
                      {new Date(t.createdAt).toLocaleDateString()}
                    </td>
                    <td className="px-4 py-3 text-zinc-500">
                      {t.csatRating ? "★".repeat(t.csatRating) : "—"}
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
