import { zodResolver } from '@hookform/resolvers/zod';
import { Button, Checkbox, FieldError, Label } from '@nihasdesk/ui';
import { useMutation } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router';
import { z } from 'zod';
import { apiPost } from '../../lib/api';
import { safeReturnTo } from '../session/returnTo';
import type { Session } from '../session/types';
import { useStartSession } from '../session/useSession';
import { AuthCard } from './AuthLayout';
import { authErrorMessage, isErrorCode } from './errors';
import { Field } from './Field';
import { emailField, passwordMax } from './schemas';

export function LoginPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const notice = (useLocation().state as { notice?: string } | null)?.notice;
  const startSession = useStartSession();

  const schema = z.object({
    email: emailField(t),
    // Only an upper bound: the API decides, and every failure looks the same (SEC-26).
    password: z.string().min(1, t('validation.required')).max(passwordMax),
    rememberMe: z.boolean(),
  });
  type Values = z.infer<typeof schema>;

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', password: '', rememberMe: false },
  });
  const login = useMutation({
    mutationFn: (values: Values) => apiPost<Session>('/api/v1/auth/login', values),
    onSuccess: (session) => {
      startSession(session);
      void navigate(safeReturnTo(params.get('returnTo')), { replace: true });
    },
  });

  const { errors } = form.formState;
  const rememberMe = useWatch({ control: form.control, name: 'rememberMe' });

  return (
    <AuthCard title={t('auth.login.title')} intro={t('auth.login.intro')}>
      {notice && (
        <p role="status" className="rounded-md bg-success px-3 py-2 text-[13px] text-success-foreground">
          {t(notice)}
        </p>
      )}
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={form.handleSubmit((values) => login.mutate(values))}
      >
        <Field
          label={t('auth.email')}
          type="email"
          autoComplete="username"
          registration={form.register('email')}
          error={errors.email?.message}
        />
        <Field
          label={t('auth.password')}
          type="password"
          autoComplete="current-password"
          registration={form.register('password')}
          error={errors.password?.message}
        />
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <Checkbox
              id="remember-me"
              checked={rememberMe}
              onCheckedChange={(checked) => form.setValue('rememberMe', checked === true)}
            />
            <Label htmlFor="remember-me" className="font-normal">
              {t('auth.rememberMe')}
            </Label>
          </div>
          <Link
            to="/forgot-password"
            className="text-[13px] font-medium text-info-foreground hover:underline"
          >
            {t('auth.login.forgot')}
          </Link>
        </div>
        {login.error && (
          <div className="flex flex-col gap-1">
            <FieldError>{authErrorMessage(login.error, t)}</FieldError>
            {isErrorCode(login.error, 'auth.email_not_verified') && (
              <Link
                to="/check-email"
                state={{ email: form.getValues('email') }}
                className="text-xs font-medium text-info-foreground hover:underline"
              >
                {t('auth.checkEmail.resend')}
              </Link>
            )}
          </div>
        )}
        <Button type="submit" disabled={login.isPending}>
          {t('auth.login.submit')}
        </Button>
      </form>
      <p className="text-center text-[13px] text-secondary-foreground">
        {t('auth.login.noAccount')}{' '}
        <Link to="/signup" className="font-medium text-info-foreground hover:underline">
          {t('auth.login.signupLink')}
        </Link>
      </p>
    </AuthCard>
  );
}
