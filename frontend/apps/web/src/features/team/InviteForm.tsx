import { zodResolver } from '@hookform/resolvers/zod';
import { Button, FieldError, Input, Label, NativeSelect } from '@nihasdesk/ui';
import { useId } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { authErrorMessage } from '../auth/errors';
import { emailField } from '../auth/schemas';
import { useInvite } from './api';

/** Invite by email. Only Owners see the Admin option; the API checks it too. */
export function InviteForm({ canInviteAdmin }: { canInviteAdmin: boolean }) {
  const { t } = useTranslation();
  const emailId = useId();
  const roleId = useId();
  const invite = useInvite();

  const schema = z.object({ email: emailField(t), role: z.enum(['member', 'admin']) });
  type Values = z.infer<typeof schema>;
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', role: 'member' },
  });

  const submit = form.handleSubmit((values) =>
    invite.mutate(values, { onSuccess: () => form.reset({ email: '', role: values.role }) }),
  );

  return (
    <form noValidate onSubmit={submit} className="flex flex-col gap-3 rounded-lg border bg-surface p-4">
      <h2 className="text-sm font-semibold">{t('team.invite.title')}</h2>
      <div className="flex flex-col gap-3 lg:flex-row lg:items-end">
        <div className="flex flex-1 flex-col gap-1.5">
          <Label htmlFor={emailId}>{t('auth.email')}</Label>
          <Input
            id={emailId}
            type="email"
            autoComplete="off"
            placeholder={t('team.invite.placeholder')}
            aria-invalid={form.formState.errors.email ? true : undefined}
            {...form.register('email')}
          />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor={roleId}>{t('team.role')}</Label>
          <NativeSelect id={roleId} className="h-9 lg:w-40" {...form.register('role')}>
            <option value="member">{t('roles.member')}</option>
            {canInviteAdmin && <option value="admin">{t('roles.admin')}</option>}
          </NativeSelect>
        </div>
        <Button type="submit" disabled={invite.isPending}>
          {t('team.invite.submit')}
        </Button>
      </div>
      {form.formState.errors.email && <FieldError>{form.formState.errors.email.message}</FieldError>}
      {invite.error && <FieldError>{authErrorMessage(invite.error, t)}</FieldError>}
      {invite.isSuccess && (
        <p role="status" className="text-[13px] text-success-foreground">
          {t('team.invite.sent')}
        </p>
      )}
    </form>
  );
}
