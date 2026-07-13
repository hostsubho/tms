"use client";

import { useEffect, useState, useCallback } from "react";
import { useParams, useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { portalAuth } from "@/lib/auth";

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

interface PortalComment {
  id: string;
  ticketId: string;
  body: string;
  isFromCustomer: boolean;
  createdAt: string;
}

const RATEABLE_STATUSES = ["Resolved", "Closed"];

export default function PortalTicketDetailPage() {
  const router = useRouter();
  const params = useParams<{ id: string }>();
  const ticketId = params.id;

  const [ticket, setTicket] = useState<PortalTicket | null>(null);
  const [comments, setComments] = useState<PortalComment[]>([]);
  const [error, setError] = useState<string | null>(null);

  const [commentBody, setCommentBody] = useState("");
  const [postingComment, setPostingComment] = useState(false);
  const [commentError, setCommentError] = useState<string | null>(null);

  const [ratingHover, setRatingHover] = useState(0);
  const [submittingRating, setSubmittingRating] = useState(false);
  const [ratingError, setRatingError] = useState<string | null>(null);

  const loadTicket = useCallback(
    async (token: string) => {
      try {
        const [t, c] = await Promise.all([
          apiFetch<PortalTicket>(`/api/portal/tickets/${ticketId}`, { token }),
          apiFetch<PortalComment[]>(`/api/portal/tickets/${ticketId}/comments`, { token }),
        ]);
        setTicket(t);
        setComments(c);
      } catch (err) {
        if (err instanceof ApiError && err.status === 401) {
          portalAuth.clear();
          router.replace("/portal/login");
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load this ticket.");
      }
    },
    [ticketId, router],
  );

  useEffect(() => {
    const auth = portalAuth.get();
    if (!auth) {
      router.replace("/portal/login");
      return;
    }
    loadTicket(auth.accessToken);
  }, [loadTicket, router]);

  async function handleAddComment(e: React.FormEvent) {
    e.preventDefault();
    const auth = portalAuth.get();
    if (!auth || !commentBody.trim()) return;

    setCommentError(null);
    setPostingComment(true);
    try {
      const comment = await apiFetch<PortalComment>(`/api/portal/tickets/${ticketId}/comments`, {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({ body: commentBody }),
      });
      setComments((prev) => [...prev, comment]);
      setCommentBody("");
    } catch (err) {
      setCommentError(err instanceof ApiError ? err.message : "Couldn't post comment.");
    } finally {
      setPostingComment(false);
    }
  }

  async function handleRate(rating: number) {
    const auth = portalAuth.get();
    if (!auth) return;

    setRatingError(null);
    setSubmittingRating(true);
    try {
      const updated = await apiFetch<PortalTicket>(`/api/portal/tickets/${ticketId}/csat`, {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({ rating }),
      });
      setTicket(updated);
    } catch (err) {
      setRatingError(err instanceof ApiError ? err.message : "Couldn't submit rating.");
    } finally {
      setSubmittingRating(false);
    }
  }

  if (error) {
    return (
      <main className="min-h-screen bg-zinc-50 p-8">
        <button onClick={() => router.push("/portal/tickets")} className="text-sm text-zinc-500 hover:underline">
          ← Back to my tickets
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

  const canRate = RATEABLE_STATUSES.includes(ticket.status);

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4">
        <button onClick={() => router.push("/portal/tickets")} className="text-sm text-zinc-500 hover:underline">
          ← Back to my tickets
        </button>
        <h1 className="mt-2 text-xl font-semibold">{ticket.subject}</h1>
        <p className="text-xs text-zinc-400 mt-1">
          Submitted {new Date(ticket.createdAt).toLocaleString()} · {ticket.status} · {ticket.priority} priority
        </p>
      </header>

      <div className="mx-auto max-w-3xl p-8 space-y-6">
        <div className="rounded-lg border border-zinc-200 bg-white p-6">
          <p className="text-xs font-medium uppercase text-zinc-500 mb-1">Description</p>
          <p className="text-sm text-zinc-700 whitespace-pre-wrap">
            {ticket.description || "No description provided."}
          </p>
          {ticket.dueAt && (
            <p className="mt-3 text-xs text-zinc-400">
              Expected resolution by {new Date(ticket.dueAt).toLocaleString()}
            </p>
          )}
        </div>

        {canRate && (
          <div className="rounded-lg border border-zinc-200 bg-white p-6">
            <h2 className="text-sm font-semibold text-zinc-900 mb-3">
              {ticket.csatSubmittedAt ? "Your rating" : "How did we do?"}
            </h2>
            <div className="flex items-center gap-1">
              {[1, 2, 3, 4, 5].map((star) => {
                const filled = ticket.csatSubmittedAt
                  ? star <= (ticket.csatRating ?? 0)
                  : star <= ratingHover;
                return (
                  <button
                    key={star}
                    type="button"
                    disabled={!!ticket.csatSubmittedAt || submittingRating}
                    onMouseEnter={() => !ticket.csatSubmittedAt && setRatingHover(star)}
                    onMouseLeave={() => !ticket.csatSubmittedAt && setRatingHover(0)}
                    onClick={() => handleRate(star)}
                    className={`text-2xl leading-none ${
                      filled ? "text-amber-400" : "text-zinc-300"
                    } ${ticket.csatSubmittedAt ? "cursor-default" : "cursor-pointer hover:text-amber-400"}`}
                    aria-label={`Rate ${star} star${star > 1 ? "s" : ""}`}
                  >
                    ★
                  </button>
                );
              })}
            </div>
            {ticket.csatSubmittedAt && (
              <p className="mt-2 text-xs text-zinc-400">
                Submitted {new Date(ticket.csatSubmittedAt).toLocaleString()} — thanks for the feedback.
              </p>
            )}
            {ratingError && <p className="mt-2 text-sm text-red-600">{ratingError}</p>}
          </div>
        )}

        <div className="rounded-lg border border-zinc-200 bg-white p-6">
          <h2 className="text-sm font-semibold text-zinc-900 mb-4">Conversation</h2>

          {comments.length === 0 && <p className="text-sm text-zinc-500">No replies yet.</p>}

          <div className="space-y-4">
            {comments.map((c) => (
              <div
                key={c.id}
                className={`rounded-md border p-3 text-sm ${
                  c.isFromCustomer ? "border-zinc-200 bg-zinc-50" : "border-blue-100 bg-blue-50"
                }`}
              >
                <div className="flex items-center justify-between mb-1">
                  <span className="text-xs font-medium text-zinc-500">
                    {c.isFromCustomer ? "You" : "Support team"}
                  </span>
                  <span className="text-xs text-zinc-400">{new Date(c.createdAt).toLocaleString()}</span>
                </div>
                <p className="whitespace-pre-wrap text-zinc-800">{c.body}</p>
              </div>
            ))}
          </div>

          <form onSubmit={handleAddComment} className="mt-4 space-y-2">
            <textarea
              value={commentBody}
              onChange={(e) => setCommentBody(e.target.value)}
              placeholder="Add a reply…"
              rows={3}
              required
              className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
            />
            <div className="flex items-center justify-end">
              <button
                type="submit"
                disabled={postingComment}
                className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
              >
                {postingComment ? "Sending…" : "Send reply"}
              </button>
            </div>
            {commentError && <p className="text-sm text-red-600">{commentError}</p>}
          </form>
        </div>
      </div>
    </main>
  );
}
