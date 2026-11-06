import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ApiError, apiDelete, apiGet, apiPatch, apiPost, apiPut } from '../../lib/api';
import { useSession } from '../session/useSession';
import type {
  Channel,
  Conversation,
  ConversationPatch,
  ConversationSummary,
  LeadStatus,
  Message,
  TeamNotes,
} from './types';

/*
 * Data access for the inbox (Inbox API, ADR 0016). The API's shapes are mapped to the types the
 * components already use, so components keep using the hooks below.
 */

type ApiConversationSummary = {
  id: string;
  contactId: string;
  contactName: string | null;
  channel: Channel;
  status: ConversationSummary['status'];
  responder: ConversationSummary['responder'];
  aiEnabled: boolean;
  leadStatus: LeadStatus;
  assigneeId: string | null;
  unread: boolean;
  preview: string;
  lastMessageAt: string;
};

type ApiContact = {
  id: string;
  name: string | null;
  phone: string | null;
  email: string | null;
  language: string | null;
  createdAt: string;
};

type ApiMessage = {
  id: string;
  author: Message['author'];
  authorUserId: string | null;
  authorName: string | null;
  text: string;
  internal: boolean;
  sentAt: string;
  delivery: 'none' | 'pending' | 'sent' | 'failed';
  mentionedUserIds: string[];
  resolvedAt: string | null;
  resolvedByUserId: string | null;
  product: {
    productId: string;
    variantId: string;
    title: string;
    variant: string;
    sku: string | null;
    price: number;
    currency: string;
  } | null;
};

type ConversationPage = { items: ApiConversationSummary[]; nextCursor: string | null };
type MessagePage = { items: ApiMessage[]; before: string | null };

/** The inbox list shows the newest conversations; filters and counts work on these in the browser. */
export const listLimit = 100;

const path = (id: string) => `/api/v1/conversations/${encodeURIComponent(id)}`;

/** Fields the list needs per user; filled in by the hooks (the API sends IDs, not names for the viewer). */
type Raw<T> = Omit<T, 'assignedToMe'> & { assigneeId: string | null };

function toSummary(c: ApiConversationSummary): Raw<ConversationSummary> {
  return {
    id: c.id,
    contactName: c.contactName ?? '',
    channel: c.channel,
    status: c.status,
    responder: c.responder,
    unread: c.unread,
    assigneeId: c.assigneeId,
    preview: c.preview,
    lastMessageAt: c.lastMessageAt,
  };
}

function toMessage(m: ApiMessage): Message {
  return {
    id: m.id,
    author: m.author,
    text: m.text,
    sentAt: m.sentAt,
    ...(m.authorName ? { authorName: m.authorName } : {}),
    ...(m.authorUserId ? { authorUserId: m.authorUserId } : {}),
    ...(m.internal ? { internal: true } : {}),
    ...(m.mentionedUserIds?.length ? { mentionedUserIds: m.mentionedUserIds } : {}),
    ...(m.resolvedAt ? { resolvedAt: m.resolvedAt } : {}),
    ...(m.resolvedByUserId ? { resolvedByUserId: m.resolvedByUserId } : {}),
    ...(m.product
      ? {
          product: {
            name: m.product.title,
            variant: m.product.variant,
            ...(m.product.sku ? { sku: m.product.sku } : {}),
            // Cents from the API, units for formatting.
            price: { amount: m.product.price / 100, currency: m.product.currency },
          },
        }
      : {}),
    ...(m.delivery !== 'none' ? { delivery: m.delivery } : {}),
  };
}

async function listConversations(): Promise<Raw<ConversationSummary>[]> {
  const page = await apiGet<ConversationPage>(`/api/v1/conversations?limit=${listLimit}`);
  return page.items.map(toSummary);
}

async function getConversation(id: string): Promise<Raw<Conversation> | null> {
  try {
    const [detail, messages] = await Promise.all([
      apiGet<{ conversation: ApiConversationSummary; contact: ApiContact | null }>(path(id)),
      apiGet<MessagePage>(`${path(id)}/messages?limit=50`),
    ]);
    const c = detail.conversation;
    return {
      ...toSummary(c),
      aiEnabled: c.aiEnabled,
      leadStatus: c.leadStatus,
      contact: {
        name: detail.contact?.name ?? '',
        ...(detail.contact?.phone ? { phone: detail.contact.phone } : {}),
        ...(detail.contact?.email ? { email: detail.contact.email } : {}),
        language: detail.contact?.language ?? '',
        firstSeenAt: detail.contact?.createdAt ?? c.lastMessageAt,
        lastActiveAt: c.lastMessageAt,
      },
      messages: messages.items.map(toMessage),
      ...(messages.before ? { olderCursor: messages.before } : {}),
      labels: [],
      handoffCount: 0,
    };
  } catch (error) {
    // Unknown IDs and other workspaces' IDs both answer 404 (SEC-06).
    if (error instanceof ApiError && error.status === 404) return null;
    throw error;
  }
}

function withoutCursor<T extends { olderCursor?: string }>(value: T): Omit<T, 'olderCursor'> {
  const copy = { ...value };
  delete copy.olderCursor;
  return copy;
}

export const inboxKeys = {
  all: ['inbox'] as const,
  list: () => [...inboxKeys.all, 'list'] as const,
  detail: (id: string) => [...inboxKeys.all, 'detail', id] as const,
  notes: (id: string) => [...inboxKeys.all, 'notes', id] as const,
};

/** Display name for people without one yet (the channel didn't share it). */
function useViewer() {
  const { t } = useTranslation();
  const { data: session } = useSession();
  return {
    userId: session?.user.id,
    name: (name: string) => name || t('inbox.unknownCustomer'),
  };
}

export function useConversations() {
  const viewer = useViewer();
  return useQuery({
    queryKey: inboxKeys.list(),
    queryFn: listConversations,
    select: (list): ConversationSummary[] =>
      list.map(({ assigneeId, ...c }) => ({
        ...c,
        contactName: viewer.name(c.contactName),
        assignedToMe: !!assigneeId && assigneeId === viewer.userId,
      })),
  });
}

export function useConversation(id: string) {
  const queryClient = useQueryClient();
  const viewer = useViewer();
  return useQuery({
    queryKey: inboxKeys.detail(id),
    queryFn: async () => {
      const conversation = await getConversation(id);
      if (conversation?.unread) {
        // Opening marks it read for this member; the list's unread state follows.
        await apiPost(`${path(id)}/read`);
        void queryClient.invalidateQueries({ queryKey: inboxKeys.list() });
      }
      return conversation;
    },
    select: (c): Conversation | null =>
      c && {
        ...c,
        unread: false,
        contactName: viewer.name(c.contactName),
        contact: { ...c.contact, name: viewer.name(c.contact.name) },
        assignedToMe: !!c.assigneeId && c.assigneeId === viewer.userId,
      },
  });
}

/** Loads the next older page of messages into the open conversation. */
export function useOlderMessages(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (cursor: string) =>
      apiGet<MessagePage>(`${path(id)}/messages?limit=50&before=${encodeURIComponent(cursor)}`),
    onSuccess: (page) =>
      queryClient.setQueryData<Raw<Conversation> | null>(
        inboxKeys.detail(id),
        (current) =>
          current && {
            ...withoutCursor(current),
            messages: [...page.items.map(toMessage), ...current.messages],
            ...(page.before ? { olderCursor: page.before } : {}),
          },
      ),
  });
}

export function useUpdateConversation(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (patch: ConversationPatch) => apiPatch(path(id), patch),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: inboxKeys.all }),
  });
}

/** A reply to the customer. Team notes are added from the side panel (useAddNote). */
export function useSendMessage(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (text: string) => apiPost(`${path(id)}/messages`, { text }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: inboxKeys.all }),
  });
}

/** Team notes (PRD 4.4, ADR 0021) for the side panel's Notes section, newest first. */
export function useNotes(id: string) {
  return useQuery({
    queryKey: inboxKeys.notes(id),
    queryFn: async (): Promise<TeamNotes> => {
      const notes = await apiGet<{ items: ApiMessage[] }>(`${path(id)}/notes`);
      return { items: notes.items.map(toMessage) };
    },
  });
}

export function useAddNote(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (note: { text: string; mentionedUserIds: string[] }) => apiPost(`${path(id)}/notes`, note),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: inboxKeys.all }),
  });
}

/** The author, an Owner or an Admin deletes a note for good (the server checks). */
export function useDeleteNote(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (noteId: string) => apiDelete(`${path(id)}/notes/${encodeURIComponent(noteId)}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: inboxKeys.all }),
  });
}

export function useResolveNote(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ noteId, resolved }: { noteId: string; resolved: boolean }) => {
      const resolve = `${path(id)}/notes/${encodeURIComponent(noteId)}/resolve`;
      return resolved ? apiPut(resolve, {}) : apiDelete(resolve);
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: inboxKeys.all }),
  });
}

/** What the AI assistant knew the reply from (ADR 0023). */
export type AssistSource = {
  kind: 'catalog' | 'shipping' | 'policy' | 'answer' | 'document';
  label: string | null;
};

/** A draft for the team: never sent by itself. `reply` is null when it was held back (`blocked`). */
export type AssistResult = {
  summary: string | null;
  reply: string | null;
  sources: AssistSource[];
  blocked: string | null;
  failed: string | null;
};

export type AssistStyle = 'default' | 'shorter' | 'friendlier';

/** Asks the AI assistant for a summary and a suggested reply (metered like other AI runs). */
export function useAssist(id: string) {
  const { i18n } = useTranslation();
  return useMutation({
    mutationFn: (style: AssistStyle) =>
      apiPost<AssistResult>(`${path(id)}/assist`, {
        style,
        language: i18n.resolvedLanguage === 'it' ? 'it' : 'en',
      }),
  });
}

/** Catalog variants to send as product cards (ADR 0024); names and prices come from the server. */
export function useSendProducts(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (items: { productId: string; variantId: string }[]) =>
      apiPost(`${path(id)}/products`, { items }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: inboxKeys.all }),
  });
}
