import { Button } from '@nihasdesk/ui';
import { ShieldCheck } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { useUiStore } from '../../stores/ui-store';
import { useSession } from '../session/useSession';

/**
 * ADR 0015: MFA is optional during the beta, so Owners without it get a gentle, dismissible
 * reminder (dismissal lasts for this session only; nothing is stored).
 */
export function MfaReminder() {
  const { t } = useTranslation();
  const { data: session } = useSession();
  const dismissed = useUiStore((state) => state.mfaReminderDismissed);
  const dismiss = useUiStore((state) => state.dismissMfaReminder);

  if (!session || session.role !== 'owner' || session.user.mfaEnabled || dismissed) return null;

  return (
    <div
      role="note"
      className="flex min-h-12 flex-wrap items-center gap-3 border-b bg-info px-6 py-2 text-[13px] text-info-foreground"
    >
      <ShieldCheck className="size-4 shrink-0" aria-hidden="true" />
      <p className="flex-1">{t('account.reminder.text')}</p>
      <Button asChild size="sm" variant="outline">
        <Link to="/settings/security">{t('account.reminder.action')}</Link>
      </Button>
      <Button size="sm" variant="ghost" onClick={dismiss}>
        {t('notificationBanner.notNow')}
      </Button>
    </div>
  );
}
