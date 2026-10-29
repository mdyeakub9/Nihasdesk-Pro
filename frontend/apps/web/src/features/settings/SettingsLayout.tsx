import { cn } from '@nihasdesk/ui';
import { useTranslation } from 'react-i18next';
import { NavLink, Outlet } from 'react-router';
import { useSession } from '../session/useSession';
import type { Role } from '../session/types';

/** Settings sections; more (workspace, AI) arrive with their features. `roles` only hides; the API decides. */
const sections: { to: string; label: string; roles?: Role[] }[] = [
  { to: 'team', label: 'settings.team' },
  { to: 'channels', label: 'settings.channels', roles: ['owner', 'admin'] },
  { to: 'ai', label: 'settings.ai', roles: ['owner', 'admin'] },
  { to: 'knowledge', label: 'settings.knowledge' },
  { to: 'security', label: 'settings.security' },
];

export function SettingsLayout() {
  const { t } = useTranslation();
  const { data: session } = useSession();
  const visible = sections.filter(({ roles }) => !roles || (session && roles.includes(session.role)));

  return (
    <div className="min-h-0 flex-1 overflow-y-auto">
      <div className="flex flex-col gap-6 p-4 md:p-8">
        <h1 className="text-[28px] leading-9 font-semibold">{t('nav.settings')}</h1>
        <nav aria-label={t('settings.sections')} className="flex gap-1 border-b">
          {visible.map(({ to, label }) => (
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
