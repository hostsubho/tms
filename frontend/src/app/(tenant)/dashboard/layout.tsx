"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { impersonationBanner, tenantAuth, ImpersonationBanner } from "@/lib/auth";
import { apiFetch } from "@/lib/api";

interface ThemeSettings {
  primaryColor: string | null;
  secondaryColor: string | null;
  accentColor: string | null;
  themeMode: string;
  borderRadius: string;
  density: string;
  customCss: string | null;
}

const RADIUS_PX: Record<string, string> = {
  None: "0px",
  Small: "4px",
  Medium: "8px",
  Large: "16px",
};

// Module 5.1 - Tenant impersonation. A layout (not a per-page addition) so
// every page under /dashboard/* gets this banner automatically - no need to
// touch each page individually. Only renders anything when an impersonation
// session is actually active; otherwise this is a no-op passthrough.
//
// Client customization - theming was added to this same layout for the same
// reason: it's the one place that wraps every one of the ~20 existing
// dashboard pages without touching each individually.
//
// HOW THEMING WORKS, AND ITS LIMITS: the rest of this app's UI is built with
// hardcoded Tailwind utility classes (bg-zinc-900, rounded-md, etc.), not a
// CSS-variable-driven design token system - retrofitting every component to
// read from variables would be a much larger rewrite. Instead, this injects
// a single <style> block that (a) exposes the tenant's chosen colors as CSS
// custom properties for any future theme-aware component, and (b) overrides
// the specific, consistently-used utility classes that make up this app's
// primary-action color (bg-zinc-900) and corner rounding (rounded-md/
// rounded-lg) so a color/radius choice is actually visible across the app,
// not just on a settings page. This is a pragmatic reskin layer, not a full
// design-token system - CustomCss (free-form, tenant-supplied) is the real
// escape hatch for pixel-level control the color pickers don't reach.
export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const [banner, setBanner] = useState<ImpersonationBanner | null>(null);
  const [theme, setTheme] = useState<ThemeSettings | null>(null);

  useEffect(() => {
    setBanner(impersonationBanner.get());

    const auth = tenantAuth.get();
    if (!auth) return; // Individual pages already redirect to /login themselves.

    apiFetch<ThemeSettings>("/api/tenant/me", { token: auth.accessToken })
      .then(setTheme)
      .catch(() => {
        // Non-fatal - the app just renders with its default look.
      });
  }, []);

  function handleEndImpersonation() {
    tenantAuth.clear();
    router.replace("/admin/tenants");
  }

  const primary = theme?.primaryColor || "#18181b";
  const radius = RADIUS_PX[theme?.borderRadius ?? "Medium"] ?? RADIUS_PX.Medium;
  const isDark = theme?.themeMode === "Dark";

  const themeStyle = theme && (
    <style>{`
      :root {
        --tms-primary: ${primary};
        --tms-secondary: ${theme.secondaryColor || "#3f3f46"};
        --tms-accent: ${theme.accentColor || "#2563eb"};
        --tms-radius: ${radius};
      }

      /* Primary-action buttons/headers use this class consistently across
         the app (login, save, new-ticket, etc.) - see this file's own doc
         comment for why this is a targeted override, not a full retheme. */
      .bg-zinc-900 { background-color: var(--tms-primary) !important; }
      .hover\\:bg-zinc-800:hover { filter: brightness(0.88); }
      .text-zinc-900 { color: var(--tms-primary); }

      .rounded-md { border-radius: var(--tms-radius) !important; }
      .rounded-lg { border-radius: calc(var(--tms-radius) + 4px) !important; }
      .rounded-full { border-radius: 9999px !important; }

      ${
        isDark
          ? `
      /* Dark mode - overrides the small set of background/text/border
         utility classes this app's page templates use consistently for
         their shell (main background, card surfaces, headings, borders).
         Status-color pills (green/amber/red badges) are left alone
         deliberately - their meaning depends on staying visually distinct
         from the shell, not on matching the light/dark toggle. */
      body { background-color: #18181b; }
      .bg-zinc-50 { background-color: #18181b !important; }
      .bg-white { background-color: #27272a !important; }
      .text-zinc-900:not(.bg-zinc-900) { color: #f4f4f5 !important; }
      .border-zinc-200 { border-color: #3f3f46 !important; }
      `
          : ""
      }

      ${theme.customCss || ""}
    `}</style>
  );

  if (!banner) {
    return (
      <>
        {themeStyle}
        {children}
      </>
    );
  }

  return (
    <div>
      {themeStyle}
      <div className="flex items-center justify-between gap-4 bg-amber-500 px-4 py-2 text-sm font-medium text-amber-950">
        <span>
          Impersonating <strong>{banner.targetEmail}</strong> at <strong>{banner.tenantName}</strong> — started by{" "}
          {banner.platformAdminEmail}
        </span>
        <button
          onClick={handleEndImpersonation}
          className="rounded-md bg-amber-950 px-3 py-1 text-xs font-semibold text-amber-50 hover:bg-amber-900"
        >
          End impersonation
        </button>
      </div>
      {children}
    </div>
  );
}
