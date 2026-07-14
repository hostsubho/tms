"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { tenantAuth } from "@/lib/auth";

// Module 1 - SSO. The landing point after SsoAuthController redirects back
// from the IdP (or from its own error path) - reads the URL fragment (never
// sent to any server, see SsoAuthController.RedirectToFrontendWithTokens'
// doc comment on why fragment-not-query) and reassembles it into the same
// TenantAuth shape login/page.tsx builds from a password login, then
// forwards to /dashboard exactly like a normal login would.
export default function SsoCallbackPage() {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const fragment = window.location.hash.startsWith("#") ? window.location.hash.slice(1) : window.location.hash;
    const params = new URLSearchParams(fragment);

    const errorMessage = params.get("error");
    if (errorMessage) {
      setError(errorMessage);
      return;
    }

    const accessToken = params.get("access_token");
    const refreshToken = params.get("refresh_token");
    const expiresAt = params.get("expires_at");
    const userId = params.get("user_id");
    const email = params.get("email");
    const role = params.get("role");
    const tenantSlug = params.get("tenant_slug");
    const permissions = params.get("permissions");

    if (!accessToken || !refreshToken || !expiresAt || !userId || !email || !role || !tenantSlug) {
      setError("The sign-in response was missing required information. Please try again.");
      return;
    }

    tenantAuth.save({
      accessToken,
      accessTokenExpiresAtUtc: expiresAt,
      refreshToken,
      userId,
      email,
      role,
      tenantSlug,
      permissions: permissions ? permissions.split(",").filter(Boolean) : [],
    });

    // Clear the fragment from history before navigating away - tokens
    // shouldn't linger in the browser's back/forward history entry either.
    window.history.replaceState(null, "", window.location.pathname);
    router.replace("/dashboard");
  }, [router]);

  return (
    <main className="flex min-h-screen items-center justify-center bg-zinc-50 px-4">
      <div className="w-full max-w-sm rounded-lg border border-zinc-200 bg-white p-8 text-center shadow-sm">
        {error ? (
          <>
            <h1 className="text-lg font-semibold text-red-700">Sign-in failed</h1>
            <p className="mt-2 text-sm text-zinc-600">{error}</p>
            <a href="/login" className="mt-4 inline-block text-sm font-medium text-zinc-900 hover:underline">
              Back to sign in
            </a>
          </>
        ) : (
          <p className="text-sm text-zinc-500">Signing you in…</p>
        )}
      </div>
    </main>
  );
}
