import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiDelete, apiGet, apiPost, apiPut } from '../../lib/api';

/* Saved replies (PRD 4.4, ADR 0022): everyone uses them with `/`; Owner and Admin manage them. */

export type SavedReply = {
  id: string;
  /** Typed after `/`, e.g. `ship`. */
  shortcut: string;
  title: string;
  textEn: string | null;
  textIt: string | null;
  updatedAt: string;
};

export type SavedReplyInput = Pick<SavedReply, 'shortcut' | 'title' | 'textEn' | 'textIt'>;

const path = '/api/v1/saved-replies';
const keys = { all: ['saved-replies'] as const };

export function useSavedReplies() {
  return useQuery({ queryKey: keys.all, queryFn: () => apiGet<SavedReply[]>(path) });
}

export function useSaveSavedReply() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...input }: SavedReplyInput & { id?: string }) =>
      id ? apiPut<SavedReply>(`${path}/${encodeURIComponent(id)}`, input) : apiPost<SavedReply>(path, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}

export function useDeleteSavedReply() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiDelete(`${path}/${encodeURIComponent(id)}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}

/** Saved replies matching what was typed after `/`, by shortcut or title. */
export function matchSavedReplies(replies: SavedReply[], query: string): SavedReply[] {
  const q = query.toLowerCase();
  return replies.filter((r) => r.shortcut.startsWith(q) || r.title.toLowerCase().includes(q)).slice(0, 8);
}

/**
 * The text to put in the composer: the customer's language (Italian or English, the other one if
 * missing) with `{first_name}` and `{shop_name}` filled in. Unknown names become empty, so no
 * placeholder ever reaches a customer.
 */
export function fillSavedReply(
  reply: SavedReply,
  values: { language: string; firstName: string; shopName: string },
): string {
  const italian = values.language.toLowerCase().startsWith('it');
  const text = (italian ? (reply.textIt ?? reply.textEn) : (reply.textEn ?? reply.textIt)) ?? '';
  return text
    .replaceAll('{first_name}', values.firstName)
    .replaceAll('{shop_name}', values.shopName)
    .replace(/ {2,}/g, ' ')
    .replace(/ ([,.!?])/g, '$1');
}
