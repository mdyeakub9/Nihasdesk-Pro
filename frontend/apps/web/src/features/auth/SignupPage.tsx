import { zodResolver } from '@hookform/resolvers/zod';
import { Button, FieldError } from '@nihasdesk/ui';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router';
import { z } from 'zod';
import { apiPost } from '../../lib/api';
import { AuthCard } from './AuthLayout';
import { authErrorMessage } from './errors';
import { Field } from './Field';
import { emailField, newPasswordField, requiredText } from './schemas';

export function SignupPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();

  const schema = z.object({
    name: requiredText(t, 200),
    businessName: requiredText(t, 200),
    email: emailField(t),
    password: newPasswordField(t),
  });
  type Values = z.infer<typeof schema>;

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', businessName: '', email: '', password: '' },
  });
  const signup = useMutation({
    // The dashboard language becomes the account language for emails (PRD 4.13).
    mutationFn: (values: Values) =>
      apiPost('/api/v1/auth/signup', { ...values, language: i18n.resolvedLanguage === 'it' ? 'it' : 'en' }),
    // The email travels in router state, not in the URL (no personal data in addresses or logs).
    onSuccess: (_, values) => void navigate('/check-email', { state: { email: values.email } }),
  });

  const { errors } = form.formState;

  return (
    <AuthCard title={t('auth.signup.title')} intro={t('auth.signup.intro')}>
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={form.handleSubmit((values) => signup.mutate(values))}
      >
        <Field
          label={t('auth.name')}
          autoComplete="name"
          registration={form.register('name')}
          error={errors.name?.message}
        />
        <Field
          label={t('auth.businessName')}
          autoComplete="organization"
          registration={form.register('businessName')}
          error={errors.businessName?.message}
        />
        <Field
          label={t('auth.email')}
          type="email"
          autoComplete="email"
          registration={form.register('email')}
          error={errors.email?.message}
        />
        <Field
          label={t('auth.password')}
          type="password"
          autoComplete="new-password"
          hint={t('auth.passwordHint')}
          registration={form.register('password')}
          error={errors.password?.message}
        />
        {signup.error && <FieldError>{authErrorMessage(signup.error, t)}</FieldError>}
        <Button type="submit" disabled={signup.isPending}>
          {t('auth.signup.submit')}
        </Button>
      </form>
      <p className="text-center text-[13px] text-secondary-foreground">
        {t('auth.signup.haveAccount')}{' '}
        <Link to="/login" className="font-medium text-info-foreground hover:underline">
          {t('auth.signup.loginLink')}
        </Link>
      </p>
    </AuthCard>
  );
}
