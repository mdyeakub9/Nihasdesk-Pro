import { cn } from '@nihasdesk/ui';
import { useTranslation } from 'react-i18next';
import { NavLink, Outlet } from 'react-router';

/** Settings sections; more (workspace, channels, AI) arrive with their features. */
const sections = [{ to: 'team', label: 'settings.team' }] as const;

export function SettingsLayout() {
  const { t } = useTranslation();

  return (
    <div className="min-h-0 flex-1 overflow-y-auto">
      <div className="flex flex-col gap-6 p-4 md:p-8">
        <h1 className="text-[28px] leading-9 font-semibold">{t('nav.settings')}</h1>
        <nav aria-label={t('settings.sections')} className="flex gap-1 border-b">
          {sections.map(({ to, label }) => (
            <NavLink
              key={to}
              to={to}
              className={({ isActive }) =>
                cn(
                  '-mb-px border-b-2 px-3 py-2 text-[13px] font-medium',
                  isActive
                    ? 'border-primary text-foreground'
                    : 'border-transparent text-secondary-foreground hover:text-foreground',
                )
              }
            >
              {t(label)}
            </NavLink>
          ))}
        </nav>
        <Outlet />
      </div>
    </div>
  );
}
