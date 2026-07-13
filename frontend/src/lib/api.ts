/**
 * Thin fetch wrapper for talking to the .NET backend API.
 * Base URL comes from env so the same frontend build works across
 * dev / staging / prod Vercel environments, pointing at different backends
 * (currently the Render-hosted tms-backend service).
 */
const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5080";

export interface ApiOptions extends RequestInit {
  /** Bearer token — tenant AppUser JWT or PlatformUser JWT, never both. */
  token?: string;
}

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

export async function apiFetch<T>(path: string, options: ApiOptions = {}): Promise<T> {
  const { token, headers, ...rest } = options;

  const res = await fetch(`${API_BASE_URL}${path}`, {
    ...rest,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
  });

  if (!res.ok) {
    let message = res.statusText;
    const body = await res.text().catch(() => "");
    if (body) {
      try {
        const parsed = JSON.parse(body);
        message = parsed.message ?? parsed.title ?? body;
      } catch {
        message = body;
      }
    }
    throw new ApiError(res.status, message || `Request to ${path} failed (${res.status})`);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  return res.json() as Promise<T>;
}

export { API_BASE_URL };
