import { zodResolver } from '@hookform/resolvers/zod';
import { Badge, Button, FieldError } from '@nihasdesk/ui';
import { useState, type ReactNode } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { authErrorMessage } from '../auth/errors';
import { Field } from '../auth/Field';
import { newPasswordField } from '../auth/schemas';
import {
  useChangePassword,
  useDisableMfa,
  useEnableMfa,
  useMfaStatus,
  useNewRecoveryCodes,
  useRevokeOtherSessions,
  useRevokeSession,
  useSessions,
  useStartMfaSetup,
  type MfaSetup,
} from './api';
import { deviceLabel } from './device';
import { QrCode } from './QrCode';

function Section({ title, intro, children }: { title: string; intro?: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-surface p-4">
      <div className="flex flex-col gap-1">
        <h2 className="text-sm font-semibold">{title}</h2>
        {intro && <p className="text-[13px] text-secondary-foreground">{intro}</p>}
      </div>
      {children}
    </section>
  );
}

/** Shown once after turning MFA on or asking for new codes; copy or download, then done. */
function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const { t } = useTranslation();
  const text = codes.join('\n');

  function download() {
    const url = URL.createObjectURL(
      new Blob([`Nihasdesk recovery codes\n\n${text}\n`], { type: 'text/plain' }),
    );
    const link = document.createElement('a');
    link.href = url;
    link.download = 'nihasdesk-recovery-codes.txt';
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <div className="flex flex-col gap-3">
      <p className="rounded-md bg-warning px-3 py-2 text-[13px] text-warning-foreground">
        {t('account.mfa.codesWarning')}
      </p>
      <ul
        aria-label={t('account.mfa.codesLabel')}
        className="grid grid-cols-2 gap-1 rounded-md bg-subtle p-3 font-mono text-[13px]"
      >
        {codes.map((code) => (
          <li key={code}>{code}</li>
        ))}
      </ul>
      <div className="flex flex-wrap gap-2">
        <Button size="sm" variant="outline" onClick={download}>
          {t('account.mfa.download')}
        </Button>
        <Button size="sm" variant="outline" onClick={() => void navigator.clipboard?.writeText(text)}>
          {t('account.mfa.copy')}
        </Button>
        <Button size="sm" onClick={onDone}>
          {t('account.mfa.saved')}
        </Button>
      </div>
    </div>
  );
}

function TwoStepSection() {
  const { t } = useTranslation();
  const { data: status } = useMfaStatus();
  const start = useStartMfaSetup();
  const enable = useEnableMfa();
  const disable = useDisableMfa();
  const newCodes = useNewRecoveryCodes();
  const [setup, setSetup] = useState<MfaSetup | null>(null);
  const [codes, setCodes] = useState<string[] | null>(null);
  const [confirmOff, setConfirmOff] = useState(false);
  const form = useForm<{ code: string }>({ defaultValues: { code: '' } });
  const error = start.error ?? enable.error ?? disable.error ?? newCodes.error;

  if (codes) {
    return (
      <Section title={t('account.mfa.title')}>
        <RecoveryCodes codes={codes} onDone={() => setCodes(null)} />
      </Section>
    );
  }

  if (setup) {
    return (
      <Section title={t('account.mfa.title')} intro={t('account.mfa.scan')}>
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start">
          <QrCode value={setup.otpauthUri} label={t('account.mfa.qrLabel')} />
          <div className="flex flex-1 flex-col gap-3">
            <p className="text-xs text-muted-foreground">
              {t('account.mfa.manual')}{' '}
              <code className="rounded bg-subtle px-1 py-0.5 break-all">
                {setup.secret.match(/.{1,4}/g)?.join(' ')}
              </code>
            </p>
            <form
              noValidate
              className="flex flex-col gap-3"
              onSubmit={form.handleSubmit(({ code }) =>
                enable.mutate(code.trim(), {
                  onSuccess: (result) => {
                    setSetup(null);
                    setCodes(result.recoveryCodes);
                  },
                }),
              )}
            >
              <Field
                label={t('auth.mfa.code')}
                autoComplete="one-time-code"
                inputMode="numeric"
                maxLength={6}
                registration={form.register('code')}
              />
              {error && <FieldError>{authErrorMessage(error, t)}</FieldError>}
              <div className="flex gap-2">
                <Button type="submit" disabled={enable.isPending}>
                  {t('account.mfa.turnOn')}
                </Button>
                <Button type="button" variant="ghost" onClick={() => setSetup(null)}>
                  {t('team.cancel')}
                </Button>
              </div>
            </form>
          </div>
        </div>
      </Section>
    );
  }

  return (
    <Section title={t('account.mfa.title')} intro={t('account.mfa.intro')}>
      {status?.enabled ? (
        <div className="flex flex-wrap items-center gap-3">
          <Badge tone="success">{t('account.mfa.on')}</Badge>
          <span className="text-xs text-muted-foreground">
            {t('account.mfa.codesLeft', { count: status.recoveryCodesLeft })}
          </span>
          <span className="flex-1" />
          <Button
            size="sm"
            variant="outline"
            disabled={newCodes.isPending}
            onClick={() =>
              newCodes.mutate(undefined, { onSuccess: (result) => setCodes(result.recoveryCodes) })
            }
          >
            {t('account.mfa.newCodes')}
          </Button>
          {confirmOff ? (
            <>
              <Button
                size="sm"
                variant="destructive"
                disabled={disable.isPending}
                onClick={() => disable.mutate(undefined, { onSettled: () => setConfirmOff(false) })}
              >
                {t('account.mfa.confirmOff')}
              </Button>
              <Button size="sm" variant="ghost" onClick={() => setConfirmOff(false)}>
                {t('team.cancel')}
              </Button>
            </>
          ) : (
            <Button size="sm" variant="ghost" onClick={() => setConfirmOff(true)}>
              {t('account.mfa.turnOff')}
            </Button>
          )}
        </div>
      ) : (
        <div className="flex flex-wrap items-center gap-3">
          <Badge>{t('account.mfa.off')}</Badge>
          <span className="flex-1" />
          <Button
            size="sm"
            disabled={start.isPending}
            onClick={() => start.mutate(undefined, { onSuccess: setSetup })}
          >
            {t('account.mfa.setUp')}
          </Button>
        </div>
      )}
      {error && <FieldError>{authErrorMessage(error, t)}</FieldError>}
    </Section>
  );
}

function SessionsSection() {
  const { t, i18n } = useTranslation();
  const { data: sessions = [] } = useSessions();
  const revoke = useRevokeSession();
  const revokeOthers = useRevokeOtherSessions();
  const format = (iso: string) =>
    new Intl.DateTimeFormat(i18n.resolvedLanguage ?? 'en', {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(iso));

  return (
    <Section title={t('account.sessions.title')} intro={t('account.sessions.intro')}>
      <ul className="flex flex-col divide-y rounded-md border">
        {sessions.map((session) => {
          const { browser, os } = deviceLabel(session.userAgent);
          const name =
            browser && os
              ? t('account.sessions.device', { browser, os })
              : (browser ?? os ?? t('account.sessions.unknown'));
          return (
            <li key={session.id} className="flex flex-wrap items-center gap-3 px-3 py-2">
              <div className="flex min-w-0 flex-1 flex-col">
                <span className="flex items-center gap-2 text-sm font-medium">
                  {name}
                  {session.current && <Badge tone="success">{t('account.sessions.thisDevice')}</Badge>}
                </span>
                <span className="text-xs text-muted-foreground">
                  {t('account.sessions.lastActive', { date: format(session.lastSeenAt) })}
                </span>
              </div>
              {!session.current && (
                <Button
                  size="sm"
                  variant="outline"
                  disabled={revoke.isPending}
                  onClick={() => revoke.mutate(session.id)}
                >
                  {t('account.sessions.signOut')}
                </Button>
              )}
            </li>
          );
        })}
      </ul>
      {sessions.length > 1 && (
        <div>
          <Button
            size="sm"
            variant="outline"
            disabled={revokeOthers.isPending}
            onClick={() => revokeOthers.mutate(undefined)}
          >
            {t('account.sessions.signOutOthers')}
          </Button>
        </div>
      )}
    </Section>
  );
}

function PasswordSection() {
  const { t } = useTranslation();
  const change = useChangePassword();
  const schema = z
    .object({
      currentPassword: z.string().min(1, t('validation.required')),
      newPassword: newPasswordField(t),
      confirmPassword: z.string(),
    })
    .refine((v) => v.newPassword === v.confirmPassword, {
      path: ['confirmPassword'],
      message: t('validation.passwordMismatch'),
    });
  type Values = z.infer<typeof schema>;
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { currentPassword: '', newPassword: '', confirmPassword: '' },
  });
  const { errors } = form.formState;

  return (
    <Section title={t('account.password.title')} intro={t('account.password.intro')}>
      <form
        noValidate
        className="flex max-w-sm flex-col gap-3"
        onSubmit={form.handleSubmit(({ currentPassword, newPassword }) =>
          change.mutate({ currentPassword, newPassword }, { onSuccess: () => form.reset() }),
        )}
      >
        <Field
          label={t('account.password.current')}
          type="password"
          autoComplete="current-password"
          registration={form.register('currentPassword')}
          error={errors.currentPassword?.message}
        />
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
        {change.error && <FieldError>{authErrorMessage(change.error, t)}</FieldError>}
        {change.isSuccess && (
          <p role="status" className="text-[13px] text-success-foreground">
            {t('account.password.changed')}
          </p>
        )}
        <div>
          <Button type="submit" disabled={change.isPending}>
            {t('account.password.submit')}
          </Button>
        </div>
      </form>
    </Section>
  );
}

/** Settings → Security (ADR 0015): two-step sign-in, signed-in devices, password. */
export function SecurityPage() {
  return (
    <div className="flex max-w-3xl flex-col gap-4">
      <TwoStepSection />
      <SessionsSection />
      <PasswordSection />
    </div>
  );
}
