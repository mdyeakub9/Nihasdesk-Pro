import { Avatar, Badge, Button, FieldError, NativeSelect } from '@nihasdesk/ui';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { authErrorMessage } from '../auth/errors';
import { useSession } from '../session/useSession';
import {
  useChangeRole,
  useInvitations,
  useMembers,
  useRemoveMember,
  useResendInvitation,
  useRevokeInvitation,
  type Member,
} from './api';
import { InviteForm } from './InviteForm';
import { can, canRemove } from './permissions';

function formatDate(iso: string, language: string) {
  return new Intl.DateTimeFormat(language, { day: 'numeric', month: 'short', year: 'numeric' }).format(
    new Date(iso),
  );
}

function MemberRow({ member, isMe }: { member: Member; isMe: boolean }) {
  const { t, i18n } = useTranslation();
  const { data: session } = useSession();
  const changeRole = useChangeRole();
  const remove = useRemoveMember();
  const [confirming, setConfirming] = useState(false);
  const myRole = session?.role ?? 'member';
  const editableRole = can(myRole, 'changeRole') && member.role !== 'owner';
  const error = changeRole.error ?? remove.error;

  return (
    <li className="flex flex-wrap items-center gap-3 border-b px-4 py-3 last:border-b-0">
      <Avatar name={member.name} size="md" />
      <div className="flex min-w-0 flex-1 flex-col">
        <span className="truncate text-sm font-medium">
          {member.name}
          {isMe && <span className="text-muted-foreground"> · {t('team.you')}</span>}
        </span>
        <span className="truncate text-xs text-muted-foreground">
          {member.email} ·{' '}
          {t('team.joined', { date: formatDate(member.joinedAt, i18n.resolvedLanguage ?? 'en') })}
        </span>
      </div>
      {editableRole ? (
        <NativeSelect
          aria-label={t('team.roleOf', { name: member.name })}
          className="w-36"
          value={member.role}
          disabled={changeRole.isPending}
          onChange={(event) =>
            changeRole.mutate({ userId: member.userId, role: event.target.value as 'admin' | 'member' })
          }
        >
          <option value="admin">{t('roles.admin')}</option>
          <option value="member">{t('roles.member')}</option>
        </NativeSelect>
      ) : (
        <Badge tone={member.role === 'owner' ? 'ai' : 'neutral'}>{t(`roles.${member.role}`)}</Badge>
      )}
      {!isMe && canRemove(myRole, member.role) && (
        <div className="flex items-center gap-2">
          {confirming ? (
            <>
              <Button
                size="sm"
                variant="destructive"
                disabled={remove.isPending}
                onClick={() => remove.mutate(member.userId)}
              >
                {t('team.removeConfirm')}
              </Button>
              <Button size="sm" variant="ghost" onClick={() => setConfirming(false)}>
                {t('team.cancel')}
              </Button>
            </>
          ) : (
            <Button size="sm" variant="outline" onClick={() => setConfirming(true)}>
              {t('team.remove')}
            </Button>
          )}
        </div>
      )}
      {error && <FieldError className="w-full">{authErrorMessage(error, t)}</FieldError>}
    </li>
  );
}

function PendingInvitations() {
  const { t, i18n } = useTranslation();
  const { data: invitations = [] } = useInvitations(true);
  const resend = useResendInvitation();
  const revoke = useRevokeInvitation();
  const language = i18n.resolvedLanguage ?? 'en';

  if (invitations.length === 0) return null;

  return (
    <section aria-labelledby="pending-heading" className="flex flex-col gap-2">
      <h2 id="pending-heading" className="text-sm font-semibold">
        {t('team.pending.title', { count: invitations.length })}
      </h2>
      <ul className="rounded-lg border bg-surface">
        {invitations.map((invitation) => (
          <li
            key={invitation.id}
            className="flex flex-wrap items-center gap-3 border-b px-4 py-3 last:border-b-0"
          >
            <div className="flex min-w-0 flex-1 flex-col">
              <span className="truncate text-sm font-medium">{invitation.email}</span>
              <span className="text-xs text-muted-foreground">
                {t(`roles.${invitation.role}`)} ·{' '}
                {t('team.pending.expires', { date: formatDate(invitation.expiresAt, language) })}
              </span>
            </div>
            <Button
              size="sm"
              variant="outline"
              disabled={resend.isPending}
              onClick={() => resend.mutate(invitation.id)}
            >
              {t('team.pending.resend')}
            </Button>
            <Button
              size="sm"
              variant="ghost"
              disabled={revoke.isPending}
              onClick={() => revoke.mutate(invitation.id)}
            >
              {t('team.pending.revoke')}
            </Button>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Settings → Team (docs/standards/permissions.md). Hidden actions are hidden for clarity, not security. */
export function TeamPage() {
  const { t } = useTranslation();
  const { data: session } = useSession();
  const { data: members = [], isPending } = useMembers();
  const role = session?.role ?? 'member';

  return (
    <div className="flex max-w-3xl flex-col gap-6">
      <p className="text-sm text-secondary-foreground">{t('team.intro')}</p>
      {can(role, 'invite') && <InviteForm canInviteAdmin={can(role, 'inviteAdmin')} />}
      {can(role, 'invite') && <PendingInvitations />}
      <section aria-labelledby="members-heading" className="flex flex-col gap-2">
        <h2 id="members-heading" className="text-sm font-semibold">
          {t('team.members', { count: members.length })}
        </h2>
        <ul aria-busy={isPending} className="rounded-lg border bg-surface">
          {members.map((member) => (
            <MemberRow key={member.userId} member={member} isMe={member.userId === session?.user.id} />
          ))}
        </ul>
      </section>
    </div>
  );
}
