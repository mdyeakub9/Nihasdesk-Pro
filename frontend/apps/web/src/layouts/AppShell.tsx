import { cn } from '@nihasdesk/ui';
import { useTranslation } from 'react-i18next';
import { Outlet } from 'react-router';
import { NotificationPermissionBanner } from '../components/NotificationPermissionBanner';
import { useDocumentSettings } from '../components/useDocumentSettings';
import { MfaReminder } from '../features/account/MfaReminder';
import { ReauthDialog } from '../features/account/ReauthDialog';
import { MentionAlert } from '../features/inbox/MentionAlert';
import { useInboxRealtime } from '../features/inbox/realtime';
import { OrderRequestAlert } from '../features/orders/OrderRequestAlert';
import { useUiStore } from '../stores/ui-store';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';

export function AppShell() {
  useInboxRealtime();
  const { t } = useTranslation();
  const sidebarOpen = useUiStore((state) => state.sidebarOpen);
  const closeSidebar = useUiStore((state) => state.closeSidebar);
  useDocumentSettings();

  return (
    <div className="flex h-dvh overflow-hidden">
      {sidebarOpen && (
        <button
          type="button"
          aria-label={t('nav.closeSidebar')}
          className="fixed inset-0 z-30 bg-foreground/30 md:hidden"
          onClick={closeSidebar}
        />
      )}
      <aside
        id="app-sidebar"
        className={cn(
          'fixed inset-y-0 left-0 z-40 w-60 shrink-0 md:static md:block',
          sidebarOpen ? 'block' : 'hidden',
        )}
      >
        <Sidebar />
      </aside>
      <div className="flex min-w-0 flex-1 flex-col">
        <TopBar />
        <MfaReminder />
        <NotificationPermissionBanner />
        <main className="flex min-h-0 flex-1 flex-col">
          <Outlet />
        </main>
      </div>
      <ReauthDialog />
      <MentionAlert />
      <OrderRequestAlert />
    </div>
  );
}
