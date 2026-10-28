import { Button, Card, FieldError } from '@nihasdesk/ui';
import { useMutation } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { apiPost } from '../../lib/api';
import { authErrorMessage } from '../auth/errors';
import { Field } from '../auth/Field';
import { useReauthStore } from './reauth';

/** Asks for the password before a risky action (ADR 0015); mounted once in the app shell. */
export function ReauthDialog() {
  const { t } = useTranslation();
  const open = useReauthStore((state) => state.open);
  const close = useReauthStore((state) => state.close);
  const form = useForm<{ password: string }>({ defaultValues: { password: '' } });
  const check = useMutation({
    mutationFn: (password: string) => apiPost('/api/v1/account/reauth', { password }),
    onSuccess: () => close(true),
  });
  const { reset: resetForm } = form;
  const { reset: resetCheck } = check;

  useEffect(() => {
    if (open) {
      resetForm({ password: '' });
      resetCheck();
    }
  }, [open, resetForm, resetCheck]);

  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && close(false);
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, close]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-foreground/40 p-4">
      <Card
        role="dialog"
        aria-modal="true"
        aria-labelledby="reauth-title"
        className="flex w-full max-w-sm flex-col gap-4"
      >
        <div className="flex flex-col gap-1">
          <h2 id="reauth-title" className="text-base font-semibold">
            {t('account.reauth.title')}
          </h2>
          <p className="text-[13px] text-secondary-foreground">{t('account.reauth.intro')}</p>
        </div>
        <form
          noValidate
          className="flex flex-col gap-4"
          onSubmit={form.handleSubmit(({ password }) => password && check.mutate(password))}
        >
          <Field
            label={t('auth.password')}
            type="password"
            autoComplete="current-password"
            autoFocus
            registration={form.register('password')}
          />
          {check.error && <FieldError>{authErrorMessage(check.error, t)}</FieldError>}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" onClick={() => close(false)}>
              {t('team.cancel')}
            </Button>
            <Button type="submit" disabled={check.isPending}>
              {t('account.reauth.submit')}
            </Button>
          </div>
        </form>
      </Card>
    </div>
  );
}
