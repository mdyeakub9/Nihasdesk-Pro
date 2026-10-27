/** In-memory stand-in for the notifications API (dev and tests only). People are fictional. */
import type { TeamNotification } from '../features/notifications/types';
import { minutesAgo } from './time';

function seed(): TeamNotification[] {
  return [
    {
      id: 'n-1',
      type: 'handoff',
      contactName: 'Sofia Conti',
      channel: 'instagram',
      conversationId: 'c-sofia',
      assignedToMe: true,
      createdAt: minutesAgo(2),
    },
    {
      id: 'n-2',
      type: 'aiReview',
      contactName: 'Marco Romano',
      channel: 'messenger',
      conversationId: 'c-marco',
      assignedToMe: false,
      createdAt: minutesAgo(8),
      detail: 'return policy',
    },
    {
      id: 'n-3',
      type: 'orderRequest',
      contactName: 'Giulia Rossi',
      channel: 'messenger',
      conversationId: 'c-giulia',
      assignedToMe: false,
      createdAt: minutesAgo(18),
      detail: '1 × Classic Leather Bag (Black) · €93.90',
    },
    {
      id: 'n-4',
      type: 'hotLead',
      contactName: 'Elena Ricci',
      channel: 'whatsapp',
      conversationId: 'c-elena',
      assignedToMe: true,
      createdAt: minutesAgo(32),
    },
    {
      id: 'n-5',
      type: 'handoff',
      contactName: 'Luca Bianchi',
      channel: 'messenger',
      conversationId: 'c-luca',
      assignedToMe: false,
      createdAt: minutesAgo(140),
      resolvedAt: minutesAgo(120),
    },
  ];
}

let notifications = seed();

export function resetMockNotifications() {
  notifications = seed();
}

export function listNotifications(): TeamNotification[] {
  return structuredClone(notifications);
}

export function resolveNotifications(ids: string[]) {
  const resolvedAt = new Date().toISOString();
  notifications = notifications.map((n) => (ids.includes(n.id) && !n.resolvedAt ? { ...n, resolvedAt } : n));
}
