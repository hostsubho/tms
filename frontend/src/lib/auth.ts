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
}

export interface PlatformAuth {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  userId: string;
  email: string;
  role: string;
}

const TENANT_KEY = "tms.tenantAuth";
const PLATFORM_KEY = "tms.platformAuth";

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
  save: (auth: TenantAuth) => writeJson(TENANT_KEY, auth),
  clear: () => clearKey(TENANT_KEY),
};

export const platformAuth = {
  get: () => readJson<PlatformAuth>(PLATFORM_KEY),
  save: (auth: PlatformAuth) => writeJson(PLATFORM_KEY, auth),
  clear: () => clearKey(PLATFORM_KEY),
};
