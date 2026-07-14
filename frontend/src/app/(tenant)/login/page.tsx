"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError, API_BASE_URL } from "@/lib/api";
import { tenantAuth, type TenantAuth } from "@/lib/auth";

export default function LoginPage() {
  const router = useRouter();
  const [tenantSlug, setTenantSlug] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  // Module 1 - SSO. A full browser navigation, not a fetch - the backend
  // redirects straight to the IdP from here, and eventually back to
  // /sso/callback with tokens in the URL fragment. Requires the workspace
  // slug up front (same as a password login) since that's how the backend
  // resolves which tenant's SSO config to use.
  function handleSsoLogin() {
    if (!tenantSlug) {
      setError("Enter your workspace first.");
      return;
    }
    window.location.href = `${API_BASE_URL}/api/auth/sso/${encodeURIComponent(tenantSlug)}/start`;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await apiFetch<Omit<TenantAuth, "tenantSlug">>("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ tenantSlug, email, password }),
      });
      tenantAuth.save({ ...res, tenantSlug });
      router.push("/dashboard");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong. Please try again.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-zinc-50 px-4">
      <div className="w-full max-w-sm rounded-lg border border-zinc-200 bg-white p-8 shadow-sm">
        <h1 className="text-xl font-semibold mb-1">Sign in to TMS</h1>
        <p className="text-sm text-zinc-500 mb-6">Enter your workspace and credentials.</p>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-zinc-700 mb-1">Workspace</label>
            <input
              type="text"
              required
              value={tenantSlug}
              onChange={(e) => setTenantSlug(e.target.value)}
              placeholder="acme"
              className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-zinc-700 mb-1">Email</label>
            <input
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-zinc-700 mb-1">Password</label>
            <input
              type="password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
            />
          </div>

          {error && <p className="text-sm text-red-600">{error}</p>}

          <button
            type="submit"
            disabled={loading}
            className="w-full rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
          >
            {loading ? "Signing in…" : "Sign in"}
          </button>
        </form>

        <div className="mt-4 flex items-center gap-3">
          <div className="h-px flex-1 bg-zinc-200" />
          <span className="text-xs text-zinc-400">or</span>
          <div className="h-px flex-1 bg-zinc-200" />
        </div>

        <button
          type="button"
          onClick={handleSsoLogin}
          className="mt-4 w-full rounded-md border border-zinc-300 px-4 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-50"
        >
          Sign in with SSO
        </button>

        <p className="mt-6 text-center text-sm text-zinc-500">
          New company?{" "}
          <a href="/signup" className="font-medium text-zinc-900 hover:underline">
            Start a free trial
          </a>
        </p>
      </div>
    </main>
  );
}
