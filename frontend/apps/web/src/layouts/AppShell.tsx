import { Button, cn } from '@nihasdesk/ui';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { NavLink, Outlet } from 'react-router';
import { LanguageSwitcher } from '../components/LanguageSwitcher';
import { ApiStatus } from '../features/health/ApiStatus';
import { useUiStore } from '../stores/ui-store';

/** Sections from PRD phase 1; only Home exists so far. */
const upcomingSections = ['inbox', 'contacts', 'orders', 'settings'] as const;

export function AppShell() {
  const { t, i18n } = useTranslation();
  const sidebarOpen = useUiStore((state) => state.sidebarOpen);
  const toggleSidebar = useUiStore((state) => state.toggleSidebar);

  useEffect(() => {
    document.documentElement.lang = i18n.resolvedLanguage ?? 'en';
  }, [i18n.resolvedLanguage]);

  return (
    <div className="flex min-h-screen">
      {sidebarOpen && (
        <aside className="w-60 shrink-0 border-r p-4">
          <div className="mb-6 text-lg font-semibold">{t('app.name')}</div>
          <nav className="flex flex-col gap-1">
            <NavLink
              to="/"
              end
              className={({ isActive }) =>
                cn('rounded-md px-3 py-2 text-sm hover:bg-muted', isActive && 'bg-muted font-medium')
              }
            >
              {t('nav.home')}
            </NavLink>
            {upcomingSections.map((section) => (
              <span
                key={section}
                aria-disabled="true"
                title={t('home.comingSoon')}
                className="cursor-not-allowed rounded-md px-3 py-2 text-sm text-muted-foreground"
              >
                {t(`nav.${section}`)}
              </span>
            ))}
          </nav>
        </aside>
      )}
      <div className="flex flex-1 flex-col">
        <header className="flex items-center justify-between gap-4 border-b px-4 py-3">
          <Button variant="ghost" size="sm" onClick={toggleSidebar} aria-expanded={sidebarOpen}>
            {t('nav.toggleSidebar')}
          </Button>
          <div className="flex items-center gap-6">
            <ApiStatus />
            <LanguageSwitcher />
          </div>
        </header>
        <main className="flex-1 p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
