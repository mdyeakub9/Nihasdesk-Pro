import { useQuery } from '@tanstack/react-query';
import type { Session } from './types';

/** Current user and workspace. Mock in dev and tests until the Identity module provides it. */
async function getSession(): Promise<Session | null> {
  if (import.meta.env.DEV) {
    return (await import('../../mocks/session')).mockSession;
  }
  return null;
}

export function useSession() {
  return useQuery({ queryKey: ['session'], queryFn: getSession, staleTime: Infinity });
}
