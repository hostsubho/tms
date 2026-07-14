"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { platformAuth } from "@/lib/auth";

interface ImpersonationLogEntry {
  id: string;
  platformUserEmail: string;
  tenantId: string;
  tenantName: string;
  targetUserEmail: string;
  startedAt: string;
}

export default function ImpersonationLogsPage() {
  const router = useRouter();
  const [logs, setLogs] = useState<ImpersonationLogEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const auth = platformAuth.get();
    if (!auth) {
      router.replace("/admin/login");
      return;
    }

    apiFetch<ImpersonationLogEntry[]>("/api/platform/impersonation-logs", { token: auth.accessToken })
      .then(setLogs)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          platformAuth.clear();
          router.replace("/admin/login");
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load impersonation log.");
      });
  }, [router]);

  return (
    <main className="min-h-screen bg-zinc-950 text-zinc-100">
      <header className="border-b border-zinc-800 px-8 py-4 flex items-center justify-between">
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-zinc-500">TMS Platform</p>
          <h1 className="text-lg font-semibold">Impersonation log</h1>
          <p className="text-sm text-zinc-500">Who impersonated which tenant, as whom, and when</p>
        </div>
        <button
          onClick={() => router.push("/admin/tenants")}
          className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm text-zinc-300 hover:bg-zinc-900"
        >
          Back to tenants
        </button>
      </header>

      <div className="p-8">
        {error && <p className="text-sm text-red-400">{error}</p>}
        {logs === null && !error && <p className="text-sm text-zinc-500">Loading…</p>}
        {logs !== null && logs.length === 0 && <p className="text-sm text-zinc-500">No impersonation sessions yet.</p>}

        {logs !== null && logs.length > 0 && (
          <div className="overflow-hidden rounded-lg border border-zinc-800">
            <table className="w-full text-sm">
              <thead className="bg-zinc-900 text-left text-xs font-medium uppercase text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Started</th>
                  <th className="px-4 py-3">Super Admin</th>
                  <th className="px-4 py-3">Tenant</th>
                  <th className="px-4 py-3">Impersonated as</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800">
                {logs.map((l) => (
                  <tr key={l.id}>
                    <td className="px-4 py-3 text-zinc-400">{new Date(l.startedAt).toLocaleString()}</td>
                    <td className="px-4 py-3 font-medium">{l.platformUserEmail}</td>
                    <td className="px-4 py-3">
                      <button
                        onClick={() => router.push(`/admin/billing/${l.tenantId}`)}
                        className="text-zinc-300 hover:underline"
                      >
                        {l.tenantName}
                      </button>
                    </td>
                    <td className="px-4 py-3 text-zinc-400">{l.targetUserEmail}</td>
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
