"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";

interface NotificationItem {
  id: string;
  type: "TicketAssigned" | "NewComment" | "SlaBreach" | "NewTicket";
  message: string;
  ticketId: string;
  isRead: boolean;
  createdAt: string;
}

interface NotificationBellProps {
  token: string;
  /** "/api/notifications" for staff, "/api/portal/notifications" for the customer portal. */
  basePath: string;
  /** Where a notification's ticketId should link to, e.g. "/dashboard/tickets" or "/portal/tickets". */
  ticketLinkPrefix: string;
  onAuthError: () => void;
}

export default function NotificationBell({ token, basePath, ticketLinkPrefix, onAuthError }: NotificationBellProps) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [notifications, setNotifications] = useState<NotificationItem[] | null>(null);
  const [enabled, setEnabled] = useState<boolean | null>(null);
  const [error, setError] = useState<string | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  const unreadCount = notifications?.filter((n) => !n.isRead).length ?? 0;

  async function load() {
    try {
      const [list, prefs] = await Promise.all([
        apiFetch<NotificationItem[]>(basePath, { token }),
        apiFetch<{ enabled: boolean }>(`${basePath}/preferences`, { token }),
      ]);
      setNotifications(list);
      setEnabled(prefs.enabled);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }
      setError(err instanceof ApiError ? err.message : "Couldn't load notifications.");
    }
  }

  useEffect(() => {
    load();
    const interval = setInterval(load, 30000);
    return () => clearInterval(interval);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  async function handleOpenNotification(n: NotificationItem) {
    setOpen(false);
    if (!n.isRead) {
      try {
        await apiFetch(`${basePath}/${n.id}/read`, { method: "POST", token });
        setNotifications((prev) => prev?.map((x) => (x.id === n.id ? { ...x, isRead: true } : x)) ?? prev);
      } catch {
        // Non-fatal: navigate anyway even if marking read failed.
      }
    }
    router.push(`${ticketLinkPrefix}/${n.ticketId}`);
  }

  async function handleMarkAllRead() {
    try {
      await apiFetch(`${basePath}/read-all`, { method: "POST", token });
      setNotifications((prev) => prev?.map((n) => ({ ...n, isRead: true })) ?? prev);
    } catch {
      // Non-fatal.
    }
  }

  async function handleToggleEnabled() {
    if (enabled === null) return;
    const next = !enabled;
    setEnabled(next);
    try {
      await apiFetch(`${basePath}/preferences`, {
        method: "PATCH",
        token,
        body: JSON.stringify({ enabled: next }),
      });
    } catch {
      setEnabled(!next);
    }
  }

  return (
    <div className="relative" ref={containerRef}>
      <button
        onClick={() => {
          setOpen((v) => !v);
          // Refresh on open rather than waiting for the 30s poll - a user
          // clicking the bell right after triggering an event (e.g.
          // creating a ticket) expects to see it immediately, not on the
          // next timer tick.
          load();
        }}
        className="relative rounded-md border border-zinc-300 px-3 py-1.5 text-sm text-zinc-700 hover:bg-zinc-100"
        aria-label="Notifications"
      >
        Notifications
        {unreadCount > 0 && (
          <span className="absolute -right-1.5 -top-1.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-600 px-1 text-[10px] font-medium text-white">
            {unreadCount > 9 ? "9+" : unreadCount}
          </span>
        )}
      </button>

      {open && (
        <div className="absolute right-0 z-10 mt-2 w-80 rounded-lg border border-zinc-200 bg-white shadow-lg">
          <div className="flex items-center justify-between border-b border-zinc-100 px-4 py-2">
            <span className="text-sm font-semibold text-zinc-900">Notifications</span>
            <button onClick={handleMarkAllRead} className="text-xs text-zinc-500 hover:underline">
              Mark all read
            </button>
          </div>

          <div className="max-h-80 overflow-y-auto">
            {error && <p className="p-4 text-sm text-red-600">{error}</p>}
            {!error && notifications === null && <p className="p-4 text-sm text-zinc-500">Loading…</p>}
            {!error && notifications !== null && notifications.length === 0 && (
              <p className="p-4 text-sm text-zinc-500">No notifications yet.</p>
            )}
            {notifications?.map((n) => (
              <button
                key={n.id}
                onClick={() => handleOpenNotification(n)}
                className={`block w-full border-b border-zinc-50 px-4 py-2.5 text-left text-sm hover:bg-zinc-50 ${
                  n.isRead ? "text-zinc-500" : "text-zinc-900"
                }`}
              >
                <div className="flex items-start gap-2">
                  {!n.isRead && <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-blue-600" />}
                  <div className={n.isRead ? "ml-3.5" : ""}>
                    <p>{n.message}</p>
                    <p className="mt-0.5 text-xs text-zinc-400">{new Date(n.createdAt).toLocaleString()}</p>
                  </div>
                </div>
              </button>
            ))}
          </div>

          <div className="border-t border-zinc-100 px-4 py-2">
            <label className="flex items-center gap-2 text-xs text-zinc-600">
              <input type="checkbox" checked={enabled ?? true} onChange={handleToggleEnabled} disabled={enabled === null} />
              Notify me about ticket activity
            </label>
          </div>
        </div>
      )}
    </div>
  );
}
