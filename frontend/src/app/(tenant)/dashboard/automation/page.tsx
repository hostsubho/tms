"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface AutomationRule {
  id: string;
  name: string;
  isActive: boolean;
  trigger: string;
  conditionField: string;
  conditionValue: string | null;
  actionType: string;
  actionValue: string | null;
  createdAt: string;
}

interface RuleLog {
  id: string;
  ruleId: string;
  ruleName: string;
  ticketId: string;
  ticketSubject: string;
  summary: string;
  firedAt: string;
}

interface Category {
  id: string;
  name: string;
}

interface UserOption {
  id: string;
  email: string;
  role: string;
}

const TRIGGERS = [
  { value: "TicketCreated", label: "Ticket created" },
  { value: "TicketUpdated", label: "Ticket updated" },
  { value: "CustomerReplyReceived", label: "Customer reply received" },
];

const CONDITION_FIELDS = [
  { value: "None", label: "No condition (always applies)" },
  { value: "Priority", label: "Priority is…" },
  { value: "Category", label: "Category is…" },
  { value: "SubjectContains", label: "Subject contains…" },
  { value: "DescriptionContains", label: "Description contains…" },
];

const ACTION_TYPES = [
  { value: "SetPriority", label: "Set priority" },
  { value: "SetStatus", label: "Set status" },
  { value: "AssignToAgent", label: "Assign to agent" },
  { value: "AssignRoundRobin", label: "Assign round-robin (least busy agent)" },
  { value: "Notify", label: "Notify admins" },
];

const PRIORITIES = ["Low", "Medium", "High", "Urgent"];
const STATUSES = ["New", "Open", "Pending", "Resolved", "Closed"];

function triggerLabel(t: string) {
  return TRIGGERS.find((x) => x.value === t)?.label ?? t;
}

function describeCondition(rule: AutomationRule) {
  switch (rule.conditionField) {
    case "Priority":
      return `Priority is ${rule.conditionValue}`;
    case "Category":
      return `Category is ${rule.conditionValue}`;
    case "SubjectContains":
      return `Subject contains "${rule.conditionValue}"`;
    case "DescriptionContains":
      return `Description contains "${rule.conditionValue}"`;
    default:
      return "Always";
  }
}

function describeAction(rule: AutomationRule) {
  switch (rule.actionType) {
    case "SetPriority":
      return `Set priority to ${rule.actionValue}`;
    case "SetStatus":
      return `Set status to ${rule.actionValue}`;
    case "AssignToAgent":
      return `Assign to agent`;
    case "AssignRoundRobin":
      return "Round-robin assign";
    case "Notify":
      return rule.actionValue ? `Notify admins: "${rule.actionValue}"` : "Notify admins";
    default:
      return rule.actionType;
  }
}

export default function AutomationRulesPage() {
  const router = useRouter();
  const [rules, setRules] = useState<AutomationRule[] | null>(null);
  const [logs, setLogs] = useState<RuleLog[] | null>(null);
  const [categories, setCategories] = useState<Category[]>([]);
  const [users, setUsers] = useState<UserOption[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [role, setRole] = useState<string | null>(null);

  const [showCreate, setShowCreate] = useState(false);
  const [name, setName] = useState("");
  const [trigger, setTrigger] = useState("TicketCreated");
  const [conditionField, setConditionField] = useState("None");
  const [conditionValue, setConditionValue] = useState("");
  const [actionType, setActionType] = useState("Notify");
  const [actionValue, setActionValue] = useState("");
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }
    setRole(auth.role);

    Promise.all([
      apiFetch<AutomationRule[]>("/api/automation-rules", { token: auth.accessToken }),
      apiFetch<RuleLog[]>("/api/automation-rules/logs", { token: auth.accessToken }),
      apiFetch<Category[]>("/api/categories", { token: auth.accessToken }).catch(() => []),
      apiFetch<UserOption[]>("/api/users", { token: auth.accessToken }).catch(() => []),
    ])
      .then(([r, l, c, u]) => {
        setRules(r);
        setLogs(l);
        setCategories(c);
        setUsers(u);
      })
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          tenantAuth.clear();
          router.replace("/login");
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load automation rules.");
      });
  }, [router]);

  const canManage = role === "Admin" || role === "Manager";

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth) return;

    setCreateError(null);
    setCreating(true);
    try {
      const rule = await apiFetch<AutomationRule>("/api/automation-rules", {
        method: "POST",
        token: auth.accessToken,
        body: JSON.stringify({
          name,
          trigger,
          conditionField,
          conditionValue: conditionField === "None" ? null : conditionValue || null,
          actionType,
          actionValue: actionType === "AssignRoundRobin" ? null : actionValue || null,
        }),
      });
      setRules((prev) => (prev ? [...prev, rule] : [rule]));
      setName("");
      setConditionValue("");
      setActionValue("");
      setShowCreate(false);
    } catch (err) {
      setCreateError(err instanceof ApiError ? err.message : "Couldn't create rule.");
    } finally {
      setCreating(false);
    }
  }

  async function handleToggle(rule: AutomationRule) {
    const auth = tenantAuth.get();
    if (!auth) return;
    try {
      const updated = await apiFetch<AutomationRule>(`/api/automation-rules/${rule.id}`, {
        method: "PATCH",
        token: auth.accessToken,
        body: JSON.stringify({ isActive: !rule.isActive }),
      });
      setRules((prev) => prev?.map((r) => (r.id === rule.id ? updated : r)) ?? prev);
    } catch {
      // Non-fatal: the row just doesn't update; user can retry.
    }
  }

  async function handleDelete(rule: AutomationRule) {
    const auth = tenantAuth.get();
    if (!auth) return;
    try {
      await apiFetch(`/api/automation-rules/${rule.id}`, { method: "DELETE", token: auth.accessToken });
      setRules((prev) => prev?.filter((r) => r.id !== rule.id) ?? prev);
    } catch {
      // Non-fatal.
    }
  }

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4 flex items-center justify-between">
        <div>
          <button onClick={() => router.push("/dashboard")} className="text-sm text-zinc-500 hover:underline">
            ← Back to tickets
          </button>
          <h1 className="mt-1 text-lg font-semibold">Automation Rules</h1>
        </div>
        {canManage && (
          <button
            onClick={() => setShowCreate((v) => !v)}
            className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-zinc-800"
          >
            {showCreate ? "Cancel" : "New rule"}
          </button>
        )}
      </header>

      <div className="p-8 space-y-6">
        <p className="text-sm text-zinc-500 max-w-2xl">
          Rules run the moment their trigger event happens - when a ticket is created,
          updated, or a customer replies - not on a schedule. Each rule checks one
          condition, then applies one action.
        </p>

        {showCreate && canManage && (
          <form
            onSubmit={handleCreate}
            className="rounded-lg border border-zinc-200 bg-white p-6 grid grid-cols-1 gap-4 sm:grid-cols-2"
          >
            <div className="sm:col-span-2">
              <label className="block text-sm font-medium text-zinc-700 mb-1">Name</label>
              <input
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">When</label>
              <select
                value={trigger}
                onChange={(e) => setTrigger(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              >
                {TRIGGERS.map((t) => (
                  <option key={t.value} value={t.value}>
                    {t.label}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">If</label>
              <select
                value={conditionField}
                onChange={(e) => {
                  setConditionField(e.target.value);
                  setConditionValue("");
                }}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              >
                {CONDITION_FIELDS.map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </select>
            </div>

            {conditionField === "Priority" && (
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">Priority equals</label>
                <select
                  required
                  value={conditionValue}
                  onChange={(e) => setConditionValue(e.target.value)}
                  className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                >
                  <option value="">Choose…</option>
                  {PRIORITIES.map((p) => (
                    <option key={p} value={p}>
                      {p}
                    </option>
                  ))}
                </select>
              </div>
            )}
            {conditionField === "Category" && (
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">Category equals</label>
                <select
                  required
                  value={conditionValue}
                  onChange={(e) => setConditionValue(e.target.value)}
                  className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                >
                  <option value="">Choose…</option>
                  {categories.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </select>
              </div>
            )}
            {(conditionField === "SubjectContains" || conditionField === "DescriptionContains") && (
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">Keyword</label>
                <input
                  required
                  value={conditionValue}
                  onChange={(e) => setConditionValue(e.target.value)}
                  className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                />
              </div>
            )}

            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">Then</label>
              <select
                value={actionType}
                onChange={(e) => {
                  setActionType(e.target.value);
                  setActionValue("");
                }}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              >
                {ACTION_TYPES.map((a) => (
                  <option key={a.value} value={a.value}>
                    {a.label}
                  </option>
                ))}
              </select>
            </div>

            {actionType === "SetPriority" && (
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">New priority</label>
                <select
                  required
                  value={actionValue}
                  onChange={(e) => setActionValue(e.target.value)}
                  className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                >
                  <option value="">Choose…</option>
                  {PRIORITIES.map((p) => (
                    <option key={p} value={p}>
                      {p}
                    </option>
                  ))}
                </select>
              </div>
            )}
            {actionType === "SetStatus" && (
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">New status</label>
                <select
                  required
                  value={actionValue}
                  onChange={(e) => setActionValue(e.target.value)}
                  className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                >
                  <option value="">Choose…</option>
                  {STATUSES.map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
                </select>
              </div>
            )}
            {actionType === "AssignToAgent" && (
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">Agent</label>
                <select
                  required
                  value={actionValue}
                  onChange={(e) => setActionValue(e.target.value)}
                  className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                >
                  <option value="">Choose…</option>
                  {users.map((u) => (
                    <option key={u.id} value={u.id}>
                      {u.email} ({u.role})
                    </option>
                  ))}
                </select>
              </div>
            )}
            {actionType === "Notify" && (
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">Message (optional)</label>
                <input
                  value={actionValue}
                  onChange={(e) => setActionValue(e.target.value)}
                  placeholder="Defaults to a generic 'rule matched' message"
                  className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                />
              </div>
            )}

            {createError && <p className="sm:col-span-2 text-sm text-red-600">{createError}</p>}

            <div className="sm:col-span-2">
              <button
                type="submit"
                disabled={creating}
                className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
              >
                {creating ? "Creating…" : "Create rule"}
              </button>
            </div>
          </form>
        )}

        {error && <p className="text-sm text-red-600">{error}</p>}
        {rules === null && !error && <p className="text-sm text-zinc-500">Loading rules…</p>}
        {rules !== null && rules.length === 0 && (
          <p className="text-sm text-zinc-500">No automation rules yet.</p>
        )}

        {rules !== null && rules.length > 0 && (
          <div className="overflow-hidden rounded-lg border border-zinc-200 bg-white">
            <table className="w-full text-sm">
              <thead className="bg-zinc-50 text-left text-xs font-medium uppercase text-zinc-500">
                <tr>
                  <th className="px-4 py-3">Name</th>
                  <th className="px-4 py-3">When</th>
                  <th className="px-4 py-3">If</th>
                  <th className="px-4 py-3">Then</th>
                  <th className="px-4 py-3">Active</th>
                  {canManage && <th className="px-4 py-3"></th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-100">
                {rules.map((r) => (
                  <tr key={r.id}>
                    <td className="px-4 py-3 font-medium text-zinc-900">{r.name}</td>
                    <td className="px-4 py-3 text-zinc-600">{triggerLabel(r.trigger)}</td>
                    <td className="px-4 py-3 text-zinc-600">{describeCondition(r)}</td>
                    <td className="px-4 py-3 text-zinc-600">{describeAction(r)}</td>
                    <td className="px-4 py-3">
                      {canManage ? (
                        <button
                          onClick={() => handleToggle(r)}
                          className={`rounded-full px-2 py-0.5 text-xs ${
                            r.isActive ? "bg-green-100 text-green-700" : "bg-zinc-100 text-zinc-500"
                          }`}
                        >
                          {r.isActive ? "Active" : "Paused"}
                        </button>
                      ) : (
                        <span
                          className={`rounded-full px-2 py-0.5 text-xs ${
                            r.isActive ? "bg-green-100 text-green-700" : "bg-zinc-100 text-zinc-500"
                          }`}
                        >
                          {r.isActive ? "Active" : "Paused"}
                        </span>
                      )}
                    </td>
                    {canManage && (
                      <td className="px-4 py-3">
                        <button
                          onClick={() => handleDelete(r)}
                          className="text-xs text-red-600 hover:underline"
                        >
                          Delete
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div>
          <h2 className="mb-3 text-sm font-semibold text-zinc-900">Recent activity</h2>
          {logs === null && <p className="text-sm text-zinc-500">Loading…</p>}
          {logs !== null && logs.length === 0 && (
            <p className="text-sm text-zinc-500">No rules have fired yet.</p>
          )}
          {logs !== null && logs.length > 0 && (
            <div className="overflow-hidden rounded-lg border border-zinc-200 bg-white">
              <table className="w-full text-sm">
                <thead className="bg-zinc-50 text-left text-xs font-medium uppercase text-zinc-500">
                  <tr>
                    <th className="px-4 py-3">Rule</th>
                    <th className="px-4 py-3">Ticket</th>
                    <th className="px-4 py-3">What happened</th>
                    <th className="px-4 py-3">When</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-zinc-100">
                  {logs.map((l) => (
                    <tr key={l.id}>
                      <td className="px-4 py-3 font-medium text-zinc-900">{l.ruleName}</td>
                      <td className="px-4 py-3 text-zinc-600">{l.ticketSubject}</td>
                      <td className="px-4 py-3 text-zinc-600">{l.summary}</td>
                      <td className="px-4 py-3 text-zinc-500">{new Date(l.firedAt).toLocaleString()}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </main>
  );
}
