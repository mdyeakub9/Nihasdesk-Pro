/**
 * Base URL of the API. Empty in development: Vite proxies /api and /health to the API, so the
 * browser stays on one origin (vite.config.ts). Production sets VITE_API_URL (e.g. https://api.<domain>).
 */
const apiBaseUrl = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '');

/** Absolute URL of an API path (for clients that don't use fetch, e.g. SignalR). */
export function apiUrl(path: string): string {
  return `${apiBaseUrl}${path}`;
}

/** A failed API call; `code` is the stable error code from Problem Details (docs/standards/errors.md). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code?: string,
  ) {
    super(code ? `API error ${status} (${code})` : `API request failed with status ${status}`);
    this.name = 'ApiError';
  }
}

const unsafeMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

/** Antiforgery token, kept in memory only (SEC-14). It is bound to the user, so it is reset on sign-in/out. */
let csrfToken: string | null = null;

export function resetCsrfToken() {
  csrfToken = null;
}

export async function getCsrfToken(): Promise<string> {
  if (!csrfToken) {
    const response = await fetch(`${apiBaseUrl}/api/v1/csrf`, { credentials: 'include' });
    if (!response.ok) {
      throw new ApiError(response.status);
    }
    csrfToken = ((await response.json()) as { token: string }).token;
  }
  return csrfToken;
}

async function errorCode(response: Response): Promise<string | undefined> {
  try {
    const body = (await response.clone().json()) as { code?: unknown };
    return typeof body.code === 'string' ? body.code : undefined;
  } catch {
    return undefined;
  }
}

async function send(path: string, init: RequestInit): Promise<Response> {
  const method = (init.method ?? 'GET').toUpperCase();
  const headers = new Headers(init.headers);
  if (unsafeMethods.has(method)) {
    headers.set('X-XSRF-TOKEN', await getCsrfToken());
  }
  return fetch(`${apiBaseUrl}${path}`, { ...init, method, headers, credentials: 'include' });
}

/**
 * Fetch against the Nihasdesk API. Cookies are always included (ADR 0005, SEC-14); changes carry
 * the CSRF header (SEC-03), retried once with a fresh token if the old one was rejected.
 * Replaced by the generated api-client once the backend publishes its OpenAPI spec.
 */
export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  let response = await send(path, init);
  if (response.status === 400 && (await errorCode(response)) === 'csrf.invalid') {
    resetCsrfToken();
    response = await send(path, init);
  }

  if (!response.ok) {
    throw new ApiError(response.status, await errorCode(response));
  }
  return response;
}

/** POST a JSON body; returns the parsed JSON response, or undefined for empty answers (202/204). */
export async function apiPost<T = undefined>(path: string, body?: unknown): Promise<T> {
  const response = await apiFetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body ?? {}),
  });
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

/** PATCH a JSON body; returns undefined for empty answers. */
export async function apiPatch(path: string, body: unknown): Promise<void> {
  await apiFetch(path, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

export async function apiDelete(path: string): Promise<void> {
  await apiFetch(path, { method: 'DELETE' });
}

export async function apiGet<T>(path: string): Promise<T> {
  return (await (await apiFetch(path)).json()) as T;
}
