"use client";

import { useCallback, useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface Asset {
  id: string;
  name: string;
  type: string;
  status: string;
  serialNumberOrLicenseKey: string | null;
  assignedToUserId: string | null;
  location: string | null;
  purchaseDate: string | null;
  warrantyExpiresAt: string | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

interface TicketSummary {
  id: string;
  subject: string;
  status: string;
  priority: string;
  createdAt: string;
}

const STATUS_OPTIONS = ["Active", "InRepair", "Retired"];

export default function AssetDetailPage() {
  const router = useRouter();
  const params = useParams<{ id: string }>();
  const assetId = params.id;

  const [role, setRole] = useState<string | null>(null);
  const [permissions, setPermissions] = useState<string[]>([]);
  const [asset, setAsset] = useState<Asset | null>(null);
  const [tickets, setTickets] = useState<TicketSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [savingStatus, setSavingStatus] = useState(false);

  const load = useCallback(async (token: string) => {
    try {
      const [a, t] = await Promise.all([
        apiFetch<Asset>(`/api/assets/${assetId}`, { token }),
        apiFetch<TicketSummary[]>(`/api/assets/${assetId}/tickets`, { token }),
      ]);
      setAsset(a);
      setTickets(t);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        tenantAuth.clear();
        router.replace("/login");
        return;
      }
      setError(err instanceof ApiError ? err.message : "Couldn't load this asset.");
    }
  }, [assetId, router]);

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }
    setRole(auth.role);
    setPermissions(auth.permissions);
    load(auth.accessToken);
  }, [router, load]);

  const canManage = role === "Admin" || role === "Manager" || permissions.includes("ManageAssets");

  async function handleStatusChange(status: string) {
    const auth = tenantAuth.get();
    if (!auth) return;

    setActionError(null);
    setSavingStatus(true);
    try {
      const updated = await apiFetch<Asset>(`/api/assets/${assetId}`, {
        method: "PATCH",
        token: auth.accessToken,
        body: JSON.stringify({ status }),
      });
      setAsset(updated);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't update status.");
    } finally {
      setSavingStatus(false);
    }
  }

  if (error) {
    return (
      <main className="min-h-screen bg-zinc-50 p-8">
        <button onClick={() => router.push("/dashboard/assets")} className="text-sm text-zinc-500 hover:underline">
          ← Back to assets
        </button>
        <p className="mt-4 text-sm text-red-600">{error}</p>
      </main>
    );
  }

  if (!asset) {
    return (
      <main className="min-h-screen bg-zinc-50 p-8">
        <p className="text-sm text-zinc-500">Loading asset…</p>
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4">
        <button onClick={() => router.push("/dashboard/assets")} className="text-sm text-zinc-500 hover:underline">
          ← Back to assets
        </button>
        <h1 className="mt-2 text-xl font-semibold">{asset.name}</h1>
        <p className="text-xs text-zinc-400 mt-1">{asset.type}</p>
      </header>

      <div className="mx-auto max-w-3xl p-8 space-y-6">
        <div className="rounded-lg border border-zinc-200 bg-white p-6 space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-medium uppercase text-zinc-500 mb-1">Status</label>
              {canManage ? (
                <select
                  value={asset.status}
                  disabled={savingStatus}
                  onChange={(e) => handleStatusChange(e.target.value)}
                  className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none disabled:opacity-50"
                >
                  {STATUS_OPTIONS.map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
                </select>
              ) : (
                <p className="text-sm text-zinc-700">{asset.status}</p>
              )}
            </div>
            <div>
              <label className="block text-xs font-medium uppercase text-zinc-500 mb-1">
                {asset.type === "Hardware" ? "Serial number" : "License key"}
              </label>
              <p className="text-sm text-zinc-700">{asset.serialNumberOrLicenseKey ?? "—"}</p>
            </div>
          </div>

          {actionError && <p className="text-sm text-red-600">{actionError}</p>}

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-medium uppercase text-zinc-500 mb-1">Location</label>
              <p className="text-sm text-zinc-700">{asset.location ?? "—"}</p>
            </div>
            <div>
              <label className="block text-xs font-medium uppercase text-zinc-500 mb-1">Warranty expires</label>
              <p className="text-sm text-zinc-700">
                {asset.warrantyExpiresAt ? new Date(asset.warrantyExpiresAt).toLocaleDateString() : "—"}
              </p>
            </div>
          </div>

          {asset.notes && (
            <div>
              <label className="block text-xs font-medium uppercase text-zinc-500 mb-1">Notes</label>
              <p className="text-sm text-zinc-700 whitespace-pre-wrap">{asset.notes}</p>
            </div>
          )}
        </div>

        <div className="rounded-lg border border-zinc-200 bg-white p-6">
          <h2 className="text-sm font-semibold text-zinc-900 mb-4">Ticket history</h2>
          {tickets === null ? (
            <p className="text-sm text-zinc-500">Loading…</p>
          ) : tickets.length === 0 ? (
            <p className="text-sm text-zinc-500">No tickets linked to this asset yet.</p>
          ) : (
            <div className="space-y-2">
              {tickets.map((t) => (
                <button
                  key={t.id}
                  onClick={() => router.push(`/dashboard/tickets/${t.id}`)}
                  className="flex w-full items-center justify-between rounded-md border border-zinc-100 px-3 py-2 text-left text-sm hover:bg-zinc-50"
                >
                  <span className="font-medium text-zinc-900">{t.subject}</span>
                  <span className="text-xs text-zinc-400">
                    {t.status} · {t.priority} · {new Date(t.createdAt).toLocaleDateString()}
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    </main>
  );
}
