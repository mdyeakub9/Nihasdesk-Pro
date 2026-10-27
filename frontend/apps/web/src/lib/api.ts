const apiBaseUrl = (import.meta.env.VITE_API_URL ?? 'http://localhost:5080').replace(/\/$/, '');

export class ApiError extends Error {
  constructor(readonly status: number) {
    super(`API request failed with status ${status}`);
    this.name = 'ApiError';
  }
}

/**
 * Fetch against the Nihasdesk API. Credentials are always included because the dashboard
 * authenticates with HttpOnly cookies (ADR 0005, SEC-14); no tokens are handled in JavaScript.
 * Replaced by the generated api-client once the backend publishes its OpenAPI spec.
 */
export async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  const response = await fetch(`${apiBaseUrl}${path}`, { ...init, credentials: 'include' });
  if (!response.ok) {
    throw new ApiError(response.status);
  }
  return response;
}
