"use client";

import { useEffect, useState, useCallback } from "react";
import { useParams, useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface Ticket {
  id: string;
  subject: string;
  description: string | null;
  status: string;
  priority: string;
  categoryId: string | null;
  requesterId: string | null;
  assigneeId: string | null;
  createdAt: string;
  dueAt: string | null;
  responseDueAt: string | null;
  firstRespondedAt: string | null;
  escalated: boolean;
  isResolutionBreached: boolean;
  isResponseBreached: boolean;
}

interface Comment {
  id: string;
  ticketId: string;
  authorId: string;
  body: string;
  isInternal: boolean;
  createdAt: string;
}

const STATUS_OPTIONS = ["New", "Open", "Pending", "Resolved", "Closed"];
const PRIORITY_OPTIONS = ["Low", "Medium", "High", "Urgent"];

export default function TicketDetailPage() {
  const router = useRouter();
  const params = useParams<{ id: string }>();
  const ticketId = params.id;

  const [ticket, setTicket] = useState<Ticket | null>(null);
  const [comments, setComments] = useState<Comment[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [updateError, setUpdateError] = useState<string | null>(null);
  const [savingField, setSavingField] = useState<"status" | "priority" | null>(null);

  const [commentBody, setCommentBody] = useState("");
  const [isInternal, setIsInternal] = useState(false);
  const [postingComment, setPostingComment] = useState(false);
  const [commentError, setCommentError] = useState<string | null>(null);

  const [currentUserId, setCurrentUserId] = useState<string | null>(null);

  const loadTicket = useCallback(
    async (token: string) => {
      try {
        const [t, c] = await Promise.all([
          apiFetch<Ticket>(`/api/tickets/${ticketId}`, { token }),
          apiFetch<Comment[]>(`/api/tickets/${ticketId}/comments`, { token }),
        ]);
        setTicket(t);
        setComments(c);
      } catch (err) {
        if (err instanceof ApiError && err.status === 401) {
          tenantAuth.clear();
          router.replace("/login");
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load this ticket.");
      }
    },
    [ticketId, router],
  );

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }
    setCurrentUserId(auth.userId);
    loadTicket(auth.accessToken);
  }, [loadTicket, router]);

  async function updateField(field: "status" | "priority", value: string) {
    const auth = tenantAuth.get();
    if (!auth || !ticket) return;

    setUpdateError(null);
    setSavingField(field);
    try {
      const updated = await apiFetch<Ticket>(`/api/tickets/${ticketId}`, {
        method: "PATCH",
        token: auth.accessToken,
        body: JSON.stringify({ [field]: value }),
      });
      setTicket(updated);
    } catch (err) {
      setUpdateError(err instanceof ApiError ? err.message : `Couldn't update ${field}.`);
    } finally {
      setSavingField(null);
    }
  }

  async function handleAddComment(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth || !commentBody.trim()) return;

    setCommentError(null);
    setPostingComment(true);
    try {
      const comment = await apiFetch<Comment>(`/api/tickets/${ticketId}/comments`, {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({ body: commentBody, isInternal }),
      });
      setComments((prev) => [...prev, comment]);
      setCommentBody("");
      setIsInternal(false);
    } catch (err) {
      setCommentError(err instanceof ApiError ? err.message : "Couldn't post comment.");
    } finally {
      setPostingComment(false);
    }
  }

  if (error) {
    return (
      <main className="min-h-screen bg-zinc-50 p-8">
        <button onClick={() => router.push("/dashboard")} className="text-sm text-zinc-500 hover:underline">
          ← Back to tickets
        </button>
        <p className="mt-4 text-sm text-red-600">{error}</p>
      </main>
    );
  }

  if (!ticket) {
    return (
      <main className="min-h-screen bg-zinc-50 p-8">
        <p className="text-sm text-zinc-500">Loading ticket…</p>
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4">
        <button onClick={() => router.push("/dashboard")} className="text-sm text-zinc-500 hover:underline">
          ← Back to tickets
        </button>
        <h1 className="mt-2 text-xl font-semibold">{ticket.subject}</h1>
        <p className="text-xs text-zinc-400 mt-1">
          Created {new Date(ticket.createdAt).toLocaleString()}
        </p>
      </header>

      <div className="mx-auto max-w-3xl p-8 space-y-6">
        <div className="rounded-lg border border-zinc-200 bg-white p-6 space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-medium uppercase text-zinc-500 mb-1">Status</label>
              <select
                value={ticket.status}
                disabled={savingField === "status"}
                onChange={(e) => updateField("status", e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none disabled:opacity-50"
              >
                {STATUS_OPTIONS.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-xs font-medium uppercase text-zinc-500 mb-1">Priority</label>
              <select
                value={ticket.priority}
                disabled={savingField === "priority"}
                onChange={(e) => updateField("priority", e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none disabled:opacity-50"
              >
                {PRIORITY_OPTIONS.map((p) => (
                  <option key={p} value={p}>
                    {p}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {updateError && <p className="text-sm text-red-600">{updateError}</p>}

          <div>
            <label className="block text-xs font-medium uppercase text-zinc-500 mb-1">Description</label>
            <p className="text-sm text-zinc-700 whitespace-pre-wrap">
              {ticket.description || "No description provided."}
            </p>
          </div>
        </div>

        {(ticket.dueAt || ticket.responseDueAt) && (
          <div className="rounded-lg border border-zinc-200 bg-white p-6">
            <h2 className="text-sm font-semibold text-zinc-900 mb-3">SLA</h2>
            <div className="grid grid-cols-2 gap-4 text-sm">
              <div>
                <p className="text-xs font-medium uppercase text-zinc-500 mb-1">Response target</p>
                {ticket.responseDueAt ? (
                  <p className={ticket.isResponseBreached ? "text-red-600 font-medium" : "text-zinc-700"}>
                    {new Date(ticket.responseDueAt).toLocaleString()}
                    {ticket.isResponseBreached && " (breached)"}
                  </p>
                ) : (
                  <p className="text-zinc-400">—</p>
                )}
                {ticket.firstRespondedAt && (
                  <p className="text-xs text-zinc-400 mt-0.5">
                    First response {new Date(ticket.firstRespondedAt).toLocaleString()}
                  </p>
                )}
              </div>
              <div>
                <p className="text-xs font-medium uppercase text-zinc-500 mb-1">Resolution target</p>
                {ticket.dueAt ? (
                  <p className={ticket.isResolutionBreached ? "text-red-600 font-medium" : "text-zinc-700"}>
                    {new Date(ticket.dueAt).toLocaleString()}
                    {ticket.isResolutionBreached && " (breached)"}
                  </p>
                ) : (
                  <p className="text-zinc-400">—</p>
                )}
              </div>
            </div>
            {ticket.escalated && (
              <span className="mt-3 inline-block rounded-full bg-red-100 px-2 py-0.5 text-xs text-red-700">
                Escalated due to SLA breach
              </span>
            )}
          </div>
        )}

        <div className="rounded-lg border border-zinc-200 bg-white p-6">
          <h2 className="text-sm font-semibold text-zinc-900 mb-4">Comments</h2>

          {comments.length === 0 && <p className="text-sm text-zinc-500">No comments yet.</p>}

          <div className="space-y-4">
            {comments.map((c) => (
              <div
                key={c.id}
                className={`rounded-md border p-3 text-sm ${
                  c.isInternal ? "border-amber-200 bg-amber-50" : "border-zinc-200 bg-zinc-50"
                }`}
              >
                <div className="flex items-center justify-between mb-1">
                  <span className="text-xs font-medium text-zinc-500">
                    {c.authorId === currentUserId ? "You" : `User ${c.authorId.slice(0, 8)}`}
                  </span>
                  <span className="flex items-center gap-2 text-xs text-zinc-400">
                    {c.isInternal && (
                      <span className="rounded-full bg-amber-200 px-2 py-0.5 text-amber-800">
                        Internal note
                      </span>
                    )}
                    {new Date(c.createdAt).toLocaleString()}
                  </span>
                </div>
                <p className="whitespace-pre-wrap text-zinc-800">{c.body}</p>
              </div>
            ))}
          </div>

          <form onSubmit={handleAddComment} className="mt-4 space-y-2">
            <textarea
              value={commentBody}
              onChange={(e) => setCommentBody(e.target.value)}
              placeholder="Add a comment…"
              rows={3}
              required
              className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
            />
            <div className="flex items-center justify-between">
              <label className="flex items-center gap-2 text-sm text-zinc-600">
                <input
                  type="checkbox"
                  checked={isInternal}
                  onChange={(e) => setIsInternal(e.target.checked)}
                />
                Internal note (not customer-visible)
              </label>
              <button
                type="submit"
                disabled={postingComment}
                className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
              >
                {postingComment ? "Posting…" : "Post comment"}
              </button>
            </div>
            {commentError && <p className="text-sm text-red-600">{commentError}</p>}
          </form>
        </div>
      </div>
    </main>
  );
}
