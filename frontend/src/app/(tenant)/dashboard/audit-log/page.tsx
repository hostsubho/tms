"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface AuditLogEntry {
  id: string;
  actorLabel: string;
  action: string;
  entityType: string;
  entityId: string;
  summary: string;
  timestamp: string;
}

const ENTITY_TYPES = [
  "Ticket",
  "Category",
  "SlaPolicy",
  "AutomationRule",
  "KnowledgeArticle",
  "CustomRole",
  "User",
  "ApiKey",
  "WebhookSubscription",
  "Billing",
  "Asset",
  "Impersonation",
];
const ACTIONS = ["Created", "Updated", "Deleted"];

const ACTION_STYLES: Record<string, string> = {
  Created: "bg-green-100 text-green-700",
  Updated: "bg-blue-100 text-blue-700",
  Deleted: "bg-red-100 text-red-700",
};

export default function AuditLogPage() {
  const router = useRouter();
  const [logs, setLogs] = useState<AuditLogEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);
  const [entityType, setEntityType] = useState("");
  const [action, setAction] = useState("");

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }

    const params = new URLSearchParams();
    if (entityType) params.set("entityType", entityType);
    if (action) params.set("action", action);
    const qs = params.toString();

    apiFetch<AuditLogEntry[]>(`/api/audit-logs${qs ? `?${qs}` : ""}`, { token: auth.accessToken })
      .then((data) => {
        setLogs(data);
        setForbidden(false);
        setError(null);
      })
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          tenantAuth.clear();
          router.replace("/login");
          return;
        }
        if (err instanceof ApiError && err.status === 403) {
          setForbidden(true);
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load the audit log.");
      });
  }, [router, entityType, action]);

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4 flex items-center justify-between">
        <div>
          <button onClick={() => router.push("/dashboard")} className="text-sm text-zinc-500 hover:underline">
            ← Back to tickets
          </button>
          <h1 className="mt-1 text-lg font-semibold">Audit Log</h1>
        </div>
      </header>

      <div className="p-8 space-y-6">
        <p className="max-w-2xl text-sm text-zinc-500">
          A tenant-wide, append-only record of who did what and when: ticket
          changes, SLA policy and automation rule edits, knowledge article
          changes, custom role changes, and every automation rule firing.
          Restricted to Admins, Managers, and anyone granted the
          &quot;View Audit Log&quot; permission via a custom role.
        </p>

        {forbidden && (
          <p className="text-sm text-zinc-500">
            You don&apos;t have permission to view the audit log.
          </p>
        )}

        {!forbidden && (
          <div className="flex gap-3">
            <select
              value={entityType}
              onChange={(e) => setEntityType(e.target.value)}
              className="rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
            >
              <option value="">All entity types</option>
              {ENTITY_TYPES.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
            <select
              value={action}
              onChange={(e) => setAction(e.target.value)}
              className="rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
            >
              <option value="">All actions</option>
              {ACTIONS.map((a) => (
                <option key={a} value={a}>
                  {a}
                </option>
              ))}
            </select>
          </div>
        )}

        {error && <p className="text-sm text-red-600">{error}</p>}
        {!forbidden && logs === null && !error && <p className="text-sm text-zinc-500">Loading…</p>}
        {!forbidden && logs !== null && logs.length === 0 && (
          <p className="text-sm text-zinc-500">No matching audit entries yet.</p>
        )}

        {!forbidden && logs !== null && logs.length > 0 && (
          <div className="overflow-hidden rounded-lg border border-zinc-200 bg-white">
            <table className="w-full text-sm">
              <thead className="bg-zinc-50 text-left text-xs font-medium uppercase text-zinc-500">
                <tr>
                  <th className="px-4 py-3">When</th>
                  <th className="px-4 py-3">Actor</th>
                  <th className="px-4 py-3">Action</th>
                  <th className="px-4 py-3">Entity</th>
                  <th className="px-4 py-3">Summary</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-100">
                {logs.map((l) => (
                  <tr key={l.id}>
                    <td className="whitespace-nowrap px-4 py-3 text-zinc-500">
                      {new Date(l.timestamp).toLocaleString()}
                    </td>
                    <td className="px-4 py-3 text-zinc-700">{l.actorLabel}</td>
                    <td className="px-4 py-3">
                      <span
                        className={`inline-block rounded-full px-2 py-0.5 text-xs ${
                          ACTION_STYLES[l.action] ?? "bg-zinc-100 text-zinc-600"
                        }`}
                      >
                        {l.action}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-zinc-500">{l.entityType}</td>
                    <td className="px-4 py-3 text-zinc-900">{l.summary}</td>
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
