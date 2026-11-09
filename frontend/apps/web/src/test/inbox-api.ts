/**
 * A small, stateful fake of the Inbox API in its real JSON shapes (ADR 0016/0017), on top of
 * fakeApi. All people and contact details are fictional.
 */
import { fakeApi, json, problem } from './fake-api';

type Summary = {
  id: string;
  contactId: string;
  contactName: string | null;
  channel: 'messenger' | 'instagram' | 'whatsapp' | 'website';
  status: 'waiting' | 'open' | 'done';
  responder: 'ai' | 'human';
  aiEnabled: boolean;
  leadStatus: 'new' | 'hot' | 'customer' | 'support' | 'spam';
  assigneeId: string | null;
  unread: boolean;
  preview: string;
  lastMessageAt: string;
};

type ApiMessage = {
  id: string;
  author: 'customer' | 'ai' | 'agent';
  authorUserId: string | null;
  authorName: string | null;
  text: string;
  internal: boolean;
  sentAt: string;
  delivery: 'none' | 'pending' | 'sent' | 'failed';
  mentionedUserIds?: string[];
  resolvedAt?: string | null;
  resolvedByUserId?: string | null;
  attachments?: FakeAttachment[];
};

/** A message's media as the API sends it (ADR 0031); `url` is set once saved. */
export type FakeAttachment = {
  id: string;
  kind: 'image' | 'audio' | 'video' | 'file' | 'sticker';
  status: 'pending' | 'saved' | 'failed' | 'too_large';
  failureCode: string | null;
  contentType: string | null;
  sizeBytes: number | null;
  width: number | null;
  height: number | null;
  fileName: string | null;
  url: string | null;
  thumbnailUrl: string | null;
};

/** Media in a given state; saved ones get the endpoint's URLs (thumbnails for photos and stickers). */
export function attachment(
  id: string,
  kind: FakeAttachment['kind'],
  status: FakeAttachment['status'] = 'saved',
  extra: Partial<FakeAttachment> = {},
): FakeAttachment {
  const saved = status === 'saved';
  const picture = kind === 'image' || kind === 'sticker';
  return {
    id,
    kind,
    status,
    failureCode: status === 'too_large' ? 'too_large' : null,
    contentType: null,
    sizeBytes: null,
    width: null,
    height: null,
    fileName: null,
    url: saved ? `/api/v1/media/${id}` : null,
    thumbnailUrl: saved && picture ? `/api/v1/media/${id}?variant=thumbnail` : null,
    ...extra,
  };
}

export type FakeConversation = Summary & {
  contact: { phone: string | null; email: string | null; language: string | null };
  messages: ApiMessage[];
};

export const teamMembers = [
  {
    userId: 'u-1',
    name: 'Md Yeakub',
    email: 'owner@example.com',
    role: 'owner',
    joinedAt: '2026-10-01T09:00:00Z',
  },
  {
    userId: 'u-2',
    name: 'Luca Neri',
    email: 'luca@example.com',
    role: 'member',
    joinedAt: '2026-10-02T09:00:00Z',
  },
];

/** Saved replies for the `/` picker (ADR 0022). */
export const savedReplies = [
  {
    id: 'r-ship',
    shortcut: 'ship',
    title: 'Shipping times',
    textEn: 'Hi {first_name}, {shop_name} ships in 2 days.',
    textIt: 'Ciao {first_name}, {shop_name} spedisce in 2 giorni.',
    updatedAt: '2026-10-30T09:00:00Z',
  },
  {
    id: 'r-size',
    shortcut: 'size',
    title: 'Size guide',
    textEn: 'Our sizes run small.',
    textIt: null,
    updatedAt: '2026-10-30T09:00:00Z',
  },
];

/** The AI assistant's default answer in tests (ADR 0023). */
export const defaultSuggestion = {
  summary: 'Giulia asks if the black bag is in stock.',
  reply: 'Sì Giulia, la borsa nera è disponibile a €89.00!',
  sources: [
    { kind: 'catalog', label: null },
    { kind: 'policy', label: 'returns' },
  ],
  blocked: null,
  failed: null,
};

const minutesAgo = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

let nextId = 1;
const customer = (text: string, minutes: number): ApiMessage => ({
  id: `m-${nextId++}`,
  author: 'customer',
  authorUserId: null,
  authorName: null,
  text,
  internal: false,
  sentAt: minutesAgo(minutes),
  delivery: 'none',
});

function conversation(
  id: string,
  name: string | null,
  channel: Summary['channel'],
  minutes: number,
  text: string,
  extra: Partial<FakeConversation> = {},
): FakeConversation {
  const first = customer(text, minutes);
  const messages = [first];
  return {
    id,
    contactId: `k-${id}`,
    contactName: name,
    channel,
    status: 'open',
    responder: 'ai',
    aiEnabled: true,
    leadStatus: 'new',
    assigneeId: null,
    unread: false,
    preview: text,
    lastMessageAt: first.sentAt,
    contact: { phone: null, email: null, language: 'it' },
    messages,
    ...extra,
  };
}

/** Six open-ish conversations, newest first, plus one done. */
export function seedConversations(): FakeConversation[] {
  return [
    conversation('c-giulia', 'Giulia Rossi', 'messenger', 2, 'Ciao! Avete la borsa in nero?', {
      leadStatus: 'hot',
      assigneeId: 'u-1',
      contact: { phone: '+39 333 000 1111', email: 'giulia.rossi@example.com', language: 'it' },
    }),
    conversation('c-luca', 'Luca Bianchi', 'instagram', 6, 'Thanks, I have placed my order.', {
      unread: true,
    }),
    conversation('c-sofia', 'Sofia Conti', 'messenger', 12, 'Can I change my delivery address?', {
      status: 'waiting',
      responder: 'human',
      unread: true,
    }),
    conversation('c-marco', 'Marco Romano', 'messenger', 18, 'Do you deliver to Milano?'),
    conversation('c-elena', 'Elena Ricci', 'whatsapp', 25, 'I would like to book a call.'),
    conversation('c-unknown', null, 'messenger', 40, 'hello'),
    conversation('c-anna', 'Anna Verdi', 'messenger', 300, 'All good, thank you!', { status: 'done' }),
  ];
}

function summary(conversation: FakeConversation): Summary {
  const copy: Partial<FakeConversation> = { ...conversation };
  delete copy.contact;
  delete copy.messages;
  return copy as Summary;
}

type Options = Parameters<typeof fakeApi>[0] & {
  conversations?: FakeConversation[];
  /** How the server answers a team reply; queued for delivery by default. */
  reply?: (conversation: FakeConversation, text: string) => Response | undefined;
  /** Messages per page before "older"; default 50. */
  pageSize?: number;
  /** The stored summary of a long conversation (ADR 0026 §4); none by default. */
  memorySummary?: string;
  /** How the AI assistant answers `POST /assist` (ADR 0023); a fixed suggestion by default. */
  assist?: (conversation: FakeConversation, body: { style?: string; language?: string }) => Response;
};

/** Installs the fake; `state.conversations` can be changed by tests (e.g. a new message arrives). */
export function fakeInboxApi({
  conversations = seedConversations(),
  reply,
  assist,
  memorySummary,
  pageSize = 50,
  ...options
}: Options = {}) {
  const state = { conversations };
  const find = (id: string) => state.conversations.find((c) => c.id === id);

  const routes: Record<string, (body: unknown, url: URL) => Response> = {
    // Names for the responder chip and @mentions.
    'GET /api/v1/team/members': () => json(teamMembers),
    // The customer panel lists a chat's orders (ADR 0025); none by default.
    'GET /api/v1/orders': () => json({ items: [], nextCursor: null }),
    'GET /api/v1/saved-replies': () => json(savedReplies),
    'GET /api/v1/conversations': () =>
      json({
        items: [...state.conversations]
          .sort((a, b) => b.lastMessageAt.localeCompare(a.lastMessageAt))
          .map(summary),
        nextCursor: null,
      }),
  };

  // fakeApi matches exact paths, so every route is listed per conversation (unknown IDs answer 404).
  for (const id of [
    'c-giulia',
    'c-luca',
    'c-sofia',
    'c-marco',
    'c-elena',
    'c-unknown',
    'c-anna',
    'c-new',
    'c-old',
  ]) {
    const base = `/api/v1/conversations/${id}`;
    const withConversation =
      (handler: (c: FakeConversation, body: never, url: URL) => Response) =>
      (body: unknown, url: URL): Response => {
        const c = find(id);
        return c ? handler(c, body as never, url) : problem(404, 'conversation.not_found');
      };

    routes[`GET ${base}`] = withConversation((c) =>
      json({
        conversation: summary(c),
        contact: { id: c.contactId, name: c.contactName, createdAt: minutesAgo(600), ...c.contact },
      }),
    );
    routes[`GET ${base}/messages`] = withConversation((c, _body, url) => {
      // Two pages are enough for tests: the newest `pageSize`, then (with `before`) everything older.
      if (url.searchParams.get('before')) {
        return json({ items: c.messages.slice(0, -pageSize), before: null });
      }
      const older = c.messages.length > pageSize;
      return json({ items: c.messages.slice(-pageSize), before: older ? 'older-cursor' : null });
    });
    routes[`PATCH ${base}`] = withConversation((c, patch: Partial<Summary>) => {
      Object.assign(c, patch);
      return json({ conversation: summary(c), contact: null });
    });
    routes[`POST ${base}/typing`] = withConversation(() => new Response(null, { status: 204 }));
    routes[`POST ${base}/assist`] = withConversation((c, body: { style?: string; language?: string }) =>
      assist ? assist(c, body) : json(defaultSuggestion),
    );
    routes[`GET ${base}/memory`] = withConversation(() =>
      json({ summary: memorySummary ?? null, updatedAt: memorySummary ? '2026-11-06T10:00:00Z' : null }),
    );
    routes[`POST ${base}/read`] = withConversation((c) => {
      c.unread = false;
      return new Response(null, { status: 204 });
    });
    routes[`GET ${base}/notes`] = withConversation((c) => {
      // Team notes only; the AI's handoff notes stay in the conversation (ADR 0021).
      return json({ items: c.messages.filter((m) => m.internal && m.author === 'agent').reverse() });
    });
    routes[`POST ${base}/notes`] = withConversation(
      (c, body: { text: string; mentionedUserIds?: string[] }) => {
        const note = addTeamMessage(c, body.text, true);
        note.mentionedUserIds = body.mentionedUserIds ?? [];
        return json(note, 201);
      },
    );
    // Resolve and delete routes for the notes a test seeded (fakeApi matches exact paths).
    for (const note of find(id)?.messages.filter((m) => m.internal && m.author === 'agent') ?? []) {
      const change = (apply: (m: ApiMessage) => void) =>
        withConversation((c) => {
          const found = c.messages.find((m) => m.id === note.id);
          if (!found) return problem(404, 'note.not_found');
          apply(found);
          return json(found);
        });
      routes[`DELETE ${base}/notes/${note.id}`] = withConversation((c) => {
        c.messages = c.messages.filter((m) => m.id !== note.id);
        return new Response(null, { status: 204 });
      });
      routes[`PUT ${base}/notes/${note.id}/resolve`] = change((m) => {
        m.resolvedAt = new Date().toISOString();
        m.resolvedByUserId = 'u-1';
      });
      routes[`DELETE ${base}/notes/${note.id}/resolve`] = change((m) => {
        m.resolvedAt = null;
        m.resolvedByUserId = null;
      });
    }
    routes[`POST ${base}/messages`] = withConversation((c, body: { text: string }) => {
      const custom = reply?.(c, body.text);
      if (custom) return custom;
      c.responder = 'human';
      if (c.status === 'waiting') c.status = 'open';
      c.preview = body.text;
      return json(addTeamMessage(c, body.text, false), 201);
    });
  }

  return { fetchMock: fakeApi({ ...options, routes: { ...routes, ...options.routes } }), state };
}

function addTeamMessage(c: FakeConversation, text: string, internal: boolean): ApiMessage {
  const message: ApiMessage = {
    id: `m-${nextId++}`,
    author: 'agent',
    authorUserId: 'u-1',
    authorName: 'Md Yeakub',
    text,
    internal,
    sentAt: new Date().toISOString(),
    delivery: internal || c.channel === 'website' ? 'none' : 'pending',
  };
  c.messages.push(message);
  return message;
}

export { customer as customerMessage };
