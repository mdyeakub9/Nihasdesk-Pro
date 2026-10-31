/** Meta channels (PRD 4.3); also the inbox's channel filter. */
export const channels = ['messenger', 'instagram', 'whatsapp'] as const;
/** `website` is the chat widget (ADR 0016). */
export type Channel = (typeof channels)[number] | 'website';

/** waiting = a customer waits for the team (handoff); done = resolved. */
export type ConversationStatus = 'waiting' | 'open' | 'done';

/** Who answers the customer right now. */
export type Responder = 'ai' | 'human';

/** AI classification per conversation, editable by agents (PRD 4.5). */
export type LeadStatus = 'new' | 'hot' | 'customer' | 'support' | 'spam';

export type ProductCard = {
  name: string;
  variant: string;
  sku: string;
  price: { amount: number; currency: string };
  inStock: boolean;
};

export type Message = {
  id: string;
  author: 'customer' | 'ai' | 'agent';
  /** Team member name for agent messages and notes. */
  authorName?: string;
  text: string;
  sentAt: string;
  /** Internal notes are visible to the team only: never sent to the channel or the AI (PRD 4.4). */
  internal?: boolean;
  /** Notes pinned to the top of the side panel. */
  pinned?: boolean;
  /** Team members @mentioned in a note. */
  mentionedUserIds?: string[];
  /** Catalog product shown to the customer (prices and stock come from the catalog, never the model). */
  product?: ProductCard;
  /** Team replies to a channel (ADR 0017): queued, sent, or given up. */
  delivery?: 'pending' | 'sent' | 'failed' | 'delivered' | 'read';
};

export type ConversationSummary = {
  id: string;
  contactName: string;
  channel: Channel;
  status: ConversationStatus;
  responder: Responder;
  unread: boolean;
  assignedToMe: boolean;
  preview: string;
  lastMessageAt: string;
};

export type ContactDetails = {
  name: string;
  phone?: string;
  email?: string;
  city?: string;
  country?: string;
  language: string;
  firstSeenAt: string;
  lastActiveAt: string;
};

export type Conversation = ConversationSummary & {
  contact: ContactDetails;
  messages: Message[];
  labels: string[];
  /** Team member the conversation is assigned to; the responder chip shows their name. */
  assigneeId: string | null;
  aiSummary?: string;
  leadStatus: LeadStatus;
  orderCount: number;
  handoffCount: number;
  /** AI allowed for this conversation at all (PRD 4.8: AI on/off per conversation). */
  aiEnabled: boolean;
  /** Loads the next older page of messages, when there is one. */
  olderCursor?: string;
};

/** The side panel's team notes: pinned first, then the newest (PRD 4.4). */
export type TeamNotes = { pinned: Message[]; recent: Message[] };

export type ConversationPatch = Partial<Pick<Conversation, 'status' | 'responder' | 'aiEnabled'>>;
