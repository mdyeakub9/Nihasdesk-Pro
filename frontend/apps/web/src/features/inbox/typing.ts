import { useEffect, useState } from 'react';
import { create } from 'zustand';
import { apiPost } from '../../lib/api';

/** How long "X is typing…" stays after the last signal, and how often we send ours. */
export const typingShownMs = 5000;
const typingSendEveryMs = 3000;

/**
 * "X is typing…" (PRD 4.4): the hub says which member writes in which conversation (IDs only,
 * never the text); the signal fades after a few seconds without a new one.
 */
type TypingState = {
  /** conversationId → userId → time the signal arrived. */
  typing: Record<string, Record<string, number>>;
  received: (conversationId: string, userId: string) => void;
};

export const useTypingStore = create<TypingState>()((set) => ({
  typing: {},
  received: (conversationId, userId) =>
    set((state) => ({
      typing: {
        ...state.typing,
        [conversationId]: { ...state.typing[conversationId], [userId]: Date.now() },
      },
    })),
}));

/** Other members writing in this conversation right now. */
export function useTypingMembers(conversationId: string, myUserId: string | undefined): string[] {
  const signals = useTypingStore((state) => state.typing[conversationId]);
  const [now, setNow] = useState(() => Date.now());
  const active = Object.entries(signals ?? {})
    .filter(([userId, at]) => userId !== myUserId && now - at < typingShownMs)
    .map(([userId]) => userId);

  // A new signal is newer than `now`, so it shows at once; ticking makes old ones fade on their own.
  useEffect(() => {
    if (!signals) return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [signals]);

  return active;
}

/** Tells the other members this one is writing; at most every few seconds, errors ignored. */
export function useSendTyping(conversationId: string) {
  const [last, setLast] = useState(0);
  return () => {
    const now = Date.now();
    if (now - last < typingSendEveryMs) return;
    setLast(now);
    apiPost(`/api/v1/conversations/${encodeURIComponent(conversationId)}/typing`).catch(() => undefined);
  };
}
