"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface CustomRole {
  id: string;
  name: string;
  permissions: string[];
  createdAt: string;
}

interface User {
  id: string;
  email: string;
  role: string;
  isActive: boolean;
  customRoleId: string | null;
  customRoleName: string | null;
}

const PERMISSIONS: { value: string; label: string }[] = [
  { value: "ManageCategories", label: "Manage categories" },
  { value: "ManageSlaPolicies", label: "Manage SLA policies" },
  { value: "ManageAutomationRules", label: "Manage automation rules" },
  { value: "ManageKnowledgeArticles", label: "Manage knowledge base articles" },
  { value: "ViewAuditLog", label: "View audit log" },
];

export default function RolesPage() {
  const router = useRouter();
  const [roles, setRoles] = useState<CustomRole[] | null>(null);
  const [users, setUsers] = useState<User[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);

  const [showCreate, setShowCreate] = useState(false);
  const [name, setName] = useState("");
  const [permissions, setPermissions] = useState<string[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  function loadAll(token: string) {
    apiFetch<CustomRole[]>("/api/custom-roles", { token })
      .then((data) => {
        setRoles(data);
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
        setError(err instanceof ApiError ? err.message : "Couldn't load roles.");
      });

    apiFetch<User[]>("/api/users", { token })
      .then(setUsers)
      .catch(() => {
        // Non-fatal: role-assignment table just stays empty.
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

  function resetForm() {
    setName("");
    setPermissions([]);
    setEditingId(null);
    setShowCreate(false);
    setSaveError(null);
  }

  function startEdit(r: CustomRole) {
    setEditingId(r.id);
    setName(r.name);
    setPermissions(r.permissions);
    setShowCreate(true);
  }

  function togglePermission(value: string) {
    setPermissions((prev) => (prev.includes(value) ? prev.filter((p) => p !== value) : [...prev, value]));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth) return;

    setSaveError(null);
    setSaving(true);
    try {
      if (editingId) {
        const updated = await apiFetch<CustomRole>(`/api/custom-roles/${editingId}`, {
          method: "PATCH",
          token: auth.accessToken,
          body: JSON.stringify({ name, permissions }),
        });
        setRoles((prev) => prev?.map((r) => (r.id === editingId ? updated : r)) ?? prev);
      } else {
        const created = await apiFetch<CustomRole>("/api/custom-roles", {
          method: "POST",
          token: auth.accessToken,
          body: JSON.stringify({ name, permissions }),
        });
        setRoles((prev) => (prev ? [...prev, created] : [created]));
      }
      resetForm();
    } catch (err) {
      setSaveError(err instanceof ApiError ? err.message : "Couldn't save role.");
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(r: CustomRole) {
    const auth = tenantAuth.get();
    if (!auth) return;
    try {
      await apiFetch(`/api/custom-roles/${r.id}`, { method: "DELETE", token: auth.accessToken });
      setRoles((prev) => prev?.filter((x) => x.id !== r.id) ?? prev);
      // Any user holding this role just lost it server-side - refresh the
      // assignment table so it doesn't keep showing a now-deleted role name.
      loadAll(auth.accessToken);
    } catch {
      // Non-fatal.
    }
  }

  async function handleAssign(userId: string, customRoleId: string) {
    const auth = tenantAuth.get();
    if (!auth) return;
    try {
      const updated = await apiFetch<User>(`/api/users/${userId}/custom-role`, {
        method: "PATCH",
        token: auth.accessToken,
        body: JSON.stringify({ customRoleId: customRoleId || null }),
      });
      setUsers((prev) => prev?.map((u) => (u.id === userId ? updated : u)) ?? prev);
    } catch {
      // Non-fatal: dropdown just doesn't update.
    }
  }

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4 flex items-center justify-between">
        <div>
          <button onClick={() => router.push("/dashboard")} className="text-sm text-zinc-500 hover:underline">
            ← Back to tickets
          </button>
          <h1 className="mt-1 text-lg font-semibold">Roles &amp; Permissions</h1>
        </div>
        <button
          onClick={() => (showCreate ? resetForm() : setShowCreate(true))}
          className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-zinc-800"
        >
          {showCreate ? "Cancel" : "New custom role"}
        </button>
      </header>

      <div className="p-8 space-y-8">
        <p className="max-w-2xl text-sm text-zinc-500">
          Admins and Managers already have full access everywhere. A custom
          role lets you grant an Agent or Read-only user access to one or
          more specific modules — SLA policies, automation rules, the
          knowledge base, categories, or the audit log — without promoting
          them to Manager. Admin-only to create, edit, or assign.
        </p>

        {forbidden && (
          <p className="text-sm text-zinc-500">Only Admins can manage custom roles.</p>
        )}

        {!forbidden && (
          <>
            {showCreate && (
              <form
                onSubmit={handleSubmit}
                className="rounded-lg border border-zinc-200 bg-white p-6 grid grid-cols-1 gap-4 max-w-xl"
              >
                <div>
                  <label className="block text-sm font-medium text-zinc-700 mb-1">Role name</label>
                  <input
                    required
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="e.g. KB Editor"
                    className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-zinc-700 mb-2">Permissions</label>
                  <div className="space-y-2">
                    {PERMISSIONS.map((p) => (
                      <label key={p.value} className="flex items-center gap-2 text-sm text-zinc-700">
                        <input
                          type="checkbox"
                          checked={permissions.includes(p.value)}
                          onChange={() => togglePermission(p.value)}
                        />
                        {p.label}
                      </label>
                    ))}
                  </div>
                </div>

                {saveError && <p className="text-sm text-red-600">{saveError}</p>}

                <div>
                  <button
                    type="submit"
                    disabled={saving}
                    className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
                  >
                    {saving ? "Saving…" : editingId ? "Save changes" : "Create role"}
                  </button>
                </div>
              </form>
            )}

            {error && <p className="text-sm text-red-600">{error}</p>}
            {roles === null && !error && <p className="text-sm text-zinc-500">Loading roles…</p>}
            {roles !== null && roles.length === 0 && (
              <p className="text-sm text-zinc-500">No custom roles yet.</p>
            )}

            {roles !== null && roles.length > 0 && (
              <div className="space-y-3">
                {roles.map((r) => (
                  <div key={r.id} className="rounded-lg border border-zinc-200 bg-white p-4">
                    <div className="flex items-start justify-between">
                      <div>
                        <h3 className="font-medium text-zinc-900">{r.name}</h3>
                        <p className="mt-1 text-sm text-zinc-500">
                          {r.permissions.length > 0
                            ? r.permissions
                                .map((p) => PERMISSIONS.find((x) => x.value === p)?.label ?? p)
                                .join(", ")
                            : "No permissions granted"}
                        </p>
                      </div>
                      <div className="flex shrink-0 gap-3 text-xs">
                        <button onClick={() => startEdit(r)} className="text-zinc-500 hover:underline">
                          Edit
                        </button>
                        <button onClick={() => handleDelete(r)} className="text-red-600 hover:underline">
                          Delete
                        </button>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}

            <div>
              <h2 className="mb-3 text-sm font-semibold text-zinc-900">Assign roles to users</h2>
              {users === null && <p className="text-sm text-zinc-500">Loading users…</p>}
              {users !== null && users.length > 0 && (
                <div className="overflow-hidden rounded-lg border border-zinc-200 bg-white">
                  <table className="w-full text-sm">
                    <thead className="bg-zinc-50 text-left text-xs font-medium uppercase text-zinc-500">
                      <tr>
                        <th className="px-4 py-3">User</th>
                        <th className="px-4 py-3">Base role</th>
                        <th className="px-4 py-3">Custom role</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-zinc-100">
                      {users.map((u) => (
                        <tr key={u.id}>
                          <td className="px-4 py-3 font-medium text-zinc-900">{u.email}</td>
                          <td className="px-4 py-3 text-zinc-500">{u.role}</td>
                          <td className="px-4 py-3">
                            <select
                              value={u.customRoleId ?? ""}
                              onChange={(e) => handleAssign(u.id, e.target.value)}
                              className="rounded-md border border-zinc-300 px-2 py-1 text-sm focus:border-zinc-500 focus:outline-none"
                            >
                              <option value="">No custom role</option>
                              {(roles ?? []).map((r) => (
                                <option key={r.id} value={r.id}>
                                  {r.name}
                                </option>
                              ))}
                            </select>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </>
        )}
      </div>
    </main>
  );
}
