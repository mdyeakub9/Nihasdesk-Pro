import { Avatar, Badge, cn, Input, NativeSelect } from '@nihasdesk/ui';
import { Search, Sparkles } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { ChannelIcon } from '../../components/ChannelIcon';
import { formatAge } from '../../lib/format';
import { useConversations } from './api';
import { countByTab, defaultFilters, filterConversations, inboxTabs, type InboxFilters } from './filters';
import { channels, type ConversationSummary } from './types';

function FilterButton({
  pressed,
  onClick,
  children,
  label,
}: {
  pressed: boolean;
  onClick: () => void;
  children: ReactNode;
  label?: string;
}) {
  return (
    <button
      type="button"
      aria-pressed={pressed}
      aria-label={label}
      onClick={onClick}
      className={cn(
        'flex h-8 items-center gap-1.5 rounded-md border px-3 text-[13px] font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
        pressed ? 'border-transparent bg-selected text-selected-foreground' : 'bg-surface hover:bg-subtle',
      )}
    >
      {children}
    </button>
  );
}

function ConversationRow({
  conversation,
  selected,
}: {
  conversation: ConversationSummary;
  selected: boolean;
}) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';

  return (
    <li>
      <Link
        to={`/inbox/${conversation.id}`}
        aria-current={selected ? 'page' : undefined}
        className={cn(
          'flex gap-3 border-b px-4 py-3 hover:bg-subtle focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset',
          selected && 'bg-selected hover:bg-selected',
        )}
      >
        <Avatar
          name={conversation.contactName}
          className="mt-1"
          badge={<ChannelIcon channel={conversation.channel} variant="badge" />}
        />
        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
          <div className="flex items-baseline gap-2">
            <span
              className={cn('flex-1 truncate text-sm', conversation.unread ? 'font-semibold' : 'font-medium')}
            >
              {conversation.contactName}
            </span>
            <time dateTime={conversation.lastMessageAt} className="text-xs text-muted-foreground">
              {formatAge(conversation.lastMessageAt, language)}
            </time>
          </div>
          <div className="flex items-center gap-2">
            <span className="flex-1 truncate text-[13px] text-secondary-foreground">
              {conversation.preview}
            </span>
            {conversation.responder === 'ai' && conversation.status !== 'done' && (
              <Badge tone="info">
                <Sparkles aria-hidden="true" />
                {t('inbox.ai')}
              </Badge>
            )}
            {conversation.unread && (
              <span className="size-2 rounded-full bg-info-foreground">
                <span className="sr-only">{t('inbox.unread')}</span>
              </span>
            )}
          </div>
        </div>
      </Link>
    </li>
  );
}

export function ConversationList({ selectedId }: { selectedId: string | undefined }) {
  const { t } = useTranslation();
  const { data: conversations = [], isPending } = useConversations();
  const [filters, setFilters] = useState<InboxFilters>(defaultFilters);
  const set = (patch: Partial<InboxFilters>) => setFilters((current) => ({ ...current, ...patch }));

  const counts = countByTab(conversations);
  const visible = filterConversations(conversations, filters);
  const inTab = filterConversations(conversations, { ...defaultFilters, tab: filters.tab });

  return (
    <section aria-labelledby="conversations-heading" className="flex h-full flex-col bg-surface">
      <div className="flex flex-col gap-3 border-b p-4">
        <h2 id="conversations-heading" className="text-base font-semibold">
          {t('inbox.title')}
        </h2>
        <div className="relative">
          <Search
            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden="true"
          />
          <Input
            type="search"
            aria-label={t('inbox.search')}
            placeholder={t('inbox.search')}
            className="bg-background pl-9"
            value={filters.query}
            onChange={(event) => set({ query: event.target.value })}
          />
        </div>
        <div role="group" aria-label={t('inbox.statusFilter')} className="flex flex-wrap gap-1">
          {inboxTabs.map((tab) => (
            <button
              key={tab}
              type="button"
              aria-pressed={filters.tab === tab}
              onClick={() => set({ tab })}
              className={cn(
                'flex h-8 items-center gap-2 rounded-md px-3 text-[13px] font-medium text-secondary-foreground hover:bg-subtle focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
                filters.tab === tab && 'bg-selected text-selected-foreground hover:bg-selected',
              )}
            >
              {t(`inbox.tabs.${tab}`)}
              <span className="text-xs text-secondary-foreground">{counts[tab]}</span>
            </button>
          ))}
        </div>
        <div role="group" aria-label={t('inbox.channelFilter')} className="flex flex-wrap gap-2">
          <FilterButton pressed={filters.channel === 'all'} onClick={() => set({ channel: 'all' })}>
            {t('inbox.allChannels')}
          </FilterButton>
          {channels.map((channel) => (
            <FilterButton
              key={channel}
              pressed={filters.channel === channel}
              onClick={() => set({ channel })}
              label={t(`channels.${channel}`)}
            >
              <ChannelIcon channel={channel} />
              <span aria-hidden="true">{inTab.filter((c) => c.channel === channel).length}</span>
            </FilterButton>
          ))}
        </div>
        <NativeSelect
          aria-label={t('inbox.assignee')}
          className="w-40"
          value={filters.assignee}
          onChange={(event) => set({ assignee: event.target.value as InboxFilters['assignee'] })}
        >
          <option value="everyone">{t('inbox.everyone')}</option>
          <option value="mine">{t('inbox.mine')}</option>
        </NativeSelect>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto">
        {!isPending && visible.length === 0 ? (
          <p className="p-6 text-center text-[13px] text-secondary-foreground">{t('inbox.noMatches')}</p>
        ) : (
          <ul aria-label={t('inbox.title')}>
            {visible.map((conversation) => (
              <ConversationRow
                key={conversation.id}
                conversation={conversation}
                selected={conversation.id === selectedId}
              />
            ))}
          </ul>
        )}
      </div>
      <p className="border-t px-4 py-2 text-xs text-muted-foreground">
        {t('inbox.showing', { shown: visible.length, total: inTab.length })}
      </p>
    </section>
  );
}
