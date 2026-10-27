import { Button, Checkbox, cn, Input, Tabs, TabsList, TabsTrigger } from '@nihasdesk/ui';
import {
  Bell,
  Bot,
  Check,
  Flame,
  Inbox,
  PackageCheck,
  Search,
  TriangleAlert,
  UserRound,
  type LucideIcon,
} from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { formatAge } from '../../lib/format';
import { isOpen, useNotifications, useResolveNotifications } from './api';
import { notificationTypes, type NotificationType, type TeamNotification } from './types';

type View = 'open' | 'resolved' | 'all';

const typeStyle: Record<NotificationType, { icon: LucideIcon; className: string }> = {
  handoff: { icon: Bot, className: 'text-info-foreground' },
  aiReview: { icon: TriangleAlert, className: 'text-warning-foreground' },
  orderRequest: { icon: PackageCheck, className: 'text-success-foreground' },
  hotLead: { icon: Flame, className: 'text-selected-foreground' },
};

/** Where each notification's main action leads. Order requests are approved on the Orders page (PRD 4.9). */
function actionTarget(notification: TeamNotification) {
  return notification.type === 'orderRequest' ? '/orders' : `/inbox/${notification.conversationId}`;
}

function NotificationRow({
  notification,
  selected,
  onSelect,
  onResolve,
  resolving,
}: {
  notification: TeamNotification;
  selected: boolean;
  onSelect: (checked: boolean) => void;
  onResolve: () => void;
  resolving: boolean;
}) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  const { icon: Icon, className } = typeStyle[notification.type];
  const open = isOpen(notification);
  const firstName = notification.contactName.split(' ')[0] ?? notification.contactName;
  const params = { name: firstName, detail: notification.detail ?? '' };
  const title = t(`notifications.types.${notification.type}.title`, params);

  return (
    <li className="flex gap-3 border-b px-4 py-4 last:border-b-0">
      {open && (
        <Checkbox
          className="mt-2"
          checked={selected}
          onCheckedChange={(checked) => onSelect(checked === true)}
          aria-label={t('notifications.select', { title })}
        />
      )}
      <span className="flex size-8 shrink-0 items-center justify-center rounded-md bg-subtle">
        <Icon className={cn('size-4', className)} aria-hidden="true" />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <p className="text-sm font-medium">{title}</p>
        <p className="text-[13px] text-secondary-foreground">
          {t(`notifications.types.${notification.type}.description`, params)}
        </p>
        <p className="text-xs text-muted-foreground">
          {[
            notification.contactName,
            t(`channels.${notification.channel}`),
            t(`notifications.types.${notification.type}.label`),
            t(notification.assignedToMe ? 'notifications.assignedToYou' : 'notifications.unassigned'),
          ].join(' · ')}
        </p>
      </div>
      <div className="flex shrink-0 flex-col items-end gap-2">
        <time dateTime={notification.createdAt} className="text-xs text-muted-foreground">
          {t('notifications.ago', { age: formatAge(notification.createdAt, language) })}
        </time>
        <div className="flex flex-wrap justify-end gap-2">
          <Button asChild size="sm" variant="outline">
            <Link to={actionTarget(notification)}>
              {t(`notifications.types.${notification.type}.action`)}
            </Link>
          </Button>
          {open ? (
            <Button size="sm" variant="outline" disabled={resolving} onClick={onResolve}>
              <Check aria-hidden="true" />
              {t('notifications.resolve')}
            </Button>
          ) : (
            <span className="flex h-8 items-center text-xs text-success-foreground">
              {t('notifications.resolved')}
            </span>
          )}
        </div>
      </div>
    </li>
  );
}

export function NotificationsPage() {
  const { t } = useTranslation();
  const { data: notifications = [], isPending } = useNotifications();
  const resolve = useResolveNotifications();
  const [view, setView] = useState<View>('open');
  const [type, setType] = useState<NotificationType | 'all'>('all');
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());

  const open = notifications.filter(isOpen);
  const counts: Record<View, number> = {
    open: open.length,
    resolved: notifications.length - open.length,
    all: notifications.length,
  };
  const needle = query.trim().toLocaleLowerCase();
  const visible = notifications.filter(
    (n) =>
      (view === 'all' || (view === 'open') === isOpen(n)) &&
      (type === 'all' || n.type === type) &&
      (!needle || n.contactName.toLocaleLowerCase().includes(needle)),
  );
  const selectable = visible.filter(isOpen);
  const selectedVisible = selectable.filter((n) => selected.has(n.id));

  function toggle(id: string, checked: boolean) {
    setSelected((current) => {
      const next = new Set(current);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  function resolveIds(ids: string[]) {
    resolve.mutate(ids, {
      onSuccess: () => setSelected((current) => new Set([...current].filter((id) => !ids.includes(id)))),
    });
  }

  const summary = [
    { icon: Bell, value: open.length, label: t('notifications.summary.actionNeeded') },
    {
      icon: UserRound,
      value: open.filter((n) => n.assignedToMe).length,
      label: t('notifications.summary.assignedToYou'),
    },
    {
      icon: Bot,
      value: open.filter((n) => n.type === 'aiReview').length,
      label: t('notifications.summary.aiReview'),
    },
  ];

  return (
    <div className="min-h-0 flex-1 overflow-y-auto">
      <div className="flex flex-col gap-4 p-4 md:p-8">
        <div className="flex flex-wrap items-start gap-4">
          <div className="flex-1">
            <h1 className="text-[28px] leading-9 font-semibold">{t('notifications.title')}</h1>
            <p className="mt-1 text-sm text-secondary-foreground">{t('notifications.subtitle')}</p>
          </div>
          <Button asChild variant="outline" size="sm">
            <Link to="/inbox">
              <Inbox aria-hidden="true" />
              {t('notifications.openInbox')}
            </Link>
          </Button>
        </div>

        <div className="flex flex-wrap items-center gap-x-6 gap-y-2 rounded-lg border bg-surface px-4 py-3">
          {summary.map(({ icon: Icon, value, label }) => (
            <p key={label} className="flex items-center gap-2 text-[13px] text-secondary-foreground">
              <Icon className="size-4" aria-hidden="true" />
              <span className="text-base font-semibold text-foreground">{value}</span>
              {label}
            </p>
          ))}
          <p className="ml-auto text-xs text-muted-foreground">{t('notifications.summary.hint')}</p>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <Tabs value={view} onValueChange={(value) => setView(value as View)}>
            <TabsList aria-label={t('notifications.view')}>
              {(['open', 'resolved', 'all'] as const).map((v) => (
                <TabsTrigger key={v} value={v}>
                  {t(`notifications.views.${v}`)}
                  <span className="text-xs text-secondary-foreground">{counts[v]}</span>
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
          <div className="relative ml-auto w-full sm:w-64">
            <Search
              className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
              aria-hidden="true"
            />
            <Input
              type="search"
              aria-label={t('notifications.search')}
              placeholder={t('notifications.search')}
              className="pl-9"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
            />
          </div>
        </div>

        <div
          role="group"
          aria-label={t('notifications.typeFilter')}
          className="flex flex-wrap items-center gap-2"
        >
          <span className="text-xs text-muted-foreground uppercase">{t('notifications.type')}</span>
          {(['all', ...notificationTypes] as const).map((value) => (
            <button
              key={value}
              type="button"
              aria-pressed={type === value}
              onClick={() => setType(value)}
              className={cn(
                'h-8 rounded-md px-3 text-[13px] font-medium hover:bg-subtle focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
                type === value ? 'bg-selected text-selected-foreground hover:bg-selected' : 'bg-surface',
              )}
            >
              {value === 'all' ? t('notifications.allTypes') : t(`notifications.types.${value}.filter`)}
            </button>
          ))}
        </div>

        <section aria-label={t('notifications.list')} className="rounded-lg border bg-surface">
          {selectable.length > 0 && (
            <div className="flex items-center gap-3 border-b px-4 py-2">
              <Checkbox
                id="select-all-notifications"
                checked={selectedVisible.length === selectable.length}
                onCheckedChange={(checked) =>
                  setSelected(checked === true ? new Set(selectable.map((n) => n.id)) : new Set())
                }
              />
              <label htmlFor="select-all-notifications" className="text-[13px] font-medium">
                {t('notifications.selectAll')}
              </label>
              <span className="text-xs text-muted-foreground">
                {t('notifications.updates', { count: selectable.length })}
              </span>
              <Button
                size="sm"
                variant="outline"
                className="ml-auto"
                disabled={selectedVisible.length === 0 || resolve.isPending}
                onClick={() => resolveIds(selectedVisible.map((n) => n.id))}
              >
                {t('notifications.resolveSelected')}
              </Button>
            </div>
          )}
          {!isPending && visible.length === 0 ? (
            <p className="p-8 text-center text-[13px] text-secondary-foreground">
              {t('notifications.empty')}
            </p>
          ) : (
            <ul>
              {visible.map((notification) => (
                <NotificationRow
                  key={notification.id}
                  notification={notification}
                  selected={selected.has(notification.id)}
                  onSelect={(checked) => toggle(notification.id, checked)}
                  onResolve={() => resolveIds([notification.id])}
                  resolving={resolve.isPending}
                />
              ))}
            </ul>
          )}
          <div className="flex flex-wrap justify-between gap-2 border-t px-4 py-2 text-xs text-muted-foreground">
            <span>{t('notifications.showing', { shown: visible.length, total: counts[view] })}</span>
            <span>{t('notifications.openingHint')}</span>
          </div>
        </section>
      </div>
    </div>
  );
}
