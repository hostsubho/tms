"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
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
  createdAt: string;
}

const STATUS_STYLES: Record<string, string> = {
  Active: "bg-green-100 text-green-700",
  InRepair: "bg-amber-100 text-amber-700",
  Retired: "bg-zinc-100 text-zinc-500",
};

export default function AssetsPage() {
  const router = useRouter();
  const [role, setRole] = useState<string | null>(null);
  const [permissions, setPermissions] = useState<string[]>([]);
  const [assets, setAssets] = useState<Asset[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const [showCreate, setShowCreate] = useState(false);
  const [name, setName] = useState("");
  const [type, setType] = useState<"Hardware" | "Software">("Hardware");
  const [serial, setSerial] = useState("");
  const [location, setLocation] = useState("");
  const [creating, setCreating] = useState(false);

  const load = useCallback(async (token: string) => {
    try {
      const data = await apiFetch<Asset[]>("/api/assets", { token });
      setAssets(data);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        tenantAuth.clear();
        router.replace("/login");
        return;
      }
      if (err instanceof ApiError && err.status === 403) {
        setForbidden(true);
        return;
      }
      setError(err instanceof ApiError ? err.message : "Couldn't load assets.");
    }
  }, [router]);

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

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth) return;

    setActionError(null);
    setCreating(true);
    try {
      await apiFetch("/api/assets", {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({
          name,
          type,
          serialNumberOrLicenseKey: serial || null,
          location: location || null,
        }),
      });
      setName("");
      setSerial("");
      setLocation("");
      setShowCreate(false);
      await load(auth.accessToken);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Couldn't create asset.");
    } finally {
      setCreating(false);
    }
  }

  if (forbidden) {
    return (
      <main className="min-h-screen bg-zinc-50 p-8">
        <button onClick={() => router.push("/dashboard")} className="text-sm text-zinc-500 hover:underline">
          ← Back to tickets
        </button>
        <p className="mt-4 text-sm text-zinc-600">
          Asset Management (CMDB) isn&apos;t enabled for this workspace yet. Contact WMX to turn it on.
        </p>
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4 flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold">Assets</h1>
          <p className="text-sm text-zinc-500">Hardware and software inventory</p>
        </div>
        <div className="flex items-center gap-2">
          {canManage && (
            <button
              onClick={() => setShowCreate((v) => !v)}
              className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-zinc-800"
            >
              {showCreate ? "Cancel" : "New asset"}
            </button>
          )}
          <button
            onClick={() => router.push("/dashboard")}
            className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm text-zinc-700 hover:bg-zinc-100"
          >
            Back to tickets
          </button>
        </div>
      </header>

      <div className="p-8 space-y-6">
        {showCreate && canManage && (
          <form
            onSubmit={handleCreate}
            className="rounded-lg border border-zinc-200 bg-white p-6 grid grid-cols-1 gap-4 sm:grid-cols-4"
          >
            <div className="sm:col-span-2">
              <label className="block text-sm text-zinc-600 mb-1">Name</label>
              <input
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-sm text-zinc-600 mb-1">Type</label>
              <select
                value={type}
                onChange={(e) => setType(e.target.value as "Hardware" | "Software")}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              >
                <option value="Hardware">Hardware</option>
                <option value="Software">Software</option>
              </select>
            </div>
            <div>
              <label className="block text-sm text-zinc-600 mb-1">
                {type === "Hardware" ? "Serial number" : "License key"}
              </label>
              <input
                value={serial}
                onChange={(e) => setSerial(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div className="sm:col-span-2">
              <label className="block text-sm text-zinc-600 mb-1">Location (optional)</label>
              <input
                value={location}
                onChange={(e) => setLocation(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div className="sm:col-span-2 flex items-end">
              <button
                type="submit"
                disabled={creating}
                className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
              >
                {creating ? "Creating…" : "Create asset"}
              </button>
            </div>
          </form>
        )}

        {actionError && <p className="text-sm text-red-600">{actionError}</p>}
        {error && <p className="text-sm text-red-600">{error}</p>}
        {assets === null && !error && <p className="text-sm text-zinc-500">Loading assets…</p>}
        {assets !== null && assets.length === 0 && <p className="text-sm text-zinc-500">No assets yet.</p>}

        {assets !== null && assets.length > 0 && (
          <div className="overflow-hidden rounded-lg border border-zinc-200 bg-white">
            <table className="w-full text-sm">
              <thead className="bg-zinc-50 text-left text-xs font-medium uppercase text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">Type</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">Serial/License</th>
                  <th className="px-4 py-3">Location</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-100">
                {assets.map((a) => (
                  <tr
                    key={a.id}
                    onClick={() => router.push(`/dashboard/assets/${a.id}`)}
                    className="cursor-pointer hover:bg-zinc-50"
                  >
                    <td className="px-4 py-3 font-medium text-zinc-900">{a.name}</td>
                    <td className="px-4 py-3 text-zinc-600">{a.type}</td>
                    <td className="px-4 py-3">
                      <span
                        className={`inline-block rounded-full px-2 py-0.5 text-xs ${
                          STATUS_STYLES[a.status] ?? "bg-zinc-100 text-zinc-600"
                        }`}
                      >
                        {a.status}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-zinc-400">{a.serialNumberOrLicenseKey ?? "—"}</td>
                    <td className="px-4 py-3 text-zinc-400">{a.location ?? "—"}</td>
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
