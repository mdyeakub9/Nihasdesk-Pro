/**
 * In-memory stand-in for the Inbox API (dev and tests only; never bundled into production builds).
 * All people and contact details are fictional.
 */
import type { Conversation, ConversationPatch, ConversationSummary, Message } from '../features/inbox/types';
import { minutesAgo } from './time';

let nextId = 1;
const id = (prefix: string) => `${prefix}-${nextId++}`;

function customer(text: string, minutes: number): Message {
  return { id: id('m'), author: 'customer', text, sentAt: minutesAgo(minutes) };
}

function ai(text: string, minutes: number, extra: Partial<Message> = {}): Message {
  return { id: id('m'), author: 'ai', text, sentAt: minutesAgo(minutes), delivery: 'delivered', ...extra };
}

function agent(text: string, minutes: number, extra: Partial<Message> = {}): Message {
  return {
    id: id('m'),
    author: 'agent',
    authorName: 'Md Yeakub',
    text,
    sentAt: minutesAgo(minutes),
    delivery: 'delivered',
    ...extra,
  };
}

function seed(): Conversation[] {
  return [
    {
      id: 'c-giulia',
      contactName: 'Giulia Rossi',
      channel: 'messenger',
      status: 'open',
      responder: 'ai',
      unread: false,
      assignedToMe: true,
      preview: 'Perfect, I would like one. Can you help me order?',
      lastMessageAt: minutesAgo(2),
      contact: {
        name: 'Giulia Rossi',
        phone: '+39 320 456 7890',
        email: 'giulia.rossi@example.com',
        city: 'Milano',
        country: 'Italy',
        language: 'Italian',
        firstSeenAt: minutesAgo(11 * 24 * 60),
        lastActiveAt: minutesAgo(2),
      },
      messages: [
        customer('Ciao! Do you have the Classic Leather Bag in black?', 10),
        ai('', 9, {
          product: {
            name: 'Classic Leather Bag',
            variant: 'Black',
            sku: 'BAG-001',
            price: { amount: 89, currency: 'EUR' },
            inStock: true,
          },
        }),
        ai(
          'Yes, black is available. Delivery to Milano takes 2–3 business days (€4.90), or free on orders over €100.',
          8,
        ),
        customer('Perfect, I would like one. Can you help me order?', 2),
      ],
      labels: ['Interested', 'Leather bags'],
      aiSummary: 'Wants one black Classic Leather Bag. Delivery to Milano. Ready to order.',
      leadStatus: 'hot',
      orderCount: 1,
      handoffCount: 1,
      aiEnabled: true,
    },
    {
      id: 'c-luca',
      contactName: 'Luca Bianchi',
      channel: 'messenger',
      status: 'open',
      responder: 'human',
      unread: false,
      assignedToMe: false,
      preview: 'Thanks, I have placed my order.',
      lastMessageAt: minutesAgo(6),
      contact: {
        name: 'Luca Bianchi',
        city: 'Torino',
        country: 'Italy',
        language: 'Italian',
        firstSeenAt: minutesAgo(3 * 24 * 60),
        lastActiveAt: minutesAgo(6),
      },
      messages: [
        agent('Your order request is approved. It will arrive on Friday, cash on delivery.', 9),
        customer('Thanks, I have placed my order.', 6),
      ],
      labels: [],
      teamNote: 'Prefers delivery after 18:00.',
      leadStatus: 'customer',
      orderCount: 2,
      handoffCount: 0,
      aiEnabled: true,
    },
    {
      id: 'c-sofia',
      contactName: 'Sofia Conti',
      channel: 'instagram',
      status: 'waiting',
      responder: 'human',
      unread: true,
      assignedToMe: true,
      preview: 'Can I change my delivery address?',
      lastMessageAt: minutesAgo(12),
      contact: {
        name: 'Sofia Conti',
        city: 'Sassari',
        country: 'Italy',
        language: 'Italian',
        firstSeenAt: minutesAgo(30 * 24 * 60),
        lastActiveAt: minutesAgo(12),
      },
      messages: [
        customer('Can I change my delivery address?', 13),
        ai('I will pass you to a person from the team who can change it for you.', 12),
      ],
      labels: ['Order change'],
      aiSummary: 'Asks to change the delivery address of an open order. Handed to the team.',
      leadStatus: 'support',
      orderCount: 1,
      handoffCount: 1,
      aiEnabled: true,
    },
    {
      id: 'c-marco',
      contactName: 'Marco Romano',
      channel: 'messenger',
      status: 'open',
      responder: 'ai',
      unread: false,
      assignedToMe: false,
      preview: 'Do you deliver to Milano?',
      lastMessageAt: minutesAgo(18),
      contact: {
        name: 'Marco Romano',
        language: 'Italian',
        firstSeenAt: minutesAgo(18),
        lastActiveAt: minutesAgo(18),
      },
      messages: [customer('Do you deliver to Milano?', 18)],
      labels: [],
      leadStatus: 'new',
      orderCount: 0,
      handoffCount: 0,
      aiEnabled: true,
    },
    {
      id: 'c-elena',
      contactName: 'Elena Ricci',
      channel: 'whatsapp',
      status: 'open',
      responder: 'human',
      unread: true,
      assignedToMe: true,
      preview: 'I would like to book a call.',
      lastMessageAt: minutesAgo(24),
      contact: {
        name: 'Elena Ricci',
        phone: '+39 347 111 2233',
        language: 'Italian',
        firstSeenAt: minutesAgo(2 * 24 * 60),
        lastActiveAt: minutesAgo(24),
      },
      messages: [customer('I would like to book a call.', 24)],
      labels: ['Wholesale'],
      leadStatus: 'hot',
      orderCount: 0,
      handoffCount: 1,
      aiEnabled: false,
    },
    {
      id: 'c-alessandro',
      contactName: 'Alessandro Costa',
      channel: 'instagram',
      status: 'open',
      responder: 'ai',
      unread: false,
      assignedToMe: false,
      preview: 'Could you share the product details?',
      lastMessageAt: minutesAgo(32),
      contact: {
        name: 'Alessandro Costa',
        language: 'English',
        firstSeenAt: minutesAgo(32),
        lastActiveAt: minutesAgo(32),
      },
      messages: [customer('Could you share the product details?', 32)],
      labels: [],
      leadStatus: 'new',
      orderCount: 0,
      handoffCount: 0,
      aiEnabled: true,
    },
    {
      id: 'c-chiara',
      contactName: 'Chiara Esposito',
      channel: 'whatsapp',
      status: 'done',
      responder: 'ai',
      unread: false,
      assignedToMe: false,
      preview: 'Grazie mille!',
      lastMessageAt: minutesAgo(180),
      contact: {
        name: 'Chiara Esposito',
        language: 'Italian',
        firstSeenAt: minutesAgo(5 * 24 * 60),
        lastActiveAt: minutesAgo(180),
      },
      messages: [ai('Your order is on its way.', 185), customer('Grazie mille!', 180)],
      labels: [],
      leadStatus: 'customer',
      orderCount: 1,
      handoffCount: 0,
      aiEnabled: true,
    },
  ];
}

let conversations = seed();

/** Restores the seed data (used between tests). */
export function resetMockInbox() {
  conversations = seed();
}

function summarize({
  id: conversationId,
  contactName,
  channel,
  status,
  responder,
  unread,
  assignedToMe,
  preview,
  lastMessageAt,
}: Conversation): ConversationSummary {
  return {
    id: conversationId,
    contactName,
    channel,
    status,
    responder,
    unread,
    assignedToMe,
    preview,
    lastMessageAt,
  };
}

export function listConversations(): ConversationSummary[] {
  return conversations.map(summarize).sort((a, b) => b.lastMessageAt.localeCompare(a.lastMessageAt));
}

export function getConversation(conversationId: string): Conversation | null {
  const found = conversations.find((c) => c.id === conversationId);
  if (!found) return null;
  // Opening a conversation marks it as read.
  found.unread = false;
  return structuredClone(found);
}

function update(conversationId: string, change: (c: Conversation) => Conversation) {
  conversations = conversations.map((c) => (c.id === conversationId ? change(c) : c));
}

export function updateConversation(conversationId: string, patch: ConversationPatch) {
  update(conversationId, (c) => {
    const next = { ...c, ...patch };
    // Turning AI off hands the conversation to the team.
    if (patch.aiEnabled === false) next.responder = 'human';
    if (patch.responder === 'human' && c.responder === 'ai') next.handoffCount = c.handoffCount + 1;
    return next;
  });
}

export function sendMessage(conversationId: string, text: string, internal: boolean) {
  const message: Message = internal
    ? { id: id('m'), author: 'agent', authorName: 'Md Yeakub', text, sentAt: minutesAgo(0), internal: true }
    : agent(text, 0);
  update(conversationId, (c) => ({
    ...c,
    messages: [...c.messages, message],
    ...(internal
      ? {}
      : {
          preview: text,
          lastMessageAt: message.sentAt,
          // A person replying takes the conversation over from the AI.
          responder: 'human' as const,
          status: c.status === 'waiting' ? ('open' as const) : c.status,
        }),
  }));
}
