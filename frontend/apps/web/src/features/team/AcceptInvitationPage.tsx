import { zodResolver } from '@hookform/resolvers/zod';
import { Button, FieldError } from '@nihasdesk/ui';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { z } from 'zod';
import { apiPost } from '../../lib/api';
import { AuthCard } from '../auth/AuthLayout';
import { authErrorMessage, isErrorCode } from '../auth/errors';
import { Field } from '../auth/Field';
import { newPasswordField, requiredText } from '../auth/schemas';
import type { Session } from '../session/types';
import { useStartSession } from '../session/useSession';
import type { InvitationPreview } from './api';

/** Opened from the invitation email (ADR 0014): shows who invited you, then creates your account. */
export function AcceptInvitationPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const startSession = useStartSession();
  // Read once and drop the token from the address bar; it is sent in request bodies only.
  const [token] = useState(() => params.get('token') ?? '');

  useEffect(() => {
    void navigate('/accept-invitation', { replace: true });
  }, [navigate]);

  const preview = useQuery({
    queryKey: ['invitation-preview', token],
    queryFn: () => apiPost<InvitationPreview>('/api/v1/invitations/preview', { token }),
    enabled: token.length > 0,
    retry: false,
    staleTime: Infinity,
  });

  const schema = z
    .object({ name: requiredText(t, 200), password: newPasswordField(t), confirmPassword: z.string() })
    .refine((v) => v.password === v.confirmPassword, {
      path: ['confirmPassword'],
      message: t('validation.passwordMismatch'),
    });
  type Values = z.infer<typeof schema>;
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', password: '', confirmPassword: '' },
  });

  const accept = useMutation({
    mutationFn: (values: Values) =>
      apiPost<Session>('/api/v1/invitations/accept', { token, name: values.name, password: values.password }),
    onSuccess: (session) => {
      startSession(session);
      void navigate('/inbox', { replace: true });
    },
  });

  if (!token || preview.isError) {
    return (
      <AuthCard title={t('acceptInvite.invalidTitle')} intro={t('teamErrors.invitationInvalid')}>
        <Link
          to="/login"
          className="text-center text-[13px] font-medium text-info-foreground hover:underline"
        >
          {t('auth.backToLogin')}
        </Link>
      </AuthCard>
    );
  }

  if (!preview.data) {
    return <AuthCard title={t('acceptInvite.loading')}>{null}</AuthCard>;
  }

  const invitation = preview.data;
  const { errors } = form.formState;
  const hasAccount = isErrorCode(accept.error, 'team.email_has_account');

  return (
    <AuthCard
      title={t('acceptInvite.title', { workspace: invitation.workspaceName })}
      intro={t('acceptInvite.intro', {
        inviter: invitation.inviterName,
        role: t(`roles.${invitation.role}`),
        email: invitation.email,
      })}
    >
      <form
        noValidate
        className="flex flex-col gap-4"
        onSubmit={form.handleSubmit((values) => accept.mutate(values))}
      >
        <Field
          label={t('auth.name')}
          autoComplete="name"
          registration={form.register('name')}
          error={errors.name?.message}
        />
        <Field
          label={t('auth.password')}
          type="password"
          autoComplete="new-password"
          hint={t('auth.passwordHint')}
          registration={form.register('password')}
          error={errors.password?.message}
        />
        <Field
          label={t('auth.reset.confirmPassword')}
          type="password"
          autoComplete="new-password"
          registration={form.register('confirmPassword')}
          error={errors.confirmPassword?.message}
        />
        {accept.error && <FieldError>{authErrorMessage(accept.error, t)}</FieldError>}
        {hasAccount && (
          <Link to="/login" className="text-xs font-medium text-info-foreground hover:underline">
            {t('auth.signup.loginLink')}
          </Link>
        )}
        <Button type="submit" disabled={accept.isPending}>
          {t('acceptInvite.submit')}
        </Button>
      </form>
    </AuthCard>
  );
}
