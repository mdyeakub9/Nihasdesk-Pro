import { Button } from '@nihasdesk/ui';
import { useMutation } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { apiPost } from '../../lib/api';
import type { Session } from '../session/types';
import { useStartSession } from '../session/useSession';
import { AuthCard } from './AuthLayout';
import { authErrorMessage } from './errors';

/** Opened from the confirmation email: confirms, signs in and continues to the inbox. */
export function VerifyEmailPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const startSession = useStartSession();
  // Read once, then drop the token from the address bar (history, screenshots, referrers).
  const [link] = useState(() => ({ userId: params.get('user'), token: params.get('token') }));
  const started = useRef(false);

  const verify = useMutation({
    mutationFn: () => apiPost<Session>('/api/v1/auth/verify-email', link),
    onSuccess: (session) => {
      startSession(session);
      void navigate('/inbox', { replace: true });
    },
  });

  useEffect(() => {
    // Guarded: React StrictMode runs effects twice in development.
    if (started.current) return;
    started.current = true;
    void navigate('/verify-email', { replace: true });
    if (link.userId && link.token) {
      verify.mutate();
    }
  }, [link, navigate, verify]);

  const broken = !link.userId || !link.token;
  const failed = broken || verify.isError;

  return (
    <AuthCard
      title={failed ? t('auth.verify.failedTitle') : t('auth.verify.title')}
      intro={failed ? (broken ? t('authErrors.invalidToken') : authErrorMessage(verify.error, t)) : undefined}
    >
      {failed ? (
        <Button asChild variant="outline">
          <Link to="/check-email">{t('auth.verify.requestNew')}</Link>
        </Button>
      ) : (
        <p role="status" aria-busy="true" className="text-[13px] text-secondary-foreground">
          {t('auth.verify.working')}
        </p>
      )}
    </AuthCard>
  );
}
