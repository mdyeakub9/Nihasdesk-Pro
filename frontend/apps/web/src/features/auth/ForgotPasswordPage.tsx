import { zodResolver } from '@hookform/resolvers/zod';
import { Button, FieldError } from '@nihasdesk/ui';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { z } from 'zod';
import { apiPost } from '../../lib/api';
import { AuthCard } from './AuthLayout';
import { authErrorMessage } from './errors';
import { Field } from './Field';
import { emailField } from './schemas';

export function ForgotPasswordPage() {
  const { t } = useTranslation();
  const schema = z.object({ email: emailField(t) });
  type Values = z.infer<typeof schema>;
  const form = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { email: '' } });
  const request = useMutation({
    mutationFn: (values: Values) => apiPost('/api/v1/auth/forgot-password', values),
  });

  return (
    <AuthCard title={t('auth.forgot.title')} intro={request.isSuccess ? undefined : t('auth.forgot.intro')}>
      {request.isSuccess ? (
        // Same message whether or not the account exists (SEC-26).
        <p role="status" className="rounded-md bg-success px-3 py-2 text-[13px] text-success-foreground">
          {t('auth.forgot.sent')}
        </p>
      ) : (
        <form
          noValidate
          className="flex flex-col gap-4"
          onSubmit={form.handleSubmit((values) => request.mutate(values))}
        >
          <Field
            label={t('auth.email')}
            type="email"
            autoComplete="email"
            registration={form.register('email')}
            error={form.formState.errors.email?.message}
          />
          {request.error && <FieldError>{authErrorMessage(request.error, t)}</FieldError>}
          <Button type="submit" disabled={request.isPending}>
            {t('auth.forgot.submit')}
          </Button>
        </form>
      )}
      <Link to="/login" className="text-center text-[13px] font-medium text-info-foreground hover:underline">
        {t('auth.backToLogin')}
      </Link>
    </AuthCard>
  );
}
