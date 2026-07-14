"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError, API_BASE_URL } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface ApiKey {
  id: string;
  name: string;
  keyPrefix: string;
  createdAt: string;
  lastUsedAt: string | null;
  revokedAt: string | null;
}

interface Webhook {
  id: string;
  url: string;
  event: string;
  isActive: boolean;
  createdAt: string;
}

interface WebhookLog {
  id: string;
  ticketId: string;
  event: string;
  success: boolean;
  statusCode: number | null;
  error: string | null;
  attemptedAt: string;
}

const EVENTS = [
  { value: "TicketCreated", label: "Ticket created" },
  { value: "TicketStatusChanged", label: "Ticket status changed" },
];

export default function IntegrationsPage() {
  const router = useRouter();
  const [forbidden, setForbidden] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [keys, setKeys] = useState<ApiKey[] | null>(null);
  const [newKeyName, setNewKeyName] = useState("");
  const [creatingKey, setCreatingKey] = useState(false);
  const [keyError, setKeyError] = useState<string | null>(null);
  const [justCreatedKey, setJustCreatedKey] = useState<string | null>(null);

  const [webhooks, setWebhooks] = useState<Webhook[] | null>(null);
  const [newWebhookUrl, setNewWebhookUrl] = useState("");
  const [newWebhookEvent, setNewWebhookEvent] = useState(EVENTS[0].value);
  const [creatingWebhook, setCreatingWebhook] = useState(false);
  const [webhookError, setWebhookError] = useState<string | null>(null);
  const [justCreatedSecret, setJustCreatedSecret] = useState<string | null>(null);

  const [logsFor, setLogsFor] = useState<string | null>(null);
  const [logs, setLogs] = useState<WebhookLog[] | null>(null);

  function loadAll(token: string) {
    apiFetch<ApiKey[]>("/api/api-keys", { token })
      .then((data) => {
        setKeys(data);
        setForbidden(false);
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
        setError(err instanceof ApiError ? err.message : "Couldn't load API keys.");
      });

    apiFetch<Webhook[]>("/api/webhooks", { token })
      .then(setWebhooks)
      .catch(() => {
        // Non-fatal - keys section can still render on its own.
      });
  }

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }
    loadAll(auth.accessToken);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [router]);

  async function handleCreateKey(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth) return;

    setKeyError(null);
    setCreatingKey(true);
    try {
      const created = await apiFetch<ApiKey & { plaintextKey: string }>("/api/api-keys", {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({ name: newKeyName }),
      });
      setKeys((prev) => (prev ? [created, ...prev] : [created]));
      setJustCreatedKey(created.plaintextKey);
      setNewKeyName("");
    } catch (err) {
      setKeyError(err instanceof ApiError ? err.message : "Couldn't create API key.");
    } finally {
      setCreatingKey(false);
    }
  }

  async function handleRevokeKey(key: ApiKey) {
    const auth = tenantAuth.get();
    if (!auth) return;
    try {
      await apiFetch(`/api/api-keys/${key.id}`, { method: "DELETE", token: auth.accessToken });
      setKeys((prev) =>
        prev?.map((k) => (k.id === key.id ? { ...k, revokedAt: new Date().toISOString() } : k)) ?? prev,
      );
    } catch {
      // Non-fatal.
    }
  }

  async function handleCreateWebhook(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth) return;

    setWebhookError(null);
    setCreatingWebhook(true);
    try {
      const created = await apiFetch<Webhook & { secret: string }>("/api/webhooks", {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({ url: newWebhookUrl, event: newWebhookEvent }),
      });
      setWebhooks((prev) => (prev ? [created, ...prev] : [created]));
      setJustCreatedSecret(created.secret);
      setNewWebhookUrl("");
    } catch (err) {
      setWebhookError(err instanceof ApiError ? err.message : "Couldn't create webhook.");
    } finally {
      setCreatingWebhook(false);
    }
  }

  async function handleToggleWebhook(webhook: Webhook) {
    const auth = tenantAuth.get();
    if (!auth) return;
    try {
      const updated = await apiFetch<Webhook>(`/api/webhooks/${webhook.id}`, {
        method: "PATCH",
        token: auth.accessToken,
        body: JSON.stringify({ isActive: !webhook.isActive }),
      });
      setWebhooks((prev) => prev?.map((w) => (w.id === webhook.id ? updated : w)) ?? prev);
    } catch {
      // Non-fatal.
    }
  }

  async function handleDeleteWebhook(webhook: Webhook) {
    const auth = tenantAuth.get();
    if (!auth) return;
    try {
      await apiFetch(`/api/webhooks/${webhook.id}`, { method: "DELETE", token: auth.accessToken });
      setWebhooks((prev) => prev?.filter((w) => w.id !== webhook.id) ?? prev);
      if (logsFor === webhook.id) setLogsFor(null);
    } catch {
      // Non-fatal.
    }
  }

  async function handleShowLogs(webhook: Webhook) {
    const auth = tenantAuth.get();
    if (!auth) return;
    if (logsFor === webhook.id) {
      setLogsFor(null);
      return;
    }
    setLogsFor(webhook.id);
    setLogs(null);
    try {
      const data = await apiFetch<WebhookLog[]>(`/api/webhooks/${webhook.id}/logs`, {
        token: auth.accessToken,
      });
      setLogs(data);
    } catch {
      setLogs([]);
    }
  }

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4">
        <button onClick={() => router.push("/dashboard")} className="text-sm text-zinc-500 hover:underline">
          ← Back to tickets
        </button>
        <h1 className="mt-1 text-lg font-semibold">Integrations</h1>
      </header>

      <div className="p-8 space-y-10">
        <p className="max-w-2xl text-sm text-zinc-500">
          API keys let an external system call the public REST API at{" "}
          <code className="rounded bg-zinc-100 px-1 py-0.5 text-xs">{API_BASE_URL}/api/v1/tickets</code>{" "}
          using an <code className="rounded bg-zinc-100 px-1 py-0.5 text-xs">X-Api-Key</code> header instead
          of a staff login. Webhooks send a signed POST request to a URL you choose whenever a ticket is
          created or changes status. Admin-only, since both are standing credentials with broad access to
          this workspace&apos;s tickets.
        </p>

        {forbidden && <p className="text-sm text-zinc-500">Only Admins can manage integrations.</p>}
        {error && <p className="text-sm text-red-600">{error}</p>}

        {!forbidden && (
          <>
            {/* API Keys */}
            <section className="space-y-4">
              <h2 className="text-base font-semibold text-zinc-900">API keys</h2>

              <form
                onSubmit={handleCreateKey}
                className="flex max-w-xl items-end gap-3 rounded-lg border border-zinc-200 bg-white p-4"
              >
                <div className="flex-1">
                  <label className="block text-sm font-medium text-zinc-700 mb-1">Name</label>
                  <input
                    required
                    value={newKeyName}
                    onChange={(e) => setNewKeyName(e.target.value)}
                    placeholder="e.g. Zendesk sync"
                    className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                  />
                </div>
                <button
                  type="submit"
                  disabled={creatingKey}
                  className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
                >
                  {creatingKey ? "Creating…" : "Generate key"}
                </button>
              </form>
              {keyError && <p className="text-sm text-red-600">{keyError}</p>}

              {justCreatedKey && (
                <div className="max-w-xl rounded-lg border border-amber-300 bg-amber-50 p-4">
                  <p className="text-sm font-medium text-amber-900">
                    Copy this key now — it won&apos;t be shown again.
                  </p>
                  <code className="mt-2 block break-all rounded bg-white px-3 py-2 text-xs text-zinc-900">
                    {justCreatedKey}
                  </code>
                  <button
                    onClick={() => setJustCreatedKey(null)}
                    className="mt-2 text-xs text-amber-700 hover:underline"
                  >
                    Dismiss
                  </button>
                </div>
              )}

              {keys === null && !forbidden && !error && (
                <p className="text-sm text-zinc-500">Loading API keys…</p>
              )}
              {keys !== null && keys.length === 0 && (
                <p className="text-sm text-zinc-500">No API keys yet.</p>
              )}
              {keys !== null && keys.length > 0 && (
                <div className="overflow-hidden rounded-lg border border-zinc-200 bg-white">
                  <table className="w-full text-sm">
                    <thead className="bg-zinc-50 text-left text-xs font-medium uppercase text-zinc-500">
                      <tr>
                        <th className="px-4 py-3">Name</th>
                        <th className="px-4 py-3">Key</th>
                        <th className="px-4 py-3">Created</th>
                        <th className="px-4 py-3">Last used</th>
                        <th className="px-4 py-3">Status</th>
                        <th className="px-4 py-3"></th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-zinc-100">
                      {keys.map((k) => (
                        <tr key={k.id}>
                          <td className="px-4 py-3 font-medium text-zinc-900">{k.name}</td>
                          <td className="px-4 py-3 text-zinc-500">
                            <code className="text-xs">{k.keyPrefix}…</code>
                          </td>
                          <td className="px-4 py-3 text-zinc-500">
                            {new Date(k.createdAt).toLocaleDateString()}
                          </td>
                          <td className="px-4 py-3 text-zinc-500">
                            {k.lastUsedAt ? new Date(k.lastUsedAt).toLocaleString() : "Never"}
                          </td>
                          <td className="px-4 py-3">
                            {k.revokedAt ? (
                              <span className="rounded-full bg-zinc-100 px-2 py-0.5 text-xs text-zinc-600">
                                Revoked
                              </span>
                            ) : (
                              <span className="rounded-full bg-green-100 px-2 py-0.5 text-xs text-green-700">
                                Active
                              </span>
                            )}
                          </td>
                          <td className="px-4 py-3 text-right">
                            {!k.revokedAt && (
                              <button
                                onClick={() => handleRevokeKey(k)}
                                className="text-xs text-red-600 hover:underline"
                              >
                                Revoke
                              </button>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            {/* Webhooks */}
            <section className="space-y-4">
              <h2 className="text-base font-semibold text-zinc-900">Webhooks</h2>

              <form
                onSubmit={handleCreateWebhook}
                className="grid max-w-xl grid-cols-1 gap-4 rounded-lg border border-zinc-200 bg-white p-4 sm:grid-cols-3"
              >
                <div className="sm:col-span-2">
                  <label className="block text-sm font-medium text-zinc-700 mb-1">URL (https only)</label>
                  <input
                    required
                    type="url"
                    value={newWebhookUrl}
                    onChange={(e) => setNewWebhookUrl(e.target.value)}
                    placeholder="https://example.com/hooks/tms"
                    className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-zinc-700 mb-1">Event</label>
                  <select
                    value={newWebhookEvent}
                    onChange={(e) => setNewWebhookEvent(e.target.value)}
                    className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                  >
                    {EVENTS.map((ev) => (
                      <option key={ev.value} value={ev.value}>
                        {ev.label}
                      </option>
                    ))}
                  </select>
                </div>
                {webhookError && <p className="sm:col-span-3 text-sm text-red-600">{webhookError}</p>}
                <div className="sm:col-span-3">
                  <button
                    type="submit"
                    disabled={creatingWebhook}
                    className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
                  >
                    {creatingWebhook ? "Creating…" : "Add webhook"}
                  </button>
                </div>
              </form>

              {justCreatedSecret && (
                <div className="max-w-xl rounded-lg border border-amber-300 bg-amber-50 p-4">
                  <p className="text-sm font-medium text-amber-900">
                    Copy this signing secret now — it won&apos;t be shown again. Every delivery includes an
                    <code className="mx-1 rounded bg-white px-1 py-0.5 text-xs">X-Tms-Signature</code>
                    header (HMAC-SHA256 of the request body, hex-encoded) computed with this secret.
                  </p>
                  <code className="mt-2 block break-all rounded bg-white px-3 py-2 text-xs text-zinc-900">
                    {justCreatedSecret}
                  </code>
                  <button
                    onClick={() => setJustCreatedSecret(null)}
                    className="mt-2 text-xs text-amber-700 hover:underline"
                  >
                    Dismiss
                  </button>
                </div>
              )}

              {webhooks === null && !forbidden && (
                <p className="text-sm text-zinc-500">Loading webhooks…</p>
              )}
              {webhooks !== null && webhooks.length === 0 && (
                <p className="text-sm text-zinc-500">No webhooks configured yet.</p>
              )}
              {webhooks !== null && webhooks.length > 0 && (
                <div className="space-y-3">
                  {webhooks.map((w) => (
                    <div key={w.id} className="rounded-lg border border-zinc-200 bg-white p-4">
                      <div className="flex items-start justify-between">
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="rounded-full bg-zinc-100 px-2 py-0.5 text-xs text-zinc-700">
                              {EVENTS.find((e) => e.value === w.event)?.label ?? w.event}
                            </span>
                            <span
                              className={`rounded-full px-2 py-0.5 text-xs ${
                                w.isActive ? "bg-green-100 text-green-700" : "bg-zinc-100 text-zinc-600"
                              }`}
                            >
                              {w.isActive ? "Active" : "Paused"}
                            </span>
                          </div>
                          <p className="mt-1 break-all text-sm text-zinc-700">{w.url}</p>
                        </div>
                        <div className="flex shrink-0 gap-3 text-xs">
                          <button onClick={() => handleShowLogs(w)} className="text-zinc-500 hover:underline">
                            Logs
                          </button>
                          <button
                            onClick={() => handleToggleWebhook(w)}
                            className="text-zinc-500 hover:underline"
                          >
                            {w.isActive ? "Pause" : "Resume"}
                          </button>
                          <button
                            onClick={() => handleDeleteWebhook(w)}
                            className="text-red-600 hover:underline"
                          >
                            Delete
                          </button>
                        </div>
                      </div>

                      {logsFor === w.id && (
                        <div className="mt-3 border-t border-zinc-100 pt-3">
                          {logs === null && <p className="text-xs text-zinc-500">Loading deliveries…</p>}
                          {logs !== null && logs.length === 0 && (
                            <p className="text-xs text-zinc-500">No deliveries yet.</p>
                          )}
                          {logs !== null && logs.length > 0 && (
                            <ul className="space-y-1">
                              {logs.map((l) => (
                                <li key={l.id} className="text-xs text-zinc-500">
                                  <span
                                    className={l.success ? "text-green-700" : "text-red-600"}
                                  >
                                    {l.success ? "✓" : "✗"}
                                  </span>{" "}
                                  {new Date(l.attemptedAt).toLocaleString()} — {l.statusCode ?? "no response"}
                                  {l.error ? ` (${l.error})` : ""}
                                </li>
                              ))}
                            </ul>
                          )}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </section>
          </>
        )}
      </div>
    </main>
  );
}
