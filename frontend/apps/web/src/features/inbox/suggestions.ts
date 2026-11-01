import { create } from 'zustand';

/**
 * The AI assistant's text inserted into a conversation's composer (ADR 0023), so that after the
 * team sends an edited version the dashboard can offer "Save as knowledge". Memory only.
 */
type Offer = { conversationId: string; question: string; answer: string };

type SuggestionState = {
  inserted: Record<string, string>;
  offer: Offer | null;
  setInserted: (conversationId: string, text: string | null) => void;
  setOffer: (offer: Offer | null) => void;
};

export const useSuggestionStore = create<SuggestionState>()((set) => ({
  inserted: {},
  offer: null,
  setInserted: (conversationId, text) =>
    set((state) => {
      const others = Object.entries(state.inserted).filter(([id]) => id !== conversationId);
      return { inserted: Object.fromEntries(text ? [...others, [conversationId, text]] : others) };
    }),
  setOffer: (offer) => set({ offer }),
}));
