import { Avatar, Badge, cn } from '@nihasdesk/ui';
import {
  Bell,
  ChartColumn,
  Check,
  ChevronDown,
  Inbox,
  LayoutGrid,
  Package,
  Settings,
  ShoppingBag,
  Target,
  Users,
  type LucideIcon,
} from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { NavLink } from 'react-router';
import { countByTab } from '../features/inbox/filters';
import { useConversations } from '../features/inbox/api';
import { isOpen, useNotifications } from '../features/notifications/api';
import { useSession } from '../features/session/useSession';
import { useUiStore } from '../stores/ui-store';

type NavItem = { to: string; label: string; icon: LucideIcon; count?: number };

function SidebarLink({ to, label, icon: Icon, count }: NavItem) {
  const { t } = useTranslation();
  const closeSidebar = useUiStore((state) => state.closeSidebar);
  return (
    <NavLink
      to={to}
      end={to === '/'}
      onClick={closeSidebar}
      className={({ isActive }) =>
        cn(
          'flex h-8 items-center gap-2 rounded-lg px-3 text-sm font-medium text-secondary-foreground hover:bg-subtle',
          isActive && 'bg-selected text-selected-foreground hover:bg-selected',
        )
      }
    >
      <Icon className="size-5 shrink-0" strokeWidth={1.5} aria-hidden="true" />
      <span className="flex-1 truncate">{t(label)}</span>
      {count !== undefined && count > 0 && <span className="text-xs">{count}</span>}
    </NavLink>
  );
}

function SectionLabel({ children }: { children: string }) {
  return <p className="mt-2 px-0 text-[11px] font-semibold uppercase text-muted-foreground">{children}</p>;
}

export function Sidebar() {
  const { t } = useTranslation();
  const { data: session } = useSession();
  const { data: conversations = [] } = useConversations();
  const { data: notifications = [] } = useNotifications();

  const workspace: NavItem[] = [
    { to: '/', label: 'nav.overview', icon: LayoutGrid },
    { to: '/inbox', label: 'nav.inbox', icon: Inbox, count: countByTab(conversations).open },
    {
      to: '/notifications',
      label: 'nav.notifications',
      icon: Bell,
      count: notifications.filter(isOpen).length,
    },
    { to: '/orders', label: 'nav.orders', icon: ShoppingBag },
    { to: '/leads', label: 'nav.leads', icon: Target },
    { to: '/customers', label: 'nav.customers', icon: Users },
  ];
  const manage: NavItem[] = [
    { to: '/products', label: 'nav.products', icon: Package },
    { to: '/analytics', label: 'nav.analytics', icon: ChartColumn },
  ];

  return (
    <div className="flex h-full flex-col gap-0.5 overflow-y-auto border-r bg-surface p-3">
      <div className="flex h-16 shrink-0 items-center gap-3 px-3">
        <span
          aria-hidden="true"
          className="flex size-10 items-center justify-center rounded-lg bg-primary text-base font-semibold text-primary-foreground"
        >
          N
        </span>
        <div className="flex flex-col">
          <span className="text-base font-semibold">{t('app.name')}</span>
          <span className="text-[11px] font-semibold uppercase text-muted-foreground">
            {t('sidebar.tagline')}
          </span>
        </div>
      </div>

      {session && (
        <>
          <div className="flex h-11 items-center gap-2 rounded-lg bg-background px-3">
            <Avatar name={session.workspace.name} />
            <span className="flex-1 truncate text-sm font-medium">{session.workspace.name}</span>
            <ChevronDown className="size-4 text-muted-foreground" aria-hidden="true" />
          </div>
          <div className="mt-0.5 flex flex-col gap-1 rounded-lg bg-selected px-3 py-2">
            <span className="flex items-center gap-2 text-[13px] font-medium text-selected-foreground">
              <Check className="size-4" aria-hidden="true" />
              {t('sidebar.setupGuide', { completed: session.setup.completed, total: session.setup.total })}
            </span>
            <span
              role="progressbar"
              aria-label={t('sidebar.setupProgress')}
              aria-valuemin={0}
              aria-valuemax={session.setup.total}
              aria-valuenow={session.setup.completed}
              className="h-1 overflow-hidden bg-border"
            >
              <span
                className="block h-full bg-primary"
                style={{ width: `${(session.setup.completed / session.setup.total) * 100}%` }}
              />
            </span>
          </div>
        </>
      )}

      <nav aria-label={t('sidebar.navigation')} className="flex flex-col gap-0.5">
        <SectionLabel>{t('sidebar.workspace')}</SectionLabel>
        {workspace.map((item) => (
          <SidebarLink key={item.to} {...item} />
        ))}
        <SectionLabel>{t('sidebar.growManage')}</SectionLabel>
        {manage.map((item) => (
          <SidebarLink key={item.to} {...item} />
        ))}
      </nav>

      <div className="flex-1" />

      {session && (
        <div className="px-3 py-1">
          <Badge tone="success">{t('sidebar.onDuty')}</Badge>
        </div>
      )}
      <SidebarLink to="/settings" label="nav.settings" icon={Settings} />
      {session && (
        <div className="flex h-12 items-center gap-3 px-3">
          <Avatar name={session.user.name} size="md" />
          <div className="flex min-w-0 flex-1 flex-col">
            <span className="truncate text-[13px] font-medium">{session.user.name}</span>
            <span className="text-xs text-muted-foreground">{t(`roles.${session.user.role}`)}</span>
          </div>
          <ChevronDown className="size-4 text-muted-foreground" aria-hidden="true" />
        </div>
      )}
    </div>
  );
}
