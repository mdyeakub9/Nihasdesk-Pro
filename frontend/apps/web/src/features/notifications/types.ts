import type { Channel } from '../inbox/types';

/** Events the team is notified about in Phase 1 (PRD 4.10) plus AI answers that need review. */
export const notificationTypes = ['handoff', 'aiReview', 'orderRequest', 'hotLead'] as const;
export type NotificationType = (typeof notificationTypes)[number];

/**
 * The server sends structured data; the dashboard renders the text in the user's language.
 * A notification stays open until someone resolves it.
 */
export type TeamNotification = {
  id: string;
  type: NotificationType;
  contactName: string;
  channel: Channel;
  conversationId: string;
  assignedToMe: boolean;
  createdAt: string;
  resolvedAt?: string;
  /** Short facts for the description, e.g. the items of an order request. */
  detail?: string;
};
