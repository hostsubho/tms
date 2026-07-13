"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface Ticket {
  id: string;
  subject: string;
  description: string | null;
  status: string;
  priority: string;
  createdAt: string;
  dueAt: string | null;
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
  const [error, setError] = useState<string | null>(null);
  const [email, setEmail] = useState<string | null>(null);

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }
    setEmail(auth.email);

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
  }, [router]);

  function handleLogout() {
    tenantAuth.clear();
    router.replace("/login");
  }

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4 flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold">Tickets</h1>
          {email && <p className="text-sm text-zinc-500">Signed in as {email}</p>}
        </div>
        <button
          onClick={handleLogout}
          className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm text-zinc-700 hover:bg-zinc-100"
        >
          Sign out
        </button>
      </header>

      <div className="p-8">
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
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-100">
                {tickets.map((t) => (
                  <tr key={t.id}>
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
