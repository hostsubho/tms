"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface Article {
  id: string;
  title: string;
  body: string;
  isPublic: boolean;
  categoryId: string | null;
  viewCount: number;
  helpfulYesCount: number;
  helpfulNoCount: number;
  createdAt: string;
  updatedAt: string;
}

interface ArticleVersion {
  id: string;
  title: string;
  body: string;
  editedAt: string;
}

export default function KnowledgeBasePage() {
  const router = useRouter();
  const [articles, setArticles] = useState<Article[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [role, setRole] = useState<string | null>(null);
  const [permissions, setPermissions] = useState<string[]>([]);

  const [showCreate, setShowCreate] = useState(false);
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [isPublic, setIsPublic] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [versionsFor, setVersionsFor] = useState<string | null>(null);
  const [versions, setVersions] = useState<ArticleVersion[] | null>(null);

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }
    setRole(auth.role);
    setPermissions(auth.permissions);

    apiFetch<Article[]>("/api/knowledge-articles", { token: auth.accessToken })
      .then(setArticles)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          tenantAuth.clear();
          router.replace("/login");
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load articles.");
      });
  }, [router]);

  const canManage = role === "Admin" || role === "Manager" || permissions.includes("ManageKnowledgeArticles");

  function resetForm() {
    setTitle("");
    setBody("");
    setIsPublic(true);
    setEditingId(null);
    setShowCreate(false);
    setSaveError(null);
  }

  function startEdit(article: Article) {
    setEditingId(article.id);
    setTitle(article.title);
    setBody(article.body);
    setIsPublic(article.isPublic);
    setShowCreate(true);
    setVersionsFor(null);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth) return;

    setSaveError(null);
    setSaving(true);
    try {
      if (editingId) {
        const updated = await apiFetch<Article>(`/api/knowledge-articles/${editingId}`, {
          method: "PATCH",
          token: auth.accessToken,
          body: JSON.stringify({ title, body, isPublic }),
        });
        setArticles((prev) => prev?.map((a) => (a.id === editingId ? updated : a)) ?? prev);
      } else {
        const created = await apiFetch<Article>("/api/knowledge-articles", {
          method: "POST",
          token: auth.accessToken,
          body: JSON.stringify({ title, body, isPublic, categoryId: null }),
        });
        setArticles((prev) => (prev ? [created, ...prev] : [created]));
      }
      resetForm();
    } catch (err) {
      setSaveError(err instanceof ApiError ? err.message : "Couldn't save article.");
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(article: Article) {
    const auth = tenantAuth.get();
    if (!auth) return;
    try {
      await apiFetch(`/api/knowledge-articles/${article.id}`, { method: "DELETE", token: auth.accessToken });
      setArticles((prev) => prev?.filter((a) => a.id !== article.id) ?? prev);
    } catch {
      // Non-fatal.
    }
  }

  async function handleShowVersions(article: Article) {
    const auth = tenantAuth.get();
    if (!auth) return;
    if (versionsFor === article.id) {
      setVersionsFor(null);
      return;
    }
    setVersionsFor(article.id);
    setVersions(null);
    try {
      const v = await apiFetch<ArticleVersion[]>(`/api/knowledge-articles/${article.id}/versions`, {
        token: auth.accessToken,
      });
      setVersions(v);
    } catch {
      setVersions([]);
    }
  }

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4 flex items-center justify-between">
        <div>
          <button onClick={() => router.push("/dashboard")} className="text-sm text-zinc-500 hover:underline">
            ← Back to tickets
          </button>
          <h1 className="mt-1 text-lg font-semibold">Knowledge Base</h1>
        </div>
        {canManage && (
          <button
            onClick={() => (showCreate ? resetForm() : setShowCreate(true))}
            className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-zinc-800"
          >
            {showCreate ? "Cancel" : "New article"}
          </button>
        )}
      </header>

      <div className="p-8 space-y-6">
        <p className="text-sm text-zinc-500 max-w-2xl">
          Public articles are suggested to customers in the portal as they type a ticket
          subject. Internal-only articles never leave this staff view.
        </p>

        {showCreate && canManage && (
          <form
            onSubmit={handleSubmit}
            className="rounded-lg border border-zinc-200 bg-white p-6 grid grid-cols-1 gap-4"
          >
            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">Title</label>
              <input
                required
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">Body</label>
              <textarea
                required
                rows={6}
                value={body}
                onChange={(e) => setBody(e.target.value)}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              />
            </div>
            <label className="flex items-center gap-2 text-sm text-zinc-700">
              <input type="checkbox" checked={isPublic} onChange={(e) => setIsPublic(e.target.checked)} />
              Visible to customers in the portal
            </label>

            {saveError && <p className="text-sm text-red-600">{saveError}</p>}

            <div>
              <button
                type="submit"
                disabled={saving}
                className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
              >
                {saving ? "Saving…" : editingId ? "Save changes" : "Create article"}
              </button>
            </div>
          </form>
        )}

        {error && <p className="text-sm text-red-600">{error}</p>}
        {articles === null && !error && <p className="text-sm text-zinc-500">Loading articles…</p>}
        {articles !== null && articles.length === 0 && (
          <p className="text-sm text-zinc-500">No articles yet.</p>
        )}

        {articles !== null && articles.length > 0 && (
          <div className="space-y-3">
            {articles.map((a) => (
              <div key={a.id} className="rounded-lg border border-zinc-200 bg-white p-4">
                <div className="flex items-start justify-between">
                  <div>
                    <div className="flex items-center gap-2">
                      <h3 className="font-medium text-zinc-900">{a.title}</h3>
                      <span
                        className={`rounded-full px-2 py-0.5 text-xs ${
                          a.isPublic ? "bg-green-100 text-green-700" : "bg-zinc-100 text-zinc-600"
                        }`}
                      >
                        {a.isPublic ? "Public" : "Internal only"}
                      </span>
                    </div>
                    <p className="mt-1 text-sm text-zinc-500 line-clamp-2">{a.body}</p>
                    <p className="mt-2 text-xs text-zinc-400">
                      {a.viewCount} views · {a.helpfulYesCount} found helpful · {a.helpfulNoCount} did not
                    </p>
                  </div>
                  <div className="flex shrink-0 gap-3 text-xs">
                    <button onClick={() => handleShowVersions(a)} className="text-zinc-500 hover:underline">
                      History
                    </button>
                    {canManage && (
                      <>
                        <button onClick={() => startEdit(a)} className="text-zinc-500 hover:underline">
                          Edit
                        </button>
                        <button onClick={() => handleDelete(a)} className="text-red-600 hover:underline">
                          Delete
                        </button>
                      </>
                    )}
                  </div>
                </div>

                {versionsFor === a.id && (
                  <div className="mt-3 border-t border-zinc-100 pt-3">
                    {versions === null && <p className="text-xs text-zinc-500">Loading history…</p>}
                    {versions !== null && versions.length === 0 && (
                      <p className="text-xs text-zinc-500">No earlier versions - this article hasn&apos;t been edited yet.</p>
                    )}
                    {versions !== null && versions.length > 0 && (
                      <ul className="space-y-2">
                        {versions.map((v) => (
                          <li key={v.id} className="text-xs text-zinc-500">
                            <span className="font-medium text-zinc-700">{new Date(v.editedAt).toLocaleString()}</span>
                            {" — "}
                            {v.title}
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
      </div>
    </main>
  );
}
