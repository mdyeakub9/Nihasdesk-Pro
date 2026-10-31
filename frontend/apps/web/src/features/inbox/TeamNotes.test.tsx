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

/** Giulia's conversation with two notes from Luca, the newer one last. */
function withNotes() {
  const conversations = seedConversations();
  const giulia = conversations.find((c) => c.id === 'c-giulia');
  const note = (id: string, text: string, minutes: number) => ({
    id,
    author: 'agent' as const,
    authorUserId: 'u-2',
    authorName: 'Luca Neri',
    text,
    internal: true,
    sentAt: new Date(Date.now() - minutes * 60_000).toISOString(),
    delivery: 'none' as const,
    pinnedAt: null,
  });
  giulia?.messages.push(
    note('n-vip', 'VIP, call after 6 pm', 2),
    note('n-ship', 'Asked about express shipping', 1),
  );
  // Keep c-giulia first, so conversations[0] is it.
  return conversations;
}

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

  it('pins a note above the message box without moving it in the side panel', async () => {
    const { fetchMock } = fakeInboxApi({ conversations: withNotes() });
    await renderApp('/inbox/c-giulia');

    const details = await panel();
    const list = await within(details).findByRole('list', { name: 'Notes' });
    expect(noteTexts(list)).toEqual(['Asked about express shipping', 'VIP, call after 6 pm']);
    expect(screen.queryByRole('region', { name: 'Pinned notes' })).not.toBeInTheDocument();

    const vip = within(list).getAllByRole('listitem')[1] as HTMLElement;
    await userEvent.click(within(vip).getByRole('button', { name: 'Pin note' }));

    const pinnedBar = await screen.findByRole('region', { name: 'Pinned notes' });
    expect(pinnedBar).toHaveTextContent('VIP, call after 6 pm');
    expect(details).not.toContainElement(pinnedBar);
    // The side panel keeps time order.
    await waitFor(() => expect(within(list).getAllByRole('button', { name: 'Unpin note' })).toHaveLength(1));
    expect(noteTexts(list)).toEqual(['Asked about express shipping', 'VIP, call after 6 pm']);
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/notes/n-vip/pin')).toHaveLength(1);

    await userEvent.click(within(pinnedBar).getByRole('button', { name: 'Unpin note' }));
    await waitFor(() =>
      expect(screen.queryByRole('region', { name: 'Pinned notes' })).not.toBeInTheDocument(),
    );
  });

  it('resolves a note: it leaves the pinned bar, is dimmed under Resolved and can be reopened', async () => {
    const conversations = withNotes();
    const vip = conversations[0]?.messages.find((m) => m.id === 'n-vip');
    if (vip) vip.pinnedAt = new Date().toISOString();
    const { fetchMock } = fakeInboxApi({ conversations });
    await renderApp('/inbox/c-giulia');

    const pinnedBar = await screen.findByRole('region', { name: 'Pinned notes' });
    await userEvent.click(within(pinnedBar).getByRole('button', { name: 'Resolve note' }));

    await waitFor(() =>
      expect(screen.queryByRole('region', { name: 'Pinned notes' })).not.toBeInTheDocument(),
    );
    const details = await panel();
    expect(noteTexts(within(details).getByRole('list', { name: 'Notes' }))).toEqual([
      'Asked about express shipping',
    ]);
    expect(within(details).getByText('Resolved (1)')).toBeInTheDocument();
    const resolved = within(details).getByRole('list', { name: 'Resolved notes', hidden: true });
    expect(resolved).toHaveTextContent('VIP, call after 6 pm');
    expect(resolved).toHaveTextContent('Resolved by Md Yeakub');
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/notes/n-vip/resolve')).toHaveLength(1);
    expect(
      within(await messages())
        .getAllByRole('note', { name: 'Internal note' })
        .some((n) => n.textContent?.includes('Resolved')),
    ).toBe(true);

    await userEvent.click(within(resolved).getByRole('button', { name: 'Reopen note', hidden: true }));
    await waitFor(() =>
      expect(noteTexts(within(details).getByRole('list', { name: 'Notes' }))).toEqual([
        'Asked about express shipping',
        'VIP, call after 6 pm',
      ]),
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
