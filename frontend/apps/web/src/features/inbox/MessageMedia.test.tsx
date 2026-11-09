import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo } from '../../test/fake-api';
import {
  attachment,
  customerMessage,
  fakeInboxApi,
  seedConversations,
  type FakeAttachment,
  type FakeConversation,
} from '../../test/inbox-api';
import { renderApp } from '../../test/render-app';
import { pendingMediaRefreshMs } from './api';

/** Giulia's chat with one media-only customer message (text is empty, ADR 0031). */
function withMedia(attachments: FakeAttachment[], preview = '[image]') {
  const conversations = seedConversations();
  const giulia = conversations.find((c) => c.id === 'c-giulia') as FakeConversation;
  const message = { ...customerMessage('', 1), attachments };
  giulia.messages.push(message);
  giulia.preview = preview;
  giulia.lastMessageAt = message.sentAt;
  return { conversations, message };
}

const messages = () => screen.findByRole('list', { name: 'Messages' });

describe('Customer media in the inbox', () => {
  it('shows a photo as a thumbnail that opens full size, and the list says "Photo"', async () => {
    const { conversations } = withMedia([
      attachment('a-photo', 'image', 'saved', { width: 640, height: 480 }),
    ]);
    fakeInboxApi({ conversations });
    await renderApp('/inbox/c-giulia');

    const list = await messages();
    const open = await within(list).findByRole('button', { name: 'Open photo full size' });
    expect(within(open).getByRole('img', { name: 'Photo' })).toHaveAttribute(
      'src',
      '/api/v1/media/a-photo?variant=thumbnail',
    );
    // No empty text bubble and no "[image]" for a media-only message.
    expect(within(list).queryByText('[image]')).not.toBeInTheDocument();
    const conversationList = screen.getByRole('list', { name: 'Conversations' });
    expect(within(conversationList).getByText('📷 Photo')).toBeInTheDocument();

    await userEvent.click(open);
    const viewer = screen.getByRole('dialog', { name: 'Photo' });
    expect(within(viewer).getByRole('img', { name: 'Photo' })).toHaveAttribute(
      'src',
      '/api/v1/media/a-photo',
    );
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(open).toHaveFocus();
  });

  it('plays voice notes and offers videos and files as downloads with name and size', async () => {
    const { conversations } = withMedia(
      [
        attachment('a-voice', 'audio', 'saved', { contentType: 'audio/ogg' }),
        attachment('a-video', 'video', 'saved', { sizeBytes: 4_200_000 }),
        attachment('a-pdf', 'file', 'saved', { fileName: 'ricevuta.pdf', sizeBytes: 820_000 }),
        attachment('a-sticker', 'sticker'),
      ],
      '[audio]',
    );
    fakeInboxApi({ conversations });
    await renderApp('/inbox/c-giulia');

    const list = await messages();
    const voice = await within(list).findByLabelText('Voice note');
    expect(voice.tagName).toBe('AUDIO');
    expect(voice).toHaveAttribute('src', '/api/v1/media/a-voice');

    const video = within(list).getByRole('link', { name: 'Download Video' });
    expect(video).toHaveAttribute('href', '/api/v1/media/a-video');
    expect(video).toHaveTextContent('4.2 MB');
    const pdf = within(list).getByRole('link', { name: 'Download ricevuta.pdf' });
    expect(pdf).toHaveAttribute('download', 'ricevuta.pdf');
    expect(pdf).toHaveTextContent('820 kB');

    // Stickers are small images that don't open.
    expect(within(list).getByRole('img', { name: 'Sticker' })).toBeInTheDocument();
    expect(within(list).queryAllByRole('button', { name: 'Open photo full size' })).toHaveLength(0);
    expect(
      within(screen.getByRole('list', { name: 'Conversations' })).getByText('🎤 Voice note'),
    ).toBeInTheDocument();
  });

  it('says why media could not be loaded, so the team can ask again', async () => {
    const { conversations } = withMedia([
      attachment('a-old', 'image', 'failed', { failureCode: 'expired' }),
      attachment('a-big', 'video', 'too_large'),
      attachment('a-exe', 'file', 'failed', { failureCode: 'type_not_allowed' }),
    ]);
    fakeInboxApi({ conversations });
    await renderApp('/inbox/c-giulia');

    const list = await messages();
    expect(await within(list).findAllByText('Media could not be loaded')).toHaveLength(3);
    expect(within(list).getByText(/The link expired before we could save it/)).toBeInTheDocument();
    expect(within(list).getByText(/larger than the limit/)).toBeInTheDocument();
    expect(within(list).getByText(/file type isn't supported/)).toBeInTheDocument();
    expect(within(list).queryByRole('link')).not.toBeInTheDocument();
  });

  it('reloads the open chat every 3 seconds while media is pending, and stops once it is in', async () => {
    const { conversations, message } = withMedia([attachment('a-photo', 'image', 'pending')]);
    const { fetchMock } = fakeInboxApi({ conversations });
    await renderApp('/inbox/c-giulia');

    const list = await messages();
    expect(await within(list).findByText('Loading photo…')).toBeInTheDocument();
    const loads = () => callsTo(fetchMock, '/api/v1/conversations/c-giulia/messages?limit=50').length;
    const before = loads();

    // The Workers job saves it; the screen picks it up without a SignalR event.
    message.attachments = [attachment('a-photo', 'image', 'saved')];
    expect(
      await within(list).findByRole(
        'button',
        { name: 'Open photo full size' },
        { timeout: pendingMediaRefreshMs + 2_000 },
      ),
    ).toBeInTheDocument();
    const after = loads();
    expect(after).toBeGreaterThan(before);

    // Nothing pending: no more reloads.
    await new Promise((resolve) => setTimeout(resolve, pendingMediaRefreshMs + 500));
    await waitFor(() => expect(loads()).toBe(after));
  }, 15_000);

  it('shows the media labels in Italian', async () => {
    const { conversations } = withMedia([attachment('a-old', 'image', 'failed', { failureCode: 'network' })]);
    fakeInboxApi({ conversations });
    await renderApp('/inbox/c-giulia', 'it');

    const list = await screen.findByRole('list', { name: 'Messaggi' });
    expect(await within(list).findByText('Impossibile caricare il contenuto')).toBeInTheDocument();
    expect(screen.getByText('📷 Foto')).toBeInTheDocument();
  });
});
