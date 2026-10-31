import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo, problem } from '../../test/fake-api';
import {
  customerMessage,
  fakeInboxApi,
  seedConversations,
  type FakeConversation,
} from '../../test/inbox-api';
import { renderApp } from '../../test/render-app';
import { hub } from '../../test/signalr-mock';

const conversationList = () => screen.findByRole('list', { name: 'Conversations' });
const names = (list: HTMLElement) =>
  within(list)
    .queryAllByRole('link')
    .map((link) => link.querySelector('.truncate')?.textContent);
const waitForRows = (list: HTMLElement) => within(list).findAllByRole('link');
const messages = () => screen.findByRole('list', { name: 'Messages' });
const giulia = (list: FakeConversation[]) => {
  const found = list.find((c) => c.id === 'c-giulia');
  if (!found) throw new Error('Seed has no c-giulia.');
  return found;
};

describe('Inbox', () => {
  it('lists open conversations from the API, newest first, with a name for unknown customers', async () => {
    fakeInboxApi();
    await renderApp('/inbox');

    const list = await conversationList();
    await waitForRows(list);
    expect(names(list)).toEqual([
      'Giulia Rossi',
      'Luca Bianchi',
      'Sofia Conti',
      'Marco Romano',
      'Elena Ricci',
      'Unknown customer',
    ]);
    expect(screen.getByRole('heading', { name: 'No conversation selected' })).toBeInTheDocument();
  });

  it('filters by status, channel, assignee and search', async () => {
    fakeInboxApi();
    await renderApp('/inbox');
    const list = await conversationList();
    await waitForRows(list);

    await userEvent.click(screen.getByRole('button', { name: /Waiting/ }));
    expect(names(list)).toEqual(['Sofia Conti']);

    await userEvent.click(screen.getByRole('button', { name: /^Done\s*\d/ }));
    expect(names(list)).toEqual(['Anna Verdi']);

    await userEvent.click(screen.getByRole('button', { name: /^Open\s*\d/ }));
    await userEvent.click(screen.getByRole('button', { name: 'WhatsApp' }));
    expect(names(list)).toEqual(['Elena Ricci']);

    await userEvent.click(screen.getByRole('button', { name: 'All' }));
    await userEvent.type(screen.getByRole('searchbox'), 'milano');
    expect(names(list)).toEqual(['Marco Romano']);
  });

  it('opens a conversation with its messages and marks it read', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-sofia');

    expect(await screen.findByRole('heading', { name: 'Sofia Conti', level: 2 })).toBeInTheDocument();
    expect(within(await messages()).getByText('Can I change my delivery address?')).toBeInTheDocument();
    await waitFor(() => expect(callsTo(fetchMock, '/api/v1/conversations/c-sofia/read')).toHaveLength(1));
  });

  it('shows customer details and does not mark read conversations again', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');
    const details = await screen.findByRole('complementary', { name: 'Customer details' });
    expect(within(details).getByText('giulia.rossi@example.com')).toBeInTheDocument();
    expect(within(details).getByText('Hot lead')).toBeInTheDocument();
    // Already read: no second call.
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/read')).toHaveLength(0);
  });

  it('takes over and returns the conversation to the AI, without the old text under the composer', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    expect(await screen.findByText(/AI answering/)).toBeInTheDocument();
    expect(screen.queryByText(/AI is answering this customer\. Sending/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send & take over' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Take over' }));
    expect(await screen.findByText(/Team answering/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Return to AI' }));
    expect(await screen.findByText(/AI answering/)).toBeInTheDocument();
    expect(
      callsTo(fetchMock, '/api/v1/conversations/c-giulia')
        .map((c) => c.body)
        .filter(Boolean),
    ).toEqual([{ responder: 'human' }, { responder: 'ai' }]);
  });

  it('turning AI assistance off hands the conversation to the team', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    await userEvent.click(await screen.findByRole('switch', { name: 'AI assistance' }));

    expect(await screen.findByText(/Team answering/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Return to AI' })).toBeDisabled();
    expect(
      callsTo(fetchMock, '/api/v1/conversations/c-giulia')
        .map((c) => c.body)
        .filter(Boolean),
    ).toEqual([{ aiEnabled: false, responder: 'human' }]);
  });

  it('sends a reply to the channel from a composer without a note mode', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    await userEvent.type(await screen.findByRole('textbox', { name: 'Reply' }), 'Sì, ce l’abbiamo.{Enter}');
    expect(await within(await messages()).findByText('Sì, ce l’abbiamo.')).toBeInTheDocument();
    expect(within(await messages()).getByText('Sending…')).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/messages').at(-1)?.body).toEqual({
      text: 'Sì, ce l’abbiamo.',
    });
    expect(screen.queryByRole('tab', { name: 'Internal note' })).not.toBeInTheDocument();
  });

  it('explains the 24-hour window and shows replies that were not delivered', async () => {
    const conversations = seedConversations();
    giulia(conversations).messages.push({
      ...customerMessage('Earlier reply', 1),
      author: 'agent',
      authorName: 'Md Yeakub',
      delivery: 'failed',
    });
    fakeInboxApi({ conversations, reply: () => problem(409, 'conversation.outside_window') });
    await renderApp('/inbox/c-giulia');

    expect(await within(await messages()).findByText('Not delivered')).toBeInTheDocument();
    await userEvent.type(screen.getByRole('textbox', { name: 'Reply' }), 'Too late{Enter}');
    expect(await screen.findByRole('alert')).toHaveTextContent('more than 24 hours old');
  });

  it('loads older messages', async () => {
    const conversations = seedConversations();
    giulia(conversations).messages = [
      customerMessage('The very first message', 90),
      customerMessage('A newer one', 3),
    ];
    fakeInboxApi({ conversations, pageSize: 1 });
    await renderApp('/inbox/c-giulia');

    expect(await within(await messages()).findByText('A newer one')).toBeInTheDocument();
    expect(within(await messages()).queryByText('The very first message')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Load older messages' }));
    expect(await within(await messages()).findByText('The very first message')).toBeInTheDocument();
  });

  it('marks a conversation done and moves it to the Done tab', async () => {
    fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    await userEvent.click(await screen.findByRole('button', { name: 'Done' }));

    expect(await screen.findByRole('button', { name: 'Reopen' })).toBeInTheDocument();
    const list = await conversationList();
    await waitFor(() => expect(names(list)).not.toContain('Giulia Rossi'));
  });

  it('shows new conversations as soon as the hub says something changed', async () => {
    const { state } = fakeInboxApi();
    await renderApp('/inbox');
    const list = await conversationList();
    await waitForRows(list);
    await waitFor(() => expect(hub.started).toBe(1));
    expect(hub.options).toMatchObject({
      withCredentials: true,
      headers: { 'X-XSRF-TOKEN': 'test-csrf-token' },
    });

    state.conversations.push({
      ...giulia(seedConversations()),
      id: 'c-new',
      contactName: 'Nuovo Cliente',
      unread: true,
      lastMessageAt: new Date().toISOString(),
    });
    hub.emit('conversationChanged', { conversationId: 'c-new' });

    await waitFor(() => expect(names(list)[0]).toBe('Nuovo Cliente'));
  });

  it('shows not found for an unknown conversation', async () => {
    fakeInboxApi();
    await renderApp('/inbox/missing');

    expect(await screen.findByRole('heading', { name: 'Conversation not found' })).toBeInTheDocument();
  });
});
