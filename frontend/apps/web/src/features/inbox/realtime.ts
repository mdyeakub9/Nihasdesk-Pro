import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { apiUrl, getCsrfToken } from '../../lib/api';
import { useSession } from '../session/useSession';
import { inboxKeys } from './api';
import { useOrderRequestStore } from '../orders/requests';
import { useMentionStore } from './mentions';
import { useTypingStore } from './typing';

/**
 * Live inbox updates (ADR 0016): the hub only says which conversation changed — IDs, never text —
 * and the dashboard reloads it through the API. Mounted once in the app shell while signed in.
 */
export function useInboxRealtime() {
  const queryClient = useQueryClient();
  const { data: session } = useSession();
  const userId = session?.user.id;

  useEffect(() => {
    if (!userId) return;

    let connection: HubConnection | undefined;
    let stopped = false;

    void (async () => {
      // The negotiate request is a POST, so it needs the CSRF header like any change (SEC-03).
      const token = await getCsrfToken();
      if (stopped) return;
      connection = new HubConnectionBuilder()
        .withUrl(apiUrl('/api/v1/hubs/inbox'), { withCredentials: true, headers: { 'X-XSRF-TOKEN': token } })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      connection.on('conversationChanged', (event: { conversationId?: unknown }) => {
        void queryClient.invalidateQueries({ queryKey: inboxKeys.list() });
        if (typeof event?.conversationId === 'string') {
          void queryClient.invalidateQueries({ queryKey: inboxKeys.detail(event.conversationId) });
        }
      });
      // Someone @mentioned this member in a team note (PRD 4.4): IDs only, like every event.
      connection.on('mentioned', (event: { conversationId?: unknown; noteId?: unknown }) => {
        if (typeof event?.conversationId === 'string' && typeof event.noteId === 'string') {
          useMentionStore.getState().received({ conversationId: event.conversationId, noteId: event.noteId });
        }
      });
      // The AI created an order request that waits for a person (ADR 0025): IDs only.
      connection.on('orderRequested', (event: { conversationId?: unknown; orderId?: unknown }) => {
        if (typeof event?.conversationId === 'string' && typeof event.orderId === 'string') {
          useOrderRequestStore
            .getState()
            .received({ conversationId: event.conversationId, orderId: event.orderId });
          void queryClient.invalidateQueries({ queryKey: ['orders'] });
        }
      });
      // A team member is writing in a conversation (PRD 4.4): IDs only.
      connection.on('typing', (event: { conversationId?: unknown; userId?: unknown }) => {
        if (typeof event?.conversationId === 'string' && typeof event.userId === 'string') {
          useTypingStore.getState().received(event.conversationId, event.userId);
        }
      });
      // Missed events while offline: reload everything once the connection is back.
      connection.onreconnected(() => void queryClient.invalidateQueries({ queryKey: inboxKeys.all }));

      try {
        await connection.start();
      } catch {
        // The inbox still works without live updates; automatic reconnect only covers drops after start.
      }
    })();

    return () => {
      stopped = true;
      void connection?.stop();
    };
  }, [queryClient, userId]);
}
