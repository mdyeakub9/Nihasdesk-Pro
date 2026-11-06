import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo, json } from '../../test/fake-api';
import { fakeInboxApi, seedConversations } from '../../test/inbox-api';
import { renderApp } from '../../test/render-app';
import type { RatedReply, Trace } from './api';

const trace = (over: Partial<Trace> = {}): Trace => ({
  outcome: 'replied',
  detail: null,
  model: 'gpt-5-mini',
  instructionVersion: 'agent-2026-11-04+1a2b3c4d',
  inputTokens: 1200,
  outputTokens: 80,
  durationMs: 2400,
  sources: [{ kind: 'policy', label: 'returns' }],
  toolNames: ['search_products'],
  tools: [
    {
      name: 'search_products',
      arguments: '{"query":"borsa nera"}',
      result: '{"products":[{"title":"Borsa nera","price":"89.00 EUR"}]}',
    },
  ],
  check: 'passed',
  hasDetails: true,
  createdAt: '2026-11-04T10:00:00Z',
  myRating: null,
  ...over,
});

const aiMessage = (id: string, text: string) => ({
  id,
  author: 'ai' as const,
  authorUserId: null,
  authorName: null,
  text,
  internal: false,
  sentAt: new Date().toISOString(),
  delivery: 'sent' as const,
});

/** Giulia's chat with the AI's disclosure and its answer; only the answer has a trace. */
function chatWithAiAnswer(details: Trace = trace()) {
  const conversations = seedConversations();
  conversations
    .find((c) => c.id === 'c-giulia')
    ?.messages.push(
      aiMessage('m-disclosure', 'Ciao! Sono l’assistente virtuale (IA) di Almanaam 🙂'),
      aiMessage('m-answer', 'Sì, la borsa nera c’è! Costa €89.00'),
    );
  let replies: RatedReply[] = [{ messageId: 'm-answer', rating: null }];
  return fakeInboxApi({
    conversations,
    routes: {
      'GET /api/v1/ai/conversations/c-giulia/replies': () => json(replies),
      'GET /api/v1/ai/messages/m-answer/trace': () => json(details),
      'PUT /api/v1/ai/messages/m-answer/feedback': (body) => {
        const saved = { messageId: 'm-answer', rating: (body as { rating: RatedReply['rating'] }).rating };
        replies = [saved];
        return json(saved);
      },
    },
  });
}

const history = () => screen.findByRole('list', { name: 'Messages' });

describe('Why this answer', () => {
  it('shows sources, tools and the reply check under the AI answer, not under the disclosure', async () => {
    const { fetchMock } = chatWithAiAnswer();
    await renderApp('/inbox/c-giulia');

    const list = await history();
    const why = await within(list).findByRole('button', { name: 'Why this answer' });
    expect(within(list).getAllByRole('button', { name: 'Why this answer' })).toHaveLength(1);
    // Loaded only when opened.
    expect(callsTo(fetchMock, '/api/v1/ai/messages/m-answer/trace')).toHaveLength(0);

    await userEvent.click(why);
    expect(why).toHaveAttribute('aria-expanded', 'true');
    expect(await within(list).findByText('return policy')).toBeInTheDocument();
    expect(within(list).getByText('Passed: prices only from your catalog, no links')).toBeInTheDocument();
    await userEvent.click(within(list).getByText('search_products'));
    expect(within(list).getByText('{"query":"borsa nera"}')).toBeVisible();
    expect(within(list).getByText(/89\.00 EUR/)).toBeVisible();
    expect(
      within(list).getByText(/gpt-5-mini · 1280 tokens · 2\.4 s · prompt agent-2026-11-04/),
    ).toBeInTheDocument();

    await userEvent.click(why);
    expect(why).toHaveAttribute('aria-expanded', 'false');
    expect(within(list).queryByText('return policy')).not.toBeInTheDocument();
  });

  it('names the document an answer came from', async () => {
    chatWithAiAnswer(
      trace({
        sources: [
          { kind: 'document', label: 'Guida.pdf › Ordini' },
          { kind: 'document', label: null },
        ],
      }),
    );
    await renderApp('/inbox/c-giulia');

    const list = await history();
    await userEvent.click(await within(list).findByRole('button', { name: 'Why this answer' }));
    expect(await within(list).findByText('Guida.pdf › Ordini, a document')).toBeInTheDocument();
  });

  it('says when the details were removed after the retention period', async () => {
    chatWithAiAnswer(trace({ hasDetails: false, tools: [], check: null }));
    await renderApp('/inbox/c-giulia');

    await userEvent.click(await within(await history()).findByRole('button', { name: 'Why this answer' }));
    expect(await screen.findByText('Details are removed after 90 days.')).toBeInTheDocument();
    expect(screen.getByText('search_products')).toBeInTheDocument();
  });

  it('rates a reply and takes the rating back with a second click', async () => {
    const { fetchMock } = chatWithAiAnswer();
    await renderApp('/inbox/c-giulia');

    const list = await history();
    const bad = await within(list).findByRole('button', { name: 'Bad reply' });
    expect(bad).toHaveAttribute('aria-pressed', 'false');

    await userEvent.click(bad);
    await waitFor(() => expect(bad).toHaveAttribute('aria-pressed', 'true'));
    expect(within(list).getByRole('button', { name: 'Good reply' })).toHaveAttribute('aria-pressed', 'false');

    await userEvent.click(bad);
    await waitFor(() => expect(bad).toHaveAttribute('aria-pressed', 'false'));
    expect(callsTo(fetchMock, '/api/v1/ai/messages/m-answer/feedback').map((c) => c.body)).toEqual([
      { rating: 'down' },
      { rating: null },
    ]);
  });
});
