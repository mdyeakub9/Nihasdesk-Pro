import type { Channel, ConversationSummary } from './types';

export const inboxTabs = ['waiting', 'unread', 'open', 'done'] as const;
export type InboxTab = (typeof inboxTabs)[number];

export type InboxFilters = {
  tab: InboxTab;
  channel: Channel | 'all';
  assignee: 'everyone' | 'mine';
  query: string;
};

export const defaultFilters: InboxFilters = { tab: 'open', channel: 'all', assignee: 'everyone', query: '' };

function inTab(conversation: ConversationSummary, tab: InboxTab) {
  switch (tab) {
    case 'waiting':
      return conversation.status === 'waiting';
    case 'unread':
      return conversation.unread && conversation.status !== 'done';
    case 'open':
      return conversation.status !== 'done';
    case 'done':
      return conversation.status === 'done';
  }
}

export function filterConversations(list: ConversationSummary[], filters: InboxFilters) {
  const query = filters.query.trim().toLocaleLowerCase();
  return list.filter(
    (c) =>
      inTab(c, filters.tab) &&
      (filters.channel === 'all' || c.channel === filters.channel) &&
      (filters.assignee === 'everyone' || c.assignedToMe) &&
      (!query ||
        c.contactName.toLocaleLowerCase().includes(query) ||
        c.preview.toLocaleLowerCase().includes(query)),
  );
}

export function countByTab(list: ConversationSummary[]): Record<InboxTab, number> {
  return {
    waiting: list.filter((c) => inTab(c, 'waiting')).length,
    unread: list.filter((c) => inTab(c, 'unread')).length,
    open: list.filter((c) => inTab(c, 'open')).length,
    done: list.filter((c) => inTab(c, 'done')).length,
  };
}
