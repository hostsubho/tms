"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { platformAuth } from "@/lib/auth";

interface PlanDistributionEntry {
  planId: string;
  planName: string;
  tenantCount: number;
  mrr: number;
}

interface Revenue {
  mrr: number;
  arr: number;
  planDistribution: PlanDistributionEntry[];
  newTenantsLast30Days: number;
  churnedTenantsLast30Days: number;
}

export default function RevenuePage() {
  const router = useRouter();
  const [data, setData] = useState<Revenue | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const auth = platformAuth.get();
    if (!auth) {
      router.replace("/admin/login");
      return;
    }

    apiFetch<Revenue>("/api/platform/billing/revenue", { token: auth.accessToken })
      .then(setData)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          platformAuth.clear();
          router.replace("/admin/login");
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load revenue.");
      });
  }, [router]);

  return (
    <main className="min-h-screen bg-zinc-950 text-zinc-100">
      <header className="border-b border-zinc-800 px-8 py-4 flex items-center justify-between">
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-zinc-500">TMS Platform</p>
          <h1 className="text-lg font-semibold">Revenue</h1>
          <p className="text-sm text-zinc-500">Computed live from currently paying tenants</p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => router.push("/admin/tenants")}
            className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm text-zinc-300 hover:bg-zinc-900"
          >
            Tenants
          </button>
          <button
            onClick={() => router.push("/admin/plans")}
            className="rounded-md border border-zinc-700 px-3 py-1.5 text-sm text-zinc-300 hover:bg-zinc-900"
          >
            Plans
          </button>
        </div>
      </header>

      <div className="p-8 space-y-6">
        {error && <p className="text-sm text-red-400">{error}</p>}
        {!data && !error && <p className="text-sm text-zinc-500">Loading…</p>}

        {data && (
          <>
            <section className="grid grid-cols-2 gap-4 sm:grid-cols-4">
              <StatCard label="MRR" value={`$${data.mrr.toFixed(2)}`} />
              <StatCard label="ARR" value={`$${data.arr.toFixed(2)}`} />
              <StatCard label="New tenants (30d)" value={data.newTenantsLast30Days} />
              <StatCard label="Churned (30d)" value={data.churnedTenantsLast30Days} />
            </section>

            <section className="rounded-lg border border-zinc-800 bg-zinc-900 p-6">
              <h2 className="mb-4 text-sm font-semibold">Plan distribution (paying tenants)</h2>
              {data.planDistribution.length === 0 ? (
                <p className="text-sm text-zinc-500">No paying tenants yet.</p>
              ) : (
                <table className="w-full text-sm">
                  <thead className="text-left text-xs font-medium uppercase text-zinc-500">
                    <tr>
                      <th className="py-2">Plan</th>
                      <th className="py-2">Tenants</th>
                      <th className="py-2">MRR contribution</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-zinc-800">
                    {data.planDistribution.map((d) => (
                      <tr key={d.planId}>
                        <td className="py-2 font-medium">{d.planName}</td>
                        <td className="py-2 text-zinc-400">{d.tenantCount}</td>
                        <td className="py-2 text-zinc-400">${d.mrr.toFixed(2)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </section>
          </>
        )}
      </div>
    </main>
  );
}

function StatCard({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-lg border border-zinc-800 bg-zinc-900 p-4">
      <p className="text-xs font-medium uppercase text-zinc-500">{label}</p>
      <p className="mt-1 text-2xl font-semibold">{value}</p>
    </div>
  );
}
