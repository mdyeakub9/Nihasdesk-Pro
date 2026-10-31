import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo, fakeApi, json, problem, testSession } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import type { SavedReply } from './api';

function fakeSavedReplies(initial: SavedReply[] = [], role: 'owner' | 'admin' | 'member' = 'owner') {
  let replies = [...initial];
  let next = 1;
  const fetchMock = fakeApi({
    session: { ...testSession, role },
    routes: {
      'GET /api/v1/saved-replies': () => json(replies),
      'POST /api/v1/saved-replies': (body) => {
        const input = body as Omit<SavedReply, 'id' | 'updatedAt'>;
        if (replies.some((r) => r.shortcut === input.shortcut))
          return problem(409, 'saved_reply.shortcut_taken');
        const created = { ...input, id: `r-${next++}`, updatedAt: new Date().toISOString() };
        replies.push(created);
        return json(created, 201);
      },
      'PUT /api/v1/saved-replies/r-ship': (body) => {
        replies = replies.map((r) => (r.id === 'r-ship' ? { ...r, ...(body as object) } : r));
        return json(replies.find((r) => r.id === 'r-ship'));
      },
      'DELETE /api/v1/saved-replies/r-ship': () => {
        replies = replies.filter((r) => r.id !== 'r-ship');
        return new Response(null, { status: 204 });
      },
    },
  });
  return fetchMock;
}

const ship: SavedReply = {
  id: 'r-ship',
  shortcut: 'ship',
  title: 'Shipping times',
  textEn: 'We ship in 2 days.',
  textIt: 'Spediamo in 2 giorni.',
  updatedAt: '2026-10-30T09:00:00Z',
};

describe('Settings → Saved replies', () => {
  it('adds a saved reply with English and Italian texts', async () => {
    const fetchMock = fakeSavedReplies();
    await renderApp('/settings/saved-replies');

    expect(await screen.findByText('No saved replies yet.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Add saved reply' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Shortcut' }), '/size');
    await userEvent.type(screen.getByRole('textbox', { name: 'Title' }), 'Size guide');
    await userEvent.type(
      screen.getByRole('textbox', { name: 'English text' }),
      'Hi {{first_name}, sizes run small.',
    );
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    const list = await screen.findByRole('list', { name: 'Saved replies' });
    expect(within(list).getByText(/Size guide/)).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/saved-replies').find((c) => c.body)?.body).toEqual({
      shortcut: 'size',
      title: 'Size guide',
      textEn: 'Hi {first_name}, sizes run small.',
      textIt: null,
    });
  });

  it('explains a taken shortcut', async () => {
    fakeSavedReplies([ship]);
    await renderApp('/settings/saved-replies');

    await userEvent.click(await screen.findByRole('button', { name: 'Add saved reply' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Shortcut' }), 'ship');
    await userEvent.type(screen.getByRole('textbox', { name: 'Title' }), 'Again');
    await userEvent.type(screen.getByRole('textbox', { name: 'Italian text' }), 'Ancora');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Another saved reply already uses this shortcut.')).toBeInTheDocument();
  });

  it('edits and deletes after confirming', async () => {
    const fetchMock = fakeSavedReplies([ship]);
    await renderApp('/settings/saved-replies');

    const list = await screen.findByRole('list', { name: 'Saved replies' });
    await userEvent.click(within(list).getByRole('button', { name: 'Edit' }));
    const title = within(list).getByRole('textbox', { name: 'Title' });
    await userEvent.clear(title);
    await userEvent.type(title, 'Delivery');
    await userEvent.click(within(list).getByRole('button', { name: 'Save' }));
    expect(await within(list).findByText(/Delivery/)).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/saved-replies/r-ship')[0]?.body).toMatchObject({ title: 'Delivery' });

    await userEvent.click(within(list).getByRole('button', { name: 'Delete' }));
    expect(within(list).getByText('Delete this saved reply?')).toBeInTheDocument();
    await userEvent.click(within(list).getByRole('button', { name: 'Delete' }));
    expect(await screen.findByText('No saved replies yet.')).toBeInTheDocument();
  });

  it('shows members the list without edit buttons', async () => {
    fakeSavedReplies([ship], 'member');
    await renderApp('/settings/saved-replies');

    const list = await screen.findByRole('list', { name: 'Saved replies' });
    expect(within(list).getByText(/We ship in 2 days\./)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add saved reply' })).not.toBeInTheDocument();
    expect(within(list).queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('link', { name: 'Saved replies' })).toBeInTheDocument());
  });
});
