import { Badge, Button } from '@nihasdesk/ui';
import { Menu } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { LanguageSwitcher } from '../components/LanguageSwitcher';
import { ThemeToggle } from '../components/ThemeToggle';
import { ApiStatus } from '../features/health/ApiStatus';
import { useUiStore } from '../stores/ui-store';

export function TopBar() {
  const { t } = useTranslation();
  const sidebarOpen = useUiStore((state) => state.sidebarOpen);
  const toggleSidebar = useUiStore((state) => state.toggleSidebar);

  return (
    <header className="flex h-16 shrink-0 items-center gap-3 border-b bg-surface px-4 md:px-6">
      <Button
        variant="ghost"
        size="icon"
        className="md:hidden"
        onClick={toggleSidebar}
        aria-expanded={sidebarOpen}
        aria-controls="app-sidebar"
        aria-label={t('nav.toggleSidebar')}
      >
        <Menu aria-hidden="true" />
      </Button>
      <Badge tone="success" className="hidden sm:inline-flex">
        {t('topBar.live')}
      </Badge>
      <span className="hidden text-xs text-secondary-foreground uppercase sm:inline">{t('topBar.mode')}</span>
      <div className="flex-1" />
      <LanguageSwitcher />
      <ThemeToggle />
      <ApiStatus />
    </header>
  );
}
