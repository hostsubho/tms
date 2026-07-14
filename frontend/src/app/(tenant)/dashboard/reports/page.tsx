"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface DailyCount {
  date: string;
  count: number;
}

interface TicketVolume {
  total: number;
  new: number;
  open: number;
  pending: number;
  resolved: number;
  closed: number;
  last30Days: DailyCount[];
}

interface SlaCompliance {
  totalWithSla: number;
  breached: number;
  compliancePercentage: number;
}

interface AgentPerformanceEntry {
  agentId: string;
  agentEmail: string;
  assignedCount: number;
  openCount: number;
  resolvedCount: number;
  breachedCount: number;
  avgResolutionHours: number | null;
  avgFirstResponseHours: number | null;
  slaCompliancePercentage: number;
  avgCsatRating: number | null;
}

interface TeamPerformance {
  activeAgentCount: number;
  totalAssigned: number;
  totalResolved: number;
  totalBreached: number;
  avgResolutionHours: number | null;
  avgFirstResponseHours: number | null;
  slaCompliancePercentage: number;
  avgCsatRating: number | null;
}

interface DailyCsat {
  date: string;
  averageRating: number;
  count: number;
}

interface Csat {
  averageRating: number | null;
  totalRatings: number;
  distribution: Record<string, number>;
  last30Days: DailyCsat[];
}

interface Dashboard {
  ticketVolume: TicketVolume;
  slaCompliance: SlaCompliance;
  teamPerformance: TeamPerformance;
  agentPerformance: AgentPerformanceEntry[];
  csat: Csat;
}

export default function ReportsPage() {
  const router = useRouter();
  const [data, setData] = useState<Dashboard | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }

    apiFetch<Dashboard>("/api/reports/dashboard", { token: auth.accessToken })
      .then(setData)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          tenantAuth.clear();
          router.replace("/login");
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load reports.");
      });
  }, [router]);

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4 flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold">Reports &amp; Analytics</h1>
          <p className="text-sm text-zinc-500">Last 30 days</p>
        </div>
        <button
          onClick={() => router.push("/dashboard")}
          className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm text-zinc-700 hover:bg-zinc-100"
        >
          Back to tickets
        </button>
      </header>

      <div className="p-8 space-y-6">
        {error && <p className="text-sm text-red-600">{error}</p>}
        {!data && !error && <p className="text-sm text-zinc-500">Loading…</p>}

        {data && (
          <>
            <section className="grid grid-cols-2 gap-4 sm:grid-cols-4">
              <StatCard label="Total tickets" value={data.ticketVolume.total} />
              <StatCard
                label="SLA compliance"
                value={`${data.slaCompliance.compliancePercentage}%`}
                sub={`${data.slaCompliance.breached} breached / ${data.slaCompliance.totalWithSla} tracked`}
              />
              <StatCard
                label="Avg CSAT"
                value={data.csat.averageRating !== null ? `${data.csat.averageRating} / 5` : "—"}
                sub={`${data.csat.totalRatings} ratings`}
              />
              <StatCard label="Open now" value={data.ticketVolume.new + data.ticketVolume.open + data.ticketVolume.pending} />
            </section>

            <section className="rounded-lg border border-zinc-200 bg-white p-6">
              <h2 className="mb-4 text-sm font-semibold text-zinc-900">Ticket volume by status</h2>
              <div className="flex gap-6 text-sm">
                <StatusPill label="New" count={data.ticketVolume.new} color="bg-blue-100 text-blue-700" />
                <StatusPill label="Open" count={data.ticketVolume.open} color="bg-amber-100 text-amber-700" />
                <StatusPill label="Pending" count={data.ticketVolume.pending} color="bg-purple-100 text-purple-700" />
                <StatusPill label="Resolved" count={data.ticketVolume.resolved} color="bg-green-100 text-green-700" />
                <StatusPill label="Closed" count={data.ticketVolume.closed} color="bg-zinc-100 text-zinc-600" />
              </div>
            </section>

            <section className="rounded-lg border border-zinc-200 bg-white p-6">
              <h2 className="mb-4 text-sm font-semibold text-zinc-900">Tickets created, last 30 days</h2>
              <DailyBarChart days={data.ticketVolume.last30Days} />
            </section>

            <section className="rounded-lg border border-zinc-200 bg-white p-6">
              <h2 className="mb-1 text-sm font-semibold text-zinc-900">Team performance</h2>
              <p className="mb-4 text-xs text-zinc-500">{data.teamPerformance.activeAgentCount} active agents</p>
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
                <StatCard label="Assigned" value={data.teamPerformance.totalAssigned} />
                <StatCard label="Resolved" value={data.teamPerformance.totalResolved} />
                <StatCard
                  label="SLA compliance"
                  value={`${data.teamPerformance.slaCompliancePercentage}%`}
                  sub={`${data.teamPerformance.totalBreached} breached`}
                />
                <StatCard
                  label="Avg first response"
                  value={data.teamPerformance.avgFirstResponseHours !== null ? `${data.teamPerformance.avgFirstResponseHours}h` : "—"}
                  sub={
                    data.teamPerformance.avgResolutionHours !== null
                      ? `${data.teamPerformance.avgResolutionHours}h avg resolution`
                      : undefined
                  }
                />
              </div>
            </section>

            <section className="rounded-lg border border-zinc-200 bg-white p-6">
              <h2 className="mb-4 text-sm font-semibold text-zinc-900">Employee performance</h2>
              {data.agentPerformance.length === 0 ? (
                <p className="text-sm text-zinc-500">No agents yet.</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead className="text-left text-xs font-medium uppercase text-zinc-500">
                      <tr>
                        <th className="py-2">Agent</th>
                        <th className="py-2">Assigned</th>
                        <th className="py-2">Open</th>
                        <th className="py-2">Resolved</th>
                        <th className="py-2">Breached</th>
                        <th className="py-2">SLA %</th>
                        <th className="py-2">Avg first response</th>
                        <th className="py-2">Avg resolution</th>
                        <th className="py-2">Avg CSAT</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-zinc-100">
                      {data.agentPerformance.map((a) => (
                        <tr key={a.agentId}>
                          <td className="py-2 font-medium text-zinc-900 whitespace-nowrap">{a.agentEmail}</td>
                          <td className="py-2 text-zinc-600">{a.assignedCount}</td>
                          <td className="py-2 text-zinc-600">{a.openCount}</td>
                          <td className="py-2 text-zinc-600">{a.resolvedCount}</td>
                          <td className="py-2">
                            {a.breachedCount > 0 ? (
                              <span className="rounded-full bg-red-100 px-2 py-0.5 text-xs text-red-700">
                                {a.breachedCount}
                              </span>
                            ) : (
                              <span className="text-zinc-400">0</span>
                            )}
                          </td>
                          <td className="py-2 text-zinc-600">{a.slaCompliancePercentage}%</td>
                          <td className="py-2 text-zinc-600">
                            {a.avgFirstResponseHours !== null ? `${a.avgFirstResponseHours}h` : "—"}
                          </td>
                          <td className="py-2 text-zinc-600">
                            {a.avgResolutionHours !== null ? `${a.avgResolutionHours}h` : "—"}
                          </td>
                          <td className="py-2 text-zinc-600">
                            {a.avgCsatRating !== null ? `${a.avgCsatRating} / 5` : "—"}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            <section className="rounded-lg border border-zinc-200 bg-white p-6">
              <h2 className="mb-4 text-sm font-semibold text-zinc-900">CSAT distribution</h2>
              {data.csat.totalRatings === 0 ? (
                <p className="text-sm text-zinc-500">No CSAT ratings submitted yet.</p>
              ) : (
                <div className="space-y-2">
                  {[5, 4, 3, 2, 1].map((star) => {
                    const count = data.csat.distribution[String(star)] ?? 0;
                    const pct = data.csat.totalRatings === 0 ? 0 : (count / data.csat.totalRatings) * 100;
                    return (
                      <div key={star} className="flex items-center gap-3 text-sm">
                        <span className="w-12 shrink-0 text-zinc-600">{"★".repeat(star)}</span>
                        <div className="h-2 flex-1 rounded-full bg-zinc-100">
                          <div className="h-2 rounded-full bg-amber-400" style={{ width: `${pct}%` }} />
                        </div>
                        <span className="w-8 shrink-0 text-right text-zinc-500">{count}</span>
                      </div>
                    );
                  })}
                </div>
              )}
            </section>
          </>
        )}
      </div>
    </main>
  );
}

function StatCard({ label, value, sub }: { label: string; value: string | number; sub?: string }) {
  return (
    <div className="rounded-lg border border-zinc-200 bg-white p-4">
      <p className="text-xs font-medium uppercase text-zinc-500">{label}</p>
      <p className="mt-1 text-2xl font-semibold text-zinc-900">{value}</p>
      {sub && <p className="mt-0.5 text-xs text-zinc-400">{sub}</p>}
    </div>
  );
}

function StatusPill({ label, count, color }: { label: string; count: number; color: string }) {
  return (
    <div className="flex items-center gap-2">
      <span className={`rounded-full px-2 py-0.5 text-xs ${color}`}>{label}</span>
      <span className="font-medium text-zinc-900">{count}</span>
    </div>
  );
}

function DailyBarChart({ days }: { days: DailyCount[] }) {
  const max = Math.max(1, ...days.map((d) => d.count));
  return (
    // items-stretch (not items-end) so each column div actually resolves to
    // the container's h-32 - a percentage height on the bar itself is only
    // meaningful against a parent with a resolved (non-auto) height, and
    // items-end would leave every column's height at its content size (0).
    <div className="flex h-32 items-stretch gap-1">
      {days.map((d) => (
        <div key={d.date} className="group relative flex flex-1 flex-col justify-end">
          <div
            className="pointer-events-none absolute -top-8 left-1/2 hidden -translate-x-1/2 whitespace-nowrap rounded bg-zinc-900 px-1.5 py-0.5 text-[10px] text-white group-hover:block"
          >
            {d.date}: {d.count}
          </div>
          <div
            className="rounded-t bg-zinc-800 transition-colors group-hover:bg-zinc-600"
            style={{ height: `${Math.max(2, (d.count / max) * 100)}%` }}
          />
        </div>
      ))}
    </div>
  );
}
