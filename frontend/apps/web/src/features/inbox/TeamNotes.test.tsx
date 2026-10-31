import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo } from '../../test/fake-api';
import { fakeInboxApi, seedConversations } from '../../test/inbox-api';
import { renderApp } from '../../test/render-app';
import { hub } from '../../test/signalr-mock';

const panel = () => screen.findByRole('complementary', { name: 'Customer details' });
const messages = () => screen.findByRole('list', { name: 'Messages' });

describe('Team notes', () => {
  it('adds a note with an @mention from the side panel and shows it as a team-only bubble', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    const box = within(await panel()).getByRole('textbox', { name: 'Add a team note' });
    await userEvent.type(box, 'Please call her @Lu');
    const options = await screen.findByRole('listbox', { name: 'Mention a team member' });
    expect(within(options).getByRole('option', { name: 'Luca Neri' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await userEvent.keyboard('{Enter}');
    expect(box).toHaveValue('Please call her @Luca Neri ');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();

    await userEvent.type(box, 'after 6 pm');
    await userEvent.click(within(await panel()).getByRole('button', { name: 'Add note' }));

    expect(
      callsTo(fetchMock, '/api/v1/conversations/c-giulia/notes')
        .filter((c) => c.body)
        .at(-1)?.body,
    ).toEqual({
      text: 'Please call her @Luca Neri after 6 pm',
      mentionedUserIds: ['u-2'],
    });
    const bubble = await within(await messages()).findByRole('note', { name: 'Internal note' });
    expect(bubble).toHaveTextContent('Only your team sees this');
    expect(bubble).toHaveTextContent('Please call her @Luca Neri after 6 pm');
    // Notes never go through the reply endpoint.
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/messages').filter((c) => c.body)).toEqual([]);
  });

  it('drops a mention whose @name was deleted and closes the list with Escape', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    const box = within(await panel()).getByRole('textbox', { name: 'Add a team note' });
    await userEvent.type(box, '@Lu');
    await userEvent.keyboard('{Enter}');
    await userEvent.clear(box);
    await userEvent.type(box, 'No mention after all @Md');
    expect(await screen.findByRole('listbox')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    await userEvent.click(within(await panel()).getByRole('button', { name: 'Add note' }));

    await waitFor(() =>
      expect(
        callsTo(fetchMock, '/api/v1/conversations/c-giulia/notes')
          .filter((c) => c.body)
          .at(-1)?.body,
      ).toEqual({
        text: 'No mention after all @Md',
        mentionedUserIds: [],
      }),
    );
  });

  it('pins a note to the top of the side panel and unpins it', async () => {
    const conversations = seedConversations();
    const giulia = conversations.find((c) => c.id === 'c-giulia');
    giulia?.messages.push({
      id: 'n-vip',
      author: 'agent',
      authorUserId: 'u-2',
      authorName: 'Luca Neri',
      text: 'VIP, call after 6 pm',
      internal: true,
      sentAt: new Date().toISOString(),
      delivery: 'none',
      pinnedAt: null,
    });
    const { fetchMock } = fakeInboxApi({ conversations });
    await renderApp('/inbox/c-giulia');

    const details = await panel();
    const recent = await within(details).findByRole('list', { name: 'Recent notes' });
    await userEvent.click(within(recent).getByRole('button', { name: 'Pin note' }));

    const pinned = await within(details).findByRole('list', { name: 'Pinned notes' });
    expect(pinned).toHaveTextContent('VIP, call after 6 pm');
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/notes/n-vip/pin')).toHaveLength(1);
    expect(await within(await messages()).findByRole('note', { name: 'Internal note' })).toHaveTextContent(
      'Pinned',
    );

    await userEvent.click(within(pinned).getByRole('button', { name: 'Unpin note' }));
    await waitFor(() =>
      expect(within(details).queryByRole('list', { name: 'Pinned notes' })).not.toBeInTheDocument(),
    );
  });

  it('tells a member they were mentioned and links to the conversation', async () => {
    fakeInboxApi();
    await renderApp('/inbox');
    await waitFor(() => expect(hub.started).toBe(1));

    hub.emit('mentioned', { conversationId: 'c-sofia', noteId: 'n-1' });

    const status = await screen.findByText('You were mentioned in a team note.');
    const alert = status.closest('[role="status"]') as HTMLElement;
    await userEvent.click(within(alert).getByRole('link', { name: 'Open' }));
    expect(await screen.findByRole('heading', { name: 'Sofia Conti', level: 2 })).toBeInTheDocument();
    expect(screen.queryByText('You were mentioned in a team note.')).not.toBeInTheDocument();
  });
});
