import { zodResolver } from '@hookform/resolvers/zod';
import { Button, FieldError } from '@nihasdesk/ui';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useLocation } from 'react-router';
import { z } from 'zod';
import { apiPost } from '../../lib/api';
import { AuthCard } from './AuthLayout';
import { authErrorMessage } from './errors';
import { Field } from './Field';
import { emailField } from './schemas';

/** After signup (or a blocked login): explains the confirmation email and lets the user ask again. */
export function CheckEmailPage() {
  const { t } = useTranslation();
  const knownEmail = (useLocation().state as { email?: string } | null)?.email ?? '';

  const schema = z.object({ email: emailField(t) });
  type Values = z.infer<typeof schema>;
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { email: knownEmail } });
  const resend = useMutation({
    mutationFn: (values: Values) => apiPost('/api/v1/auth/resend-verification', values),
  });

  return (
    <AuthCard
      title={t('auth.checkEmail.title')}
      intro={knownEmail ? t('auth.checkEmail.text', { email: knownEmail }) : t('auth.checkEmail.textNoEmail')}
    >
      {resend.isSuccess ? (
        <p role="status" className="rounded-md bg-success px-3 py-2 text-[13px] text-success-foreground">
          {t('auth.checkEmail.resent')}
        </p>
      ) : (
        <form
          noValidate
          className="flex flex-col gap-4"
          onSubmit={form.handleSubmit((values) => resend.mutate(values))}
        >
          {!knownEmail && (
            <Field
              label={t('auth.email')}
              type="email"
              autoComplete="email"
              registration={form.register('email')}
              error={form.formState.errors.email?.message}
            />
          )}
          {resend.error && <FieldError>{authErrorMessage(resend.error, t)}</FieldError>}
          <Button type="submit" variant="outline" disabled={resend.isPending}>
            {t('auth.checkEmail.resend')}
          </Button>
        </form>
      )}
      <Link to="/login" className="text-center text-[13px] font-medium text-info-foreground hover:underline">
        {t('auth.backToLogin')}
      </Link>
    </AuthCard>
  );
}
