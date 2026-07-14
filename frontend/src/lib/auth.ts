/**
 * Client-side storage for the two separate auth surfaces the backend exposes:
 * tenant AppUser sessions (Module 1 / Module 2) and platform Super Admin
 * sessions (Module 5). Kept in localStorage for simplicity — this is a
 * starting point, not a hardened auth implementation. Swap for httpOnly
 * cookies + server-side session handling before this goes to real production
 * traffic with sensitive data.
 */

export interface TenantAuth {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  userId: string;
  email: string;
  role: string;
  tenantSlug: string;
  // Module 12 - Roles & Permissions. Granted by the user's assigned custom
  // role, if any - always empty for Admin/Manager (they already have every
  // permission implicitly; see PermissionAuthorizationHandler on the
  // backend). Snapshotted at login/refresh, same staleness window as `role`.
  permissions: string[];
}

export interface PlatformAuth {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  userId: string;
  email: string;
  role: string;
}

// Module 7 - Customer/End-User Portal. A third, separate auth surface: a
// PortalCustomer, never a tenant AppUser or PlatformUser.
export interface PortalAuth {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  customerId: string;
  name: string;
  email: string;
  tenantSlug: string;
}

// Module 5.1 - Tenant impersonation. Not a fourth auth surface - the token
// issued is stored in the same `tenantAuth` slot a real tenant login uses
// (so every existing tenant-side page works completely unchanged). This is
// purely a UI marker so the dashboard layout can show "you're impersonating
// X" instead of silently pretending to be a normal session, plus an "End
// impersonation" action. Cleared whenever tenantAuth itself is cleared
// (logout/401), see clear() below.
export interface ImpersonationBanner {
  tenantName: string;
  targetEmail: string;
  platformAdminEmail: string;
  startedAt: string;
}

const TENANT_KEY = "tms.tenantAuth";
const PLATFORM_KEY = "tms.platformAuth";
const PORTAL_KEY = "tms.portalAuth";
const IMPERSONATION_KEY = "tms.impersonationBanner";

function readJson<T>(key: string): T | null {
  if (typeof window === "undefined") return null;
  const raw = window.localStorage.getItem(key);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as T;
  } catch {
    return null;
  }
}

function writeJson(key: string, value: unknown) {
  if (typeof window === "undefined") return;
  window.localStorage.setItem(key, JSON.stringify(value));
}

function clearKey(key: string) {
  if (typeof window === "undefined") return;
  window.localStorage.removeItem(key);
}

export const tenantAuth = {
  get: () => readJson<TenantAuth>(TENANT_KEY),
  // Clears any impersonation banner on every save, not just clear() - a
  // real tenant login/signup (login/page.tsx, signup/page.tsx) also calls
  // save() directly and has no reason to know about impersonation, but if
  // a stale banner from an earlier impersonation session were left behind
  // in localStorage, DashboardLayout would show "Impersonating X" over a
  // completely unrelated real session, and "End impersonation" would then
  // discard that real session instead of an impersonation one.
  // handleImpersonate (admin/tenants/page.tsx) calls impersonationBanner.save()
  // immediately after this, so the ordering still ends up correct for an
  // actual impersonation start.
  save: (auth: TenantAuth) => {
    writeJson(TENANT_KEY, auth);
    clearKey(IMPERSONATION_KEY);
  },
  // Also clears any impersonation banner - a stale "you're impersonating
  // X" banner must never survive past the session it describes (e.g. a
  // 401 that clears tenantAuth and bounces to /login).
  clear: () => {
    clearKey(TENANT_KEY);
    clearKey(IMPERSONATION_KEY);
  },
};

export const impersonationBanner = {
  get: () => readJson<ImpersonationBanner>(IMPERSONATION_KEY),
  save: (banner: ImpersonationBanner) => writeJson(IMPERSONATION_KEY, banner),
  clear: () => clearKey(IMPERSONATION_KEY),
};

export const platformAuth = {
  get: () => readJson<PlatformAuth>(PLATFORM_KEY),
  save: (auth: PlatformAuth) => writeJson(PLATFORM_KEY, auth),
  clear: () => clearKey(PLATFORM_KEY),
};

export const portalAuth = {
  get: () => readJson<PortalAuth>(PORTAL_KEY),
  save: (auth: PortalAuth) => writeJson(PORTAL_KEY, auth),
  clear: () => clearKey(PORTAL_KEY),
};
