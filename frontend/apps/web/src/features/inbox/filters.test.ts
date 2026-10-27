import { describe, expect, it } from 'vitest';
import { countByTab, defaultFilters, filterConversations } from './filters';
import type { ConversationSummary } from './types';

const base: ConversationSummary = {
  id: 'x',
  contactName: 'Giulia Rossi',
  channel: 'messenger',
  status: 'open',
  responder: 'ai',
  unread: false,
  assignedToMe: false,
  preview: 'Is this available in black?',
  lastMessageAt: '2026-10-26T14:28:00Z',
};

const list: ConversationSummary[] = [
  base,
  {
    ...base,
    id: 'w',
    contactName: 'Sofia Conti',
    preview: 'Can I change my address?',
    status: 'waiting',
    unread: true,
    assignedToMe: true,
    channel: 'instagram',
  },
  { ...base, id: 'd', contactName: 'Chiara Esposito', status: 'done', unread: true, channel: 'whatsapp' },
];

const ids = (result: ConversationSummary[]) => result.map((c) => c.id);

describe('filterConversations', () => {
  it('open includes waiting but not done', () => {
    expect(ids(filterConversations(list, defaultFilters))).toEqual(['x', 'w']);
  });

  it('unread ignores done conversations', () => {
    expect(ids(filterConversations(list, { ...defaultFilters, tab: 'unread' }))).toEqual(['w']);
  });

  it('combines channel, assignee and search', () => {
    expect(ids(filterConversations(list, { ...defaultFilters, channel: 'instagram' }))).toEqual(['w']);
    expect(ids(filterConversations(list, { ...defaultFilters, assignee: 'mine' }))).toEqual(['w']);
    expect(ids(filterConversations(list, { ...defaultFilters, query: 'BLACK' }))).toEqual(['x']);
  });

  it('counts every tab', () => {
    expect(countByTab(list)).toEqual({ waiting: 1, unread: 1, open: 2, done: 1 });
  });
});
