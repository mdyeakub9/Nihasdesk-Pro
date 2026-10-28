import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, apiFetch, apiPost, resetCsrfToken } from '../../lib/api';
import type { Session } from './types';

export const sessionKey = ['session'] as const;

/** The signed-in user from the server-side session; `null` when signed out (401). */
async function getSession(): Promise<Session | null> {
  try {
    return (await (await apiFetch('/api/v1/me')).json()) as Session;
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null;
    }
    throw error;
  }
}

export function useSession() {
  return useQuery({ queryKey: sessionKey, queryFn: getSession, staleTime: 5 * 60_000 });
}

/** Stores a session returned by login or email verification, and resets the user-bound CSRF token. */
export function useStartSession() {
  const queryClient = useQueryClient();
  return (session: Session) => {
    resetCsrfToken();
    queryClient.setQueryData(sessionKey, session);
  };
}

/** Signs out on the server (the session is revoked there) and forgets all cached data. */
export function useLogout() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiPost('/api/v1/auth/logout'),
    onSettled: () => {
      resetCsrfToken();
      queryClient.clear();
      queryClient.setQueryData(sessionKey, null);
    },
  });
}
