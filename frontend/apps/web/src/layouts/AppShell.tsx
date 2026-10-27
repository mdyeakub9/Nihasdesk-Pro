import { cn } from '@nihasdesk/ui';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Outlet } from 'react-router';
import { NotificationPermissionBanner } from '../components/NotificationPermissionBanner';
import { useUiStore } from '../stores/ui-store';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';

export function AppShell() {
  const { t, i18n } = useTranslation();
  const sidebarOpen = useUiStore((state) => state.sidebarOpen);
  const closeSidebar = useUiStore((state) => state.closeSidebar);
  const theme = useUiStore((state) => state.theme);

  useEffect(() => {
    document.documentElement.lang = i18n.resolvedLanguage ?? 'en';
  }, [i18n.resolvedLanguage]);

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
  }, [theme]);

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
        <NotificationPermissionBanner />
        <main className="flex min-h-0 flex-1 flex-col">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
