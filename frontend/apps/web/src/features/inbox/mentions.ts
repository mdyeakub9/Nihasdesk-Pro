import { create } from 'zustand';

/**
 * @mentions this member received while the dashboard is open (PRD 4.4). The hub sends IDs only;
 * the alert links to the conversation. Email and push come with the Notifications API.
 */
type Mention = { conversationId: string; noteId: string };

type MentionState = {
  latest: Mention | null;
  received: (mention: Mention) => void;
  dismiss: () => void;
};

export const useMentionStore = create<MentionState>()((set) => ({
  latest: null,
  received: (mention) => set({ latest: mention }),
  dismiss: () => set({ latest: null }),
}));
