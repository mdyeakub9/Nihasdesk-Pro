import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { TeamNotification } from './types';

/* Same pattern as features/inbox/api.ts: mock in dev and tests, empty in production until the API exists. */

const loadMock = () => import('../../mocks/notifications');

async function listNotifications(): Promise<TeamNotification[]> {
  if (import.meta.env.DEV) {
    return (await loadMock()).listNotifications();
  }
  return [];
}

async function resolveNotifications(ids: string[]): Promise<void> {
  if (import.meta.env.DEV) {
    (await loadMock()).resolveNotifications(ids);
    return;
  }
  throw new Error('Notifications API is not available yet.');
}

export const notificationKeys = {
  all: ['notifications'] as const,
};

export function useNotifications() {
  return useQuery({ queryKey: notificationKeys.all, queryFn: listNotifications });
}

export function useResolveNotifications() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: resolveNotifications,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: notificationKeys.all }),
  });
}

export function isOpen(notification: TeamNotification) {
  return !notification.resolvedAt;
}
