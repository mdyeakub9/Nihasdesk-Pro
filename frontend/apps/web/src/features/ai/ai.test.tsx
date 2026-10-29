import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { Role } from '../session/types';
import { callsTo, fakeApi, json, testSession } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import type { AiSettings, TestReply } from './api';

const settings = (over: Partial<AiSettings> = {}): AiSettings => ({
  enabled: true,
  tone: 'friendly',
  instructions: null,
  disclosure: null,
  defaultDisclosure: 'Ciao! Sono l’assistente virtuale (IA) di Almanaam 🙂',
  providerConfigured: true,
  ...over,
});

function aiApi(role: Role = 'owner', extra: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    session: { ...testSession, role },
    ...extra,
    routes: {
      'GET /api/v1/ai/settings': () => json(settings()),
      'GET /api/v1/ai/questions': () => json([]),
      'GET /api/v1/ai/answers': () => json([]),
      ...extra.routes,
    },
  });
}

const reply = (over: Partial<TestReply> = {}): TestReply => ({
  disclosure: null,
  reply: null,
  handoff: null,
  question: null,
  blocked: null,
  failed: null,
  tools: [],
  sources: [],
  ...over,
});

describe('Settings → AI', () => {
  it('saves tone, guidance and the first message', async () => {
    const fetchMock = aiApi('owner', {
      routes: { 'PUT /api/v1/ai/settings': () => json(settings({ tone: 'formal' })) },
    });
    await renderApp('/settings/ai');

    await userEvent.selectOptions(await screen.findByLabelText('Tone'), 'formal');
    await userEvent.type(
      screen.getByLabelText('Your guidance for the AI'),
      'Spedizione gratuita sopra 50 €.',
    );
    expect(screen.getByLabelText('First message to customers')).toHaveAttribute(
      'placeholder',
      'Ciao! Sono l’assistente virtuale (IA) di Almanaam 🙂',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/ai/settings').find((c) => c.body)?.body).toEqual({
        enabled: true,
        tone: 'formal',
        instructions: 'Spedizione gratuita sopra 50 €.',
        disclosure: null,
      }),
    );
  });

  it('says when the AI is not connected and keeps the test chat off', async () => {
    aiApi('owner', {
      routes: { 'GET /api/v1/ai/settings': () => json(settings({ providerConfigured: false })) },
    });
    await renderApp('/settings/ai');

    expect(await screen.findByRole('note')).toHaveTextContent('no OpenAI key');
    expect(screen.getByLabelText('Test message')).toBeDisabled();
  });

  it('teaches the AI from an unanswered question', async () => {
    let open = [
      {
        id: 'q-1',
        conversationId: 'c-1',
        question: 'Spedite in Sardegna?',
        status: 'open',
        answerId: null,
        createdAt: '2026-10-28T10:00:00Z',
      },
    ];
    const fetchMock = aiApi('admin', {
      routes: { 'GET /api/v1/ai/questions': () => json(open) },
      post: {
        '/api/v1/ai/questions/q-1/answer': () => {
          open = [];
          return json({
            id: 'a-1',
            question: 'Spedite in Sardegna?',
            answer: 'Sì',
            active: true,
            updatedAt: '2026-10-28T10:00:00Z',
          });
        },
      },
    });
    await renderApp('/settings/ai');

    const list = await screen.findByRole('list', { name: 'Questions the AI couldn’t answer' });
    expect(await within(list).findByText('“Spedite in Sardegna?”')).toBeInTheDocument();
    expect(within(list).getByRole('link', { name: 'Open the conversation' })).toHaveAttribute(
      'href',
      '/inbox/c-1',
    );
    await userEvent.type(within(list).getByLabelText('Your answer'), 'Sì, isole comprese.');
    await userEvent.click(within(list).getByRole('button', { name: 'Teach the AI' }));

    expect(await screen.findByText('Nothing waiting. The AI knew the answers so far.')).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/ai/questions/q-1/answer')[0]?.body).toEqual({
      answer: 'Sì, isole comprese.',
    });
  });

  it('runs a test chat with the disclosure first and the real history after', async () => {
    let turn = 0;
    const fetchMock = aiApi('owner', {
      post: {
        '/api/v1/ai/test': () =>
          json(
            ++turn === 1
              ? reply({
                  disclosure: 'Ciao! Sono l’assistente virtuale.',
                  reply: 'Sì, costa €89.00!',
                  tools: ['search_products', 'search_knowledge'],
                  sources: [
                    { kind: 'policy', label: 'returns' },
                    { kind: 'answer', label: 'Posso cambiare taglia?' },
                  ],
                })
              : reply({
                  handoff: 'unknown_answer',
                  question: 'Spedite in Sardegna?',
                  tools: ['search_knowledge'],
                }),
          ),
      },
    });
    await renderApp('/settings/ai');

    const input = await screen.findByLabelText('Test message');
    // Enabled once the settings say the AI is connected.
    await waitFor(() => expect(input).toBeEnabled());
    await userEvent.type(input, 'Avete la borsa nera?{Enter}');
    const chat = screen.getByRole('list', { name: 'Test conversation' });
    expect(await within(chat).findByText('Sì, costa €89.00!')).toBeInTheDocument();
    expect(within(chat).getByText('Ciao! Sono l’assistente virtuale.')).toBeInTheDocument();
    expect(within(chat).getByText('Looked up: search_products, search_knowledge')).toBeInTheDocument();
    expect(within(chat).getByText('Based on: return policy, “Posso cambiare taglia?”')).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Test message'), 'Spedite in Sardegna?{Enter}');
    expect(
      await within(chat).findByText(/hand this to your team \(it doesn’t know the answer\)/),
    ).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/ai/test')[1]?.body).toEqual({
      messages: [
        { author: 'customer', text: 'Avete la borsa nera?' },
        { author: 'ai', text: 'Sì, costa €89.00!' },
        { author: 'customer', text: 'Spedite in Sardegna?' },
      ],
    });
  });

  it('explains why the AI provider failed', async () => {
    aiApi('owner', {
      post: { '/api/v1/ai/test': () => json(reply({ handoff: 'ai_unavailable', failed: 'http_401' })) },
    });
    await renderApp('/settings/ai');

    const input = await screen.findByLabelText('Test message');
    await waitFor(() => expect(input).toBeEnabled());
    await userEvent.type(input, 'Ciao{Enter}');
    expect(await screen.findByText(/didn’t accept the API key \(401\)/)).toBeInTheDocument();
  });

  it('is only for Owners and Admins', async () => {
    const fetchMock = aiApi('member');
    await renderApp('/settings/ai');

    expect(await screen.findByRole('note')).toHaveTextContent('Only Owners and Admins');
    expect(screen.queryByRole('link', { name: 'AI' })).not.toBeInTheDocument();
    // Members still see what the AI knows.
    expect(screen.getByRole('link', { name: 'Shop knowledge' })).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/ai/questions')).toHaveLength(0);
  });
});
