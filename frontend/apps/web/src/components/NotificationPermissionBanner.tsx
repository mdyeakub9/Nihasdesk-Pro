import { Button } from '@nihasdesk/ui';
import { Bell } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUiStore } from '../stores/ui-store';

/**
 * Asks for browser notification permission (PRD 4.10 web push). The push subscription itself comes
 * with the notifications backend; this only gets the permission prompt out of the way.
 */
export function NotificationPermissionBanner() {
  const { t } = useTranslation();
  const dismissed = useUiStore((state) => state.notificationBannerDismissed);
  const dismiss = useUiStore((state) => state.dismissNotificationBanner);
  const [permission, setPermission] = useState(() =>
    typeof Notification === 'undefined' ? 'unsupported' : Notification.permission,
  );

  if (dismissed || permission !== 'default') return null;

  async function enable() {
    setPermission(await Notification.requestPermission());
  }

  return (
    <div className="flex min-h-14 flex-wrap items-center gap-3 border-b bg-background px-6 py-2 text-[13px] text-secondary-foreground">
      <Bell className="size-4 shrink-0" aria-hidden="true" />
      <p className="flex-1">{t('notificationBanner.text')}</p>
      <Button variant="outline" size="sm" onClick={() => void enable()}>
        {t('notificationBanner.enable')}
      </Button>
      <Button variant="ghost" size="sm" onClick={dismiss}>
        {t('notificationBanner.notNow')}
      </Button>
    </div>
  );
}
