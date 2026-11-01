import { create } from 'zustand';

/**
 * Unsent reply text per conversation (PRD 4.4), so switching chats keeps it. Memory only, like
 * other client state: customer data never goes to browser storage, and a reload starts empty.
 */
type DraftState = {
  drafts: Record<string, string>;
  setDraft: (conversationId: string, text: string) => void;
};

export const useDraftStore = create<DraftState>()((set) => ({
  drafts: {},
  setDraft: (conversationId, text) =>
    set((state) => {
      const others = Object.entries(state.drafts).filter(([id]) => id !== conversationId);
      return { drafts: Object.fromEntries(text ? [...others, [conversationId, text]] : others) };
    }),
}));
