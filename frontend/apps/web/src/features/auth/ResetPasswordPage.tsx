import { zodResolver } from '@hookform/resolvers/zod';
import { Button, FieldError } from '@nihasdesk/ui';
import { useMutation } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { z } from 'zod';
import { apiPost } from '../../lib/api';
import { AuthCard } from './AuthLayout';
import { authErrorMessage } from './errors';
import { Field } from './Field';
import { newPasswordField } from './schemas';

/** Opened from the reset email. On success all sessions are gone, so the user signs in again. */
export function ResetPasswordPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [link] = useState(() => ({ userId: params.get('user'), token: params.get('token') }));

  // Keep the token out of the address bar once read.
  useEffect(() => {
    void navigate('/reset-password', { replace: true });
  }, [navigate]);

  const schema = z
    .object({ newPassword: newPasswordField(t), confirmPassword: z.string() })
    .refine((v) => v.newPassword === v.confirmPassword, {
      path: ['confirmPassword'],
      message: t('validation.passwordMismatch'),
    });
  type Values = z.infer<typeof schema>;
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { newPassword: '', confirmPassword: '' },
  });

  const reset = useMutation({
    mutationFn: (values: Values) =>
      apiPost('/api/v1/auth/reset-password', {
        userId: link.userId,
        token: link.token,
        newPassword: values.newPassword,
      }),
    onSuccess: () => void navigate('/login', { replace: true, state: { notice: 'auth.reset.done' } }),
  });

  if (!link.userId || !link.token) {
    return (
      <AuthCard title={t('auth.reset.title')} intro={t('authErrors.invalidToken')}>
        <Button asChild variant="outline">
          <Link to="/forgot-password">{t('auth.verify.requestNew')}</Link>
        </Button>
      </AuthCard>
    );
  }

  const { errors } = form.formState;

  return (
    <AuthCard title={t('auth.reset.title')} intro={t('auth.reset.intro')}>
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={form.handleSubmit((values) => reset.mutate(values))}
      >
        <Field
          label={t('auth.reset.newPassword')}
          type="password"
          autoComplete="new-password"
          hint={t('auth.passwordHint')}
          registration={form.register('newPassword')}
          error={errors.newPassword?.message}
        />
        <Field
          label={t('auth.reset.confirmPassword')}
          type="password"
          autoComplete="new-password"
          registration={form.register('confirmPassword')}
          error={errors.confirmPassword?.message}
        />
        {reset.error && <FieldError>{authErrorMessage(reset.error, t)}</FieldError>}
        <Button type="submit" disabled={reset.isPending}>
          {t('auth.reset.submit')}
        </Button>
      </form>
    </AuthCard>
  );
}
