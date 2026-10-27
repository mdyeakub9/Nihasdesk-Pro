import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { Conversation, ConversationPatch, ConversationSummary } from './types';

/*
 * Data access for the inbox. Until the Inbox API exists, dev and test builds read an in-memory mock;
 * production builds get no data (the mock is tree-shaken out) and show empty states.
 * When the API lands, only the functions below change — components keep using the hooks.
 */

const loadMock = () => import('../../mocks/inbox');

async function listConversations(): Promise<ConversationSummary[]> {
  if (import.meta.env.DEV) {
    return (await loadMock()).listConversations();
  }
  return [];
}

async function getConversation(id: string): Promise<Conversation | null> {
  if (import.meta.env.DEV) {
    return (await loadMock()).getConversation(id);
  }
  return null;
}

async function patchConversation(id: string, patch: ConversationPatch): Promise<void> {
  if (import.meta.env.DEV) {
    (await loadMock()).updateConversation(id, patch);
    return;
  }
  throw new Error('Inbox API is not available yet.');
}

async function postMessage(id: string, text: string, internal: boolean): Promise<void> {
  if (import.meta.env.DEV) {
    (await loadMock()).sendMessage(id, text, internal);
    return;
  }
  throw new Error('Inbox API is not available yet.');
}

export const inboxKeys = {
  all: ['inbox'] as const,
  list: () => [...inboxKeys.all, 'list'] as const,
  detail: (id: string) => [...inboxKeys.all, 'detail', id] as const,
};

export function useConversations() {
  return useQuery({ queryKey: inboxKeys.list(), queryFn: listConversations });
}

export function useConversation(id: string) {
  const queryClient = useQueryClient();
  return useQuery({
    queryKey: inboxKeys.detail(id),
    queryFn: async () => {
      const conversation = await getConversation(id);
      // Opening marks the conversation read, so the list's unread state changes too.
      void queryClient.invalidateQueries({ queryKey: inboxKeys.list() });
      return conversation;
    },
  });
}

export function useUpdateConversation(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (patch: ConversationPatch) => patchConversation(id, patch),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: inboxKeys.all }),
  });
}

export function useSendMessage(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ text, internal }: { text: string; internal: boolean }) => postMessage(id, text, internal),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: inboxKeys.all }),
  });
}
