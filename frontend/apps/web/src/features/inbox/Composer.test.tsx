import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo } from '../../test/fake-api';
import { fakeInboxApi, seedConversations } from '../../test/inbox-api';
import { renderApp } from '../../test/render-app';
import { hub } from '../../test/signalr-mock';

const replyBox = () => screen.findByRole('textbox', { name: 'Reply' });
const sentBodies = (fetchMock: ReturnType<typeof fakeInboxApi>['fetchMock'], id = 'c-giulia') =>
  callsTo(fetchMock, `/api/v1/conversations/${id}/messages`)
    .map((c) => c.body)
    .filter(Boolean);

describe('Composer', () => {
  it('sends with Ctrl+Enter; Enter alone is a new line', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    const box = await replyBox();
    await userEvent.type(box, 'Ciao!{Enter}Come posso aiutarti?');
    expect(box).toHaveValue('Ciao!\nCome posso aiutarti?');
    expect(sentBodies(fetchMock)).toEqual([]);
    expect(screen.getByText('Ctrl+Enter to send · / for saved replies')).toBeInTheDocument();

    await userEvent.keyboard('{Control>}{Enter}{/Control}');
    await waitFor(() => expect(sentBodies(fetchMock)).toEqual([{ text: 'Ciao!\nCome posso aiutarti?' }]));
    await waitFor(() => expect(box).toHaveValue(''));
  });

  it('keeps the draft per conversation when switching chats', async () => {
    fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    await userEvent.type(await replyBox(), 'Half-written answer');
    await userEvent.click(screen.getByRole('link', { name: /Luca Bianchi/ }));
    expect(await screen.findByRole('heading', { name: 'Luca Bianchi', level: 2 })).toBeInTheDocument();
    expect(await replyBox()).toHaveValue('');

    await userEvent.click(screen.getByRole('link', { name: /Giulia Rossi/ }));
    expect(await screen.findByRole('heading', { name: 'Giulia Rossi', level: 2 })).toBeInTheDocument();
    expect(await replyBox()).toHaveValue('Half-written answer');
  });

  it('inserts a saved reply with / in the customer’s language and never sends it by itself', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    const box = await replyBox();
    await userEvent.type(box, '/sh');
    const list = await screen.findByRole('listbox', { name: 'Saved replies' });
    expect(within(list).getAllByRole('option')).toHaveLength(1);
    expect(within(list).getByRole('option')).toHaveAttribute('aria-selected', 'true');
    await userEvent.keyboard('{Enter}');

    // Giulia writes in Italian; her first name and the shop name are filled in.
    expect(box).toHaveValue('Ciao Giulia, Almanaam spedisce in 2 giorni.');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(sentBodies(fetchMock)).toEqual([]);
  });

  it('moves through saved replies with the arrow keys, falls back to English, and Esc closes', async () => {
    const conversations = seedConversations();
    const giulia = conversations.find((c) => c.id === 'c-giulia');
    if (giulia) giulia.contact.language = 'it';
    fakeInboxApi({ conversations });
    await renderApp('/inbox/c-giulia');

    const box = await replyBox();
    await userEvent.type(box, 'Thanks! /');
    const list = await screen.findByRole('listbox', { name: 'Saved replies' });
    expect(within(list).getAllByRole('option')).toHaveLength(2);
    await userEvent.keyboard('{ArrowDown}');
    expect(within(list).getAllByRole('option')[1]).toHaveAttribute('aria-selected', 'true');
    await userEvent.keyboard('{Enter}');
    // The size guide has no Italian text, so the English one is used.
    expect(box).toHaveValue('Thanks! Our sizes run small.');

    await userEvent.type(box, ' /');
    expect(await screen.findByRole('listbox')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(box).toHaveValue('Thanks! Our sizes run small. /');
  });

  it('shows who else is typing in this conversation and tells the others when you type', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');
    await waitFor(() => expect(hub.started).toBe(1));
    await replyBox();

    act(() => hub.emit('typing', { conversationId: 'c-giulia', userId: 'u-2' }));
    expect(await screen.findByText('Luca Neri is typing…')).toBeInTheDocument();
    // Your own signal and other conversations' signals are not shown.
    act(() => hub.emit('typing', { conversationId: 'c-giulia', userId: 'u-1' }));
    act(() => hub.emit('typing', { conversationId: 'c-sofia', userId: 'u-2' }));
    expect(screen.getAllByText(/is typing…/)).toHaveLength(1);

    await userEvent.type(await replyBox(), 'abc');
    // Throttled: one signal for a burst of keys, with no text in it.
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/typing')).toHaveLength(1);
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/typing')[0]?.body).toEqual({});
  });
});
