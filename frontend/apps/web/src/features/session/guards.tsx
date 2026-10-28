import type { ReactNode } from 'react';
import { Navigate, useLocation, useSearchParams } from 'react-router';
import { safeReturnTo } from './returnTo';
import { useSession } from './useSession';

function Loading() {
  return <div className="flex h-dvh items-center justify-center bg-background" aria-busy="true" />;
}

/** Wraps the dashboard: without a session, go to login and come back afterwards. */
export function RequireSession({ children }: { children: ReactNode }) {
  const { data: session, isPending } = useSession();
  const location = useLocation();

  if (isPending) return <Loading />;
  if (!session) {
    const returnTo = location.pathname + location.search;
    return <Navigate to={`/login?returnTo=${encodeURIComponent(returnTo)}`} replace />;
  }
  return children;
}

/** Login and signup pages: someone already signed in goes straight to the dashboard. */
export function RedirectIfSignedIn({ children }: { children: ReactNode }) {
  const { data: session, isPending } = useSession();
  const [params] = useSearchParams();

  if (isPending) return <Loading />;
  if (session) return <Navigate to={safeReturnTo(params.get('returnTo'))} replace />;
  return children;
}
