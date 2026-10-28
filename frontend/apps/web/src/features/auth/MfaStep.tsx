import { Button, FieldError } from '@nihasdesk/ui';
import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { apiPost } from '../../lib/api';
import type { Session } from '../session/types';
import { AuthCard } from './AuthLayout';
import { authErrorMessage, isErrorCode } from './errors';
import { Field } from './Field';

/**
 * Second sign-in step for people with two-step sign-in (ADR 0015): the 6-digit code from the
 * authenticator app, or one recovery code. The API holds the pending login for 5 minutes.
 */
export function MfaStep({
  onSignedIn,
  onRestart,
}: {
  onSignedIn: (session: Session) => void;
  onRestart: () => void;
}) {
  const { t } = useTranslation();
  const [useRecovery, setUseRecovery] = useState(false);
  const form = useForm<{ value: string }>({ defaultValues: { value: '' } });
  const verify = useMutation({
    mutationFn: (value: string) =>
      apiPost<Session>('/api/v1/auth/login/mfa', useRecovery ? { recoveryCode: value } : { code: value }),
    onSuccess: onSignedIn,
  });
  const expired = isErrorCode(verify.error, 'auth.mfa_challenge_expired');

  const submit = form.handleSubmit(({ value }) => {
    if (value.trim()) verify.mutate(value.trim());
  });

  return (
    <AuthCard
      title={t('auth.mfa.title')}
      intro={useRecovery ? t('auth.mfa.recoveryIntro') : t('auth.mfa.intro')}
    >
      <form noValidate className="flex flex-col gap-4" onSubmit={submit}>
        <Field
          key={useRecovery ? 'recovery' : 'code'}
          label={useRecovery ? t('auth.mfa.recoveryCode') : t('auth.mfa.code')}
          autoComplete="one-time-code"
          inputMode={useRecovery ? 'text' : 'numeric'}
          maxLength={useRecovery ? 30 : 6}
          registration={form.register('value')}
        />
        {verify.error && <FieldError>{authErrorMessage(verify.error, t)}</FieldError>}
        <Button type="submit" disabled={verify.isPending}>
          {t('auth.mfa.submit')}
        </Button>
      </form>
      <div className="flex flex-col items-center gap-2 text-[13px]">
        <button
          type="button"
          className="font-medium text-info-foreground hover:underline"
          onClick={() => {
            setUseRecovery(!useRecovery);
            form.reset({ value: '' });
            verify.reset();
          }}
        >
          {useRecovery ? t('auth.mfa.useCode') : t('auth.mfa.useRecovery')}
        </button>
        {expired && (
          <button
            type="button"
            className="font-medium text-info-foreground hover:underline"
            onClick={onRestart}
          >
            {t('auth.backToLogin')}
          </button>
        )}
      </div>
    </AuthCard>
  );
}
