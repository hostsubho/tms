"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface SsoConfig {
  isConfigured: boolean;
  protocol: "Oidc" | "Saml";
  enabled: boolean;
  oidcAuthority: string | null;
  oidcClientId: string | null;
  hasOidcClientSecret: boolean;
  samlIdpEntityId: string | null;
  samlIdpSsoUrl: string | null;
  hasSamlIdpCertificate: boolean;
  spEntityId: string;
  oidcRedirectUri: string;
  samlAcsUrl: string;
}

function CopyField({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <label className="block text-xs font-medium text-zinc-500 mb-1">{label}</label>
      <code className="block break-all rounded-md bg-zinc-100 px-3 py-2 text-xs text-zinc-700">{value}</code>
    </div>
  );
}

export default function SsoSettingsPage() {
  const router = useRouter();
  const [forbidden, setForbidden] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  const [config, setConfig] = useState<SsoConfig | null>(null);

  // Form state - kept separate from `config` (the last-saved server state)
  // so an in-progress edit doesn't get clobbered by a re-render, same
  // pattern IntegrationsController's page uses for its "new key"/"new
  // webhook" form fields.
  const [protocol, setProtocol] = useState<"Oidc" | "Saml">("Oidc");
  const [enabled, setEnabled] = useState(false);
  const [oidcAuthority, setOidcAuthority] = useState("");
  const [oidcClientId, setOidcClientId] = useState("");
  const [oidcClientSecret, setOidcClientSecret] = useState("");
  const [samlIdpEntityId, setSamlIdpEntityId] = useState("");
  const [samlIdpSsoUrl, setSamlIdpSsoUrl] = useState("");
  const [samlIdpCertificate, setSamlIdpCertificate] = useState("");

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }

    apiFetch<SsoConfig>("/api/tenant/sso", { token: auth.accessToken })
      .then((data) => {
        setConfig(data);
        setProtocol(data.protocol);
        setEnabled(data.enabled);
        setOidcAuthority(data.oidcAuthority ?? "");
        setOidcClientId(data.oidcClientId ?? "");
        setSamlIdpEntityId(data.samlIdpEntityId ?? "");
        setSamlIdpSsoUrl(data.samlIdpSsoUrl ?? "");
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
        setError(err instanceof ApiError ? err.message : "Couldn't load SSO settings.");
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [router]);

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth) return;

    setSaveError(null);
    setSaved(false);
    setSaving(true);
    try {
      const updated = await apiFetch<SsoConfig>("/api/tenant/sso", {
        method: "PUT",
        token: auth.accessToken,
        body: JSON.stringify({
          protocol,
          enabled,
          oidcAuthority: oidcAuthority || null,
          oidcClientId: oidcClientId || null,
          // Blank means "leave whatever's already stored" (see the backend's
          // UpsertSsoConfigRequest doc comment) - never sent as an empty
          // string that would overwrite a real secret with nothing.
          oidcClientSecret: oidcClientSecret || null,
          samlIdpEntityId: samlIdpEntityId || null,
          samlIdpSsoUrl: samlIdpSsoUrl || null,
          samlIdpCertificate: samlIdpCertificate || null,
        }),
      });
      setConfig(updated);
      setOidcClientSecret("");
      setSamlIdpCertificate("");
      setSaved(true);
    } catch (err) {
      setSaveError(err instanceof ApiError ? err.message : "Couldn't save SSO settings.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <main className="min-h-screen bg-zinc-50">
      <header className="border-b border-zinc-200 bg-white px-8 py-4">
        <button onClick={() => router.push("/dashboard")} className="text-sm text-zinc-500 hover:underline">
          ← Back to tickets
        </button>
        <h1 className="mt-1 text-lg font-semibold">Single Sign-On</h1>
      </header>

      <div className="p-8 max-w-2xl space-y-6">
        <p className="text-sm text-zinc-500">
          Let your team sign in with your identity provider (Entra ID, Okta, Google Workspace, or any SAML
          2.0 / OIDC provider) instead of a TMS password. The first time someone signs in this way, an
          account is created for them automatically using their IdP email; an admin can promote their role
          afterward from the Roles page. Admin-only, since this is a live credential granting sign-in access
          to the whole workspace.
        </p>

        {forbidden && <p className="text-sm text-zinc-500">Only Admins can manage SSO.</p>}
        {error && <p className="text-sm text-red-600">{error}</p>}

        {!forbidden && !error && config === null && <p className="text-sm text-zinc-500">Loading…</p>}

        {!forbidden && config !== null && (
          <form onSubmit={handleSave} className="space-y-6 rounded-lg border border-zinc-200 bg-white p-6">
            <div className="flex items-center justify-between">
              <div>
                <label className="block text-sm font-medium text-zinc-700">Protocol</label>
                <p className="text-xs text-zinc-500">Pick whichever your identity provider uses.</p>
              </div>
              <select
                value={protocol}
                onChange={(e) => setProtocol(e.target.value as "Oidc" | "Saml")}
                className="rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
              >
                <option value="Oidc">OIDC (Entra ID, Okta, Google Workspace)</option>
                <option value="Saml">SAML 2.0</option>
              </select>
            </div>

            <label className="flex items-center gap-2 text-sm text-zinc-700">
              <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
              Enabled — show &quot;Sign in with SSO&quot; on the login page
            </label>

            {protocol === "Oidc" && (
              <div className="space-y-4 border-t border-zinc-100 pt-4">
                <div>
                  <label className="block text-sm font-medium text-zinc-700 mb-1">Authority (issuer URL)</label>
                  <input
                    value={oidcAuthority}
                    onChange={(e) => setOidcAuthority(e.target.value)}
                    placeholder="https://login.microsoftonline.com/{tenant-id}/v2.0"
                    className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                  />
                  <p className="mt-1 text-xs text-zinc-500">
                    Discovery is fetched from <code>{"{authority}"}/.well-known/openid-configuration</code>.
                  </p>
                </div>
                <div>
                  <label className="block text-sm font-medium text-zinc-700 mb-1">Client ID</label>
                  <input
                    value={oidcClientId}
                    onChange={(e) => setOidcClientId(e.target.value)}
                    className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-zinc-700 mb-1">
                    Client secret {config.hasOidcClientSecret && "(already set — leave blank to keep it)"}
                  </label>
                  <input
                    type="password"
                    value={oidcClientSecret}
                    onChange={(e) => setOidcClientSecret(e.target.value)}
                    placeholder={config.hasOidcClientSecret ? "••••••••" : ""}
                    className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                  />
                </div>
                <CopyField label="Redirect URI (paste into your IdP app registration)" value={config.oidcRedirectUri} />
              </div>
            )}

            {protocol === "Saml" && (
              <div className="space-y-4 border-t border-zinc-100 pt-4">
                <div>
                  <label className="block text-sm font-medium text-zinc-700 mb-1">IdP Entity ID / Issuer</label>
                  <input
                    value={samlIdpEntityId}
                    onChange={(e) => setSamlIdpEntityId(e.target.value)}
                    className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-zinc-700 mb-1">IdP Single Sign-On URL</label>
                  <input
                    value={samlIdpSsoUrl}
                    onChange={(e) => setSamlIdpSsoUrl(e.target.value)}
                    placeholder="https://idp.example.com/sso/saml"
                    className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-zinc-700 mb-1">
                    IdP signing certificate{" "}
                    {config.hasSamlIdpCertificate && "(already set — leave blank to keep it)"}
                  </label>
                  <textarea
                    value={samlIdpCertificate}
                    onChange={(e) => setSamlIdpCertificate(e.target.value)}
                    placeholder="-----BEGIN CERTIFICATE-----&#10;...&#10;-----END CERTIFICATE-----"
                    rows={5}
                    className="w-full rounded-md border border-zinc-300 px-3 py-2 font-mono text-xs focus:border-zinc-500 focus:outline-none"
                  />
                  <p className="mt-1 text-xs text-zinc-500">
                    The IdP&apos;s public signing certificate, not a private key — copy it from your IdP&apos;s
                    app registration / metadata page.
                  </p>
                </div>
                <CopyField label="SP Entity ID" value={config.spEntityId} />
                <CopyField label="ACS URL (Assertion Consumer Service)" value={config.samlAcsUrl} />
              </div>
            )}

            {saveError && <p className="text-sm text-red-600">{saveError}</p>}
            {saved && <p className="text-sm text-green-700">Saved.</p>}

            <button
              type="submit"
              disabled={saving}
              className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
            >
              {saving ? "Saving…" : "Save"}
            </button>
          </form>
        )}
      </div>
    </main>
  );
}
