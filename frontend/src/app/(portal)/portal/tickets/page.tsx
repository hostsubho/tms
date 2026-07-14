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

interface ArticleSummary {
  id: string;
  title: string;
  snippet: string;
}

interface ArticleDetail {
  id: string;
  title: string;
  body: string;
  helpfulYesCount: number;
  helpfulNoCount: number;
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

  // Module 6 - Knowledge Base deflection: as the customer types a subject,
  // show relevant self-service articles before they even submit the ticket.
  const [suggestions, setSuggestions] = useState<ArticleSummary[]>([]);
  const [expandedArticle, setExpandedArticle] = useState<ArticleDetail | null>(null);
  const [feedbackGiven, setFeedbackGiven] = useState(false);

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

  // Debounced so every keystroke doesn't fire a request - waits 400ms of no
  // typing, and only searches once there's enough of a subject to score
  // against (matches the backend's own 3-character-minimum word filter).
  useEffect(() => {
    if (!showCreate || subject.trim().length < 3) {
      setSuggestions([]);
      return;
    }
    const auth = portalAuth.get();
    if (!auth) return;

    const handle = setTimeout(() => {
      apiFetch<ArticleSummary[]>(`/api/portal/knowledge-articles?query=${encodeURIComponent(subject)}`, {
        token: auth.accessToken,
      })
        .then(setSuggestions)
        .catch(() => {
          // Non-fatal: suggestions just don't show up.
        });
    }, 400);

    return () => clearTimeout(handle);
  }, [subject, showCreate]);

  async function handleViewArticle(id: string) {
    const auth = portalAuth.get();
    if (!auth) return;
    setFeedbackGiven(false);
    try {
      const article = await apiFetch<ArticleDetail>(`/api/portal/knowledge-articles/${id}`, { token: auth.accessToken });
      setExpandedArticle(article);
    } catch {
      // Non-fatal: article just doesn't expand.
    }
  }

  async function handleFeedback(helpful: boolean) {
    const auth = portalAuth.get();
    if (!auth || !expandedArticle) return;
    setFeedbackGiven(true);
    try {
      await apiFetch(`/api/portal/knowledge-articles/${expandedArticle.id}/feedback`, {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({ helpful }),
      });
    } catch {
      // Non-fatal: the vote just doesn't register.
    }
  }

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
      setSuggestions([]);
      setExpandedArticle(null);
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
                onChange={(e) => {
                  setSubject(e.target.value);
                  setExpandedArticle(null);
                }}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>

            {suggestions.length > 0 && (
              <div className="sm:col-span-2 rounded-md border border-blue-100 bg-blue-50 p-4">
                <p className="mb-2 text-xs font-medium uppercase text-blue-700">
                  These articles might already answer your question
                </p>
                <ul className="space-y-1">
                  {suggestions.map((s) => (
                    <li key={s.id}>
                      <button
                        type="button"
                        onClick={() => handleViewArticle(s.id)}
                        className="text-left text-sm font-medium text-blue-700 hover:underline"
                      >
                        {s.title}
                      </button>
                      <p className="text-xs text-zinc-500">{s.snippet}</p>
                    </li>
                  ))}
                </ul>

                {expandedArticle && (
                  <div className="mt-3 rounded-md border border-zinc-200 bg-white p-4">
                    <h3 className="text-sm font-semibold text-zinc-900">{expandedArticle.title}</h3>
                    <p className="mt-1 whitespace-pre-wrap text-sm text-zinc-600">{expandedArticle.body}</p>
                    <div className="mt-3 flex items-center gap-3 border-t border-zinc-100 pt-3">
                      <span className="text-xs text-zinc-500">Was this helpful?</span>
                      {feedbackGiven ? (
                        <span className="text-xs text-green-700">Thanks for the feedback!</span>
                      ) : (
                        <>
                          <button
                            type="button"
                            onClick={() => handleFeedback(true)}
                            className="rounded-md border border-zinc-300 px-2 py-1 text-xs hover:bg-zinc-100"
                          >
                            Yes
                          </button>
                          <button
                            type="button"
                            onClick={() => handleFeedback(false)}
                            className="rounded-md border border-zinc-300 px-2 py-1 text-xs hover:bg-zinc-100"
                          >
                            No
                          </button>
                        </>
                      )}
                    </div>
                  </div>
                )}
              </div>
            )}

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
