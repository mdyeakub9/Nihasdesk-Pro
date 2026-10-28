import { vi } from 'vitest';
import type { Session } from '../features/session/types';

export const testSession: Session = {
  user: { id: 'u-1', name: 'Md Yeakub', email: 'owner@example.com', language: 'en', emailVerified: true },
  workspace: { id: 'w-1', name: 'Almanaam' },
  role: 'owner',
};

type Handler = (body: unknown) => Response | Promise<Response>;

type FakeApiOptions = {
  /** Signed-in user, or null for a signed-out browser. */
  session?: Session | null;
  /** Health check answer; healthy by default. */
  health?: () => Response;
  /** Answers for POST paths, e.g. '/api/v1/auth/login'. Unlisted POSTs answer 204. */
  post?: Record<string, Handler>;
};

export const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

export const problem = (status: number, code: string) => json({ status, code, title: code }, status);

/** Replaces fetch with a small fake of the API; returns the mock to inspect calls. */
export function fakeApi({ session = testSession, health, post = {} }: FakeApiOptions = {}) {
  let current = session;
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString();
    const method = (init?.method ?? 'GET').toUpperCase();

    if (url.endsWith('/health')) return health ? health() : new Response('Healthy');
    if (url.endsWith('/api/v1/csrf')) return json({ token: 'test-csrf-token' });
    if (url.endsWith('/api/v1/me')) return current ? json(current) : problem(401, 'auth.unauthorized');

    if (method === 'POST') {
      const path = new URL(url, 'http://localhost').pathname;
      if (path === '/api/v1/auth/logout') current = null;
      const handler = post[path];
      const body = init?.body ? JSON.parse(String(init.body)) : undefined;
      const response = handler ? await handler(body) : new Response(null, { status: 204 });
      // Like the real API: a successful login or verification starts a session.
      if (response.ok && (path === '/api/v1/auth/login' || path === '/api/v1/auth/verify-email')) {
        current = (await response.clone().json()) as Session;
      }
      return response;
    }

    return new Response(null, { status: 404 });
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

/** Calls to a path, with the parsed JSON body and headers. */
export function callsTo(fetchMock: ReturnType<typeof fakeApi>, path: string) {
  return fetchMock.mock.calls
    .filter(([input]) => String(input).endsWith(path))
    .map(([, init]) => ({
      body: init?.body ? JSON.parse(String(init.body)) : undefined,
      headers: new Headers(init?.headers),
    }));
}
