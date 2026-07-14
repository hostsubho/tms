"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, ApiError } from "@/lib/api";
import { tenantAuth } from "@/lib/auth";

interface ThemeSettings {
  primaryColor: string | null;
  secondaryColor: string | null;
  accentColor: string | null;
  themeMode: string;
  borderRadius: string;
  density: string;
  customCss: string | null;
}

const DEFAULT_PRIMARY = "#18181b";
const DEFAULT_SECONDARY = "#3f3f46";
const DEFAULT_ACCENT = "#2563eb";

export default function ThemeSettingsPage() {
  const router = useRouter();
  const [forbidden, setForbidden] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [loaded, setLoaded] = useState(false);

  const [primaryColor, setPrimaryColor] = useState(DEFAULT_PRIMARY);
  const [secondaryColor, setSecondaryColor] = useState(DEFAULT_SECONDARY);
  const [accentColor, setAccentColor] = useState(DEFAULT_ACCENT);
  const [themeMode, setThemeMode] = useState("Light");
  const [borderRadius, setBorderRadius] = useState("Medium");
  const [density, setDensity] = useState("Comfortable");
  const [customCss, setCustomCss] = useState("");

  useEffect(() => {
    const auth = tenantAuth.get();
    if (!auth) {
      router.replace("/login");
      return;
    }
    if (auth.role !== "Admin") {
      setForbidden(true);
      return;
    }

    apiFetch<ThemeSettings>("/api/tenant/me", { token: auth.accessToken })
      .then((t) => {
        setPrimaryColor(t.primaryColor || DEFAULT_PRIMARY);
        setSecondaryColor(t.secondaryColor || DEFAULT_SECONDARY);
        setAccentColor(t.accentColor || DEFAULT_ACCENT);
        setThemeMode(t.themeMode);
        setBorderRadius(t.borderRadius);
        setDensity(t.density);
        setCustomCss(t.customCss || "");
        setLoaded(true);
      })
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          tenantAuth.clear();
          router.replace("/login");
          return;
        }
        setError(err instanceof ApiError ? err.message : "Couldn't load theme settings.");
      });
  }, [router]);

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    const auth = tenantAuth.get();
    if (!auth) return;

    setSaveError(null);
    setSaved(false);
    setSaving(true);
    try {
      await apiFetch("/api/tenant/me/theme", {
        method: "PATCH",
        token: auth.accessToken,
        body: JSON.stringify({
          primaryColor,
          secondaryColor,
          accentColor,
          themeMode,
          borderRadius,
          density,
          customCss,
        }),
      });
      setSaved(true);
      // A full reload (not a client-side state update) so the layout's own
      // theme fetch picks up the new values immediately, everywhere -
      // simpler and more reliable than plumbing a theme-changed event up to
      // the layout from a page it doesn't otherwise know about.
      setTimeout(() => window.location.reload(), 600);
    } catch (err) {
      setSaveError(err instanceof ApiError ? err.message : "Couldn't save theme settings.");
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
        <h1 className="mt-1 text-lg font-semibold">Theme &amp; Branding</h1>
      </header>

      <div className="p-8 max-w-2xl space-y-6">
        <p className="text-sm text-zinc-500">
          Customize how your workspace looks for everyone on your team. Color and rounding changes apply
          across the dashboard; Custom CSS is an escape hatch for anything the pickers below don&apos;t
          cover. Admin-only.
        </p>

        {forbidden && <p className="text-sm text-zinc-500">Only Admins can change theme settings.</p>}
        {error && <p className="text-sm text-red-600">{error}</p>}
        {!forbidden && !error && !loaded && <p className="text-sm text-zinc-500">Loading…</p>}

        {!forbidden && loaded && (
          <form onSubmit={handleSave} className="space-y-6 rounded-lg border border-zinc-200 bg-white p-6">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">Primary color</label>
                <div className="flex items-center gap-2">
                  <input
                    type="color"
                    value={primaryColor}
                    onChange={(e) => setPrimaryColor(e.target.value)}
                    className="h-9 w-9 cursor-pointer rounded border border-zinc-300"
                  />
                  <input
                    value={primaryColor}
                    onChange={(e) => setPrimaryColor(e.target.value)}
                    className="w-full rounded-md border border-zinc-300 px-2 py-1.5 text-xs focus:border-zinc-500 focus:outline-none"
                  />
                </div>
              </div>
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">Secondary color</label>
                <div className="flex items-center gap-2">
                  <input
                    type="color"
                    value={secondaryColor}
                    onChange={(e) => setSecondaryColor(e.target.value)}
                    className="h-9 w-9 cursor-pointer rounded border border-zinc-300"
                  />
                  <input
                    value={secondaryColor}
                    onChange={(e) => setSecondaryColor(e.target.value)}
                    className="w-full rounded-md border border-zinc-300 px-2 py-1.5 text-xs focus:border-zinc-500 focus:outline-none"
                  />
                </div>
              </div>
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">Accent color</label>
                <div className="flex items-center gap-2">
                  <input
                    type="color"
                    value={accentColor}
                    onChange={(e) => setAccentColor(e.target.value)}
                    className="h-9 w-9 cursor-pointer rounded border border-zinc-300"
                  />
                  <input
                    value={accentColor}
                    onChange={(e) => setAccentColor(e.target.value)}
                    className="w-full rounded-md border border-zinc-300 px-2 py-1.5 text-xs focus:border-zinc-500 focus:outline-none"
                  />
                </div>
              </div>
            </div>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">Theme mode</label>
                <select
                  value={themeMode}
                  onChange={(e) => setThemeMode(e.target.value)}
                  className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                >
                  <option value="Light">Light</option>
                  <option value="Dark">Dark</option>
                  <option value="Auto">Auto (follow system)</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">Corner rounding</label>
                <select
                  value={borderRadius}
                  onChange={(e) => setBorderRadius(e.target.value)}
                  className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                >
                  <option value="None">None</option>
                  <option value="Small">Small</option>
                  <option value="Medium">Medium</option>
                  <option value="Large">Large</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-zinc-700 mb-1">Density</label>
                <select
                  value={density}
                  onChange={(e) => setDensity(e.target.value)}
                  className="w-full rounded-md border border-zinc-300 px-3 py-2 text-sm focus:border-zinc-500 focus:outline-none"
                >
                  <option value="Compact">Compact</option>
                  <option value="Comfortable">Comfortable</option>
                  <option value="Spacious">Spacious</option>
                </select>
              </div>
            </div>

            <div>
              <label className="block text-sm font-medium text-zinc-700 mb-1">Custom CSS (advanced)</label>
              <textarea
                value={customCss}
                onChange={(e) => setCustomCss(e.target.value)}
                placeholder=".rounded-md { border-radius: 2px; }"
                rows={6}
                className="w-full rounded-md border border-zinc-300 px-3 py-2 font-mono text-xs focus:border-zinc-500 focus:outline-none"
              />
              <p className="mt-1 text-xs text-zinc-500">
                Applied only within your own workspace&apos;s dashboard, never the Super Admin console or
                another company&apos;s workspace. 20,000 character limit.
              </p>
            </div>

            {saveError && <p className="text-sm text-red-600">{saveError}</p>}
            {saved && <p className="text-sm text-green-700">Saved — reloading…</p>}

            <button
              type="submit"
              disabled={saving}
              className="rounded-md bg-zinc-900 px-4 py-2 text-sm font-medium text-white hover:bg-zinc-800 disabled:opacity-50"
            >
              {saving ? "Saving…" : "Save theme"}
            </button>
          </form>
        )}
      </div>
    </main>
  );
}
