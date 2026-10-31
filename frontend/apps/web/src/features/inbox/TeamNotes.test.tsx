import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo } from '../../test/fake-api';
import { fakeInboxApi, seedConversations } from '../../test/inbox-api';
import { renderApp } from '../../test/render-app';
import { hub } from '../../test/signalr-mock';

const panel = () => screen.findByRole('complementary', { name: 'Customer details' });
const messages = () => screen.findByRole('list', { name: 'Messages' });
const noteTexts = (list: HTMLElement) =>
  within(list)
    .getAllByRole('listitem', { hidden: true })
    .map((item) => item.querySelector('p')?.textContent);

/** Giulia's conversation with two notes from Luca (the newer one last) and an AI handoff note. */
function withNotes() {
  const conversations = seedConversations();
  const giulia = conversations.find((c) => c.id === 'c-giulia');
  const at = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();
  const note = (id: string, text: string, minutes: number) => ({
    id,
    author: 'agent' as const,
    authorUserId: 'u-2',
    authorName: 'Luca Neri',
    text,
    internal: true,
    sentAt: at(minutes),
    delivery: 'none' as const,
  });
  giulia?.messages.push(
    {
      id: 'n-ai',
      author: 'ai',
      authorUserId: null,
      authorName: null,
      text: 'The customer asked for a refund. Please answer this customer.',
      internal: true,
      sentAt: at(3),
      delivery: 'none',
    },
    note('n-vip', 'VIP, call after 6 pm', 2),
    note('n-ship', 'Asked about express shipping', 1),
  );
  return conversations;
}

describe('Team notes', () => {
  it('adds a note with an @mention in the Notes section only, not in the chat', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    const details = await panel();
    const box = within(details).getByRole('textbox', { name: 'Add a team note' });
    await userEvent.type(box, 'Please call her @Lu');
    const options = await screen.findByRole('listbox', { name: 'Mention a team member' });
    expect(within(options).getByRole('option', { name: 'Luca Neri' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await userEvent.keyboard('{Enter}');
    expect(box).toHaveValue('Please call her @Luca Neri ');
    await userEvent.type(box, 'after 6 pm');
    await userEvent.click(within(details).getByRole('button', { name: 'Add note' }));

    expect(
      callsTo(fetchMock, '/api/v1/conversations/c-giulia/notes')
        .filter((c) => c.body)
        .at(-1)?.body,
    ).toEqual({ text: 'Please call her @Luca Neri after 6 pm', mentionedUserIds: ['u-2'] });
    const list = await within(details).findByRole('list', { name: 'Notes' });
    expect(noteTexts(list)).toEqual(['Please call her @Luca Neri after 6 pm']);
    expect(within(details).getByText('1 note')).toBeInTheDocument();

    expect(within(await messages()).queryByText(/Please call her/)).not.toBeInTheDocument();
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
      ).toEqual({ text: 'No mention after all @Md', mentionedUserIds: [] }),
    );
  });

  it('shows the open-note count next to Team notes; the AI handoff note stays in the chat', async () => {
    fakeInboxApi({ conversations: withNotes() });
    await renderApp('/inbox/c-giulia');

    const details = await panel();
    expect(await within(details).findByText('2 notes')).toBeInTheDocument();
    expect(noteTexts(within(details).getByRole('list', { name: 'Notes' }))).toEqual([
      'Asked about express shipping',
      'VIP, call after 6 pm',
    ]);
    expect(within(details).queryByText(/asked for a refund/)).not.toBeInTheDocument();

    const chat = await messages();
    expect(within(chat).getByRole('note', { name: 'Internal note' })).toHaveTextContent('asked for a refund');
    expect(within(chat).queryByText('VIP, call after 6 pm')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Pin note/ })).not.toBeInTheDocument();
  });

  it('resolves a note: dimmed under Resolved, the count goes down, and it can be reopened', async () => {
    const { fetchMock } = fakeInboxApi({ conversations: withNotes() });
    await renderApp('/inbox/c-giulia');

    const details = await panel();
    const list = await within(details).findByRole('list', { name: 'Notes' });
    const vip = within(list).getAllByRole('listitem')[1] as HTMLElement;
    await userEvent.click(within(vip).getByRole('button', { name: 'Resolve note' }));

    expect(await within(details).findByText('1 note')).toBeInTheDocument();
    expect(noteTexts(within(details).getByRole('list', { name: 'Notes' }))).toEqual([
      'Asked about express shipping',
    ]);
    expect(within(details).getByText('Resolved (1)')).toBeInTheDocument();
    const resolved = within(details).getByRole('list', { name: 'Resolved notes', hidden: true });
    expect(resolved).toHaveTextContent('Resolved by Md Yeakub');
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/notes/n-vip/resolve')).toHaveLength(1);

    await userEvent.click(within(resolved).getByRole('button', { name: 'Reopen note', hidden: true }));
    expect(await within(details).findByText('2 notes')).toBeInTheDocument();
  });

  it('deletes a note after confirming, and Cancel keeps it', async () => {
    const { fetchMock } = fakeInboxApi({ conversations: withNotes() });
    await renderApp('/inbox/c-giulia');

    const details = await panel();
    const list = await within(details).findByRole('list', { name: 'Notes' });
    const vip = () => within(list).getAllByRole('listitem')[1] as HTMLElement;

    await userEvent.click(within(vip()).getByRole('button', { name: 'Delete note' }));
    expect(within(vip()).getByText('Delete this note?')).toBeInTheDocument();
    await userEvent.click(within(vip()).getByRole('button', { name: 'Cancel' }));
    expect(within(vip()).queryByText('Delete this note?')).not.toBeInTheDocument();

    await userEvent.click(within(vip()).getByRole('button', { name: 'Delete note' }));
    await userEvent.click(within(vip()).getByRole('button', { name: 'Delete' }));

    await waitFor(() =>
      expect(noteTexts(within(details).getByRole('list', { name: 'Notes' }))).toEqual([
        'Asked about express shipping',
      ]),
    );
    expect(within(details).getByText('1 note')).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/notes/n-vip')).toHaveLength(1);
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
