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

/** A catalog product as it was sent (ADR 0024); `amount` in currency units, e.g. 89 for €89.00. */
export type ProductCard = {
  name: string;
  variant: string;
  sku?: string;
  price: { amount: number; currency: string };
};

/** Media a customer sent (PRD 4.4, ADR 0031): kept by Nihasdesk, read through the media endpoint. */
export type AttachmentKind = 'image' | 'audio' | 'video' | 'file' | 'sticker';

/** Why media could not be saved; the team can ask the customer to send it again. */
export type AttachmentFailure =
  'expired' | 'type_not_allowed' | 'too_large' | 'network' | 'source_not_allowed';

export type Attachment = {
  id: string;
  kind: AttachmentKind;
  /** `deleted`: removed after the retention period (SEC-44). */
  status: 'pending' | 'saved' | 'failed' | 'too_large' | 'deleted';
  failureCode?: AttachmentFailure | undefined;
  contentType?: string | undefined;
  sizeBytes?: number | undefined;
  width?: number | undefined;
  height?: number | undefined;
  fileName?: string | undefined;
  /** Set once saved; the API checks the session and the workspace (never a public link). */
  url?: string | undefined;
  thumbnailUrl?: string | undefined;
};

export type Message = {
  id: string;
  author: 'customer' | 'ai' | 'agent';
  /** Team member name for agent messages and notes. */
  authorName?: string;
  /** Who wrote an agent message or note; decides who may delete a note. */
  authorUserId?: string;
  text: string;
  sentAt: string;
  /** Internal notes are visible to the team only: never sent to the channel or the AI (PRD 4.4). */
  internal?: boolean;
  /** Team members @mentioned in a note. */
  mentionedUserIds?: string[];
  /** Resolved notes are dimmed and grouped under "Resolved". */
  resolvedAt?: string;
  resolvedByUserId?: string;
  /** Catalog product shown to the customer (prices and stock come from the catalog, never the model). */
  product?: ProductCard;
  /** Team replies to a channel (ADR 0017): queued, sent, or given up. */
  delivery?: 'pending' | 'sent' | 'failed' | 'delivered' | 'read';
  /** Photos, voice notes, videos, files and stickers, in the order they were sent. */
  attachments?: Attachment[];
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
  leadStatus: LeadStatus;
  handoffCount: number;
  /** AI allowed for this conversation at all (PRD 4.8: AI on/off per conversation). */
  aiEnabled: boolean;
  /** Loads the next older page of messages, when there is one. */
  olderCursor?: string;
};

/** Team notes (PRD 4.4, ADR 0021): only in the side panel's Notes section, newest first. */
export type TeamNotes = { items: Message[] };

export type ConversationPatch = Partial<Pick<Conversation, 'status' | 'responder' | 'aiEnabled'>>;
