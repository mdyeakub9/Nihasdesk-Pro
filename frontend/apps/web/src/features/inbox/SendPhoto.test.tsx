import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { problem } from '../../test/fake-api';
import {
  attachment,
  customerMessage,
  fakeInboxApi,
  seedConversations,
  type FakeConversation,
} from '../../test/inbox-api';
import { renderApp } from '../../test/render-app';

const user = () => userEvent.setup({ applyAccept: false });
const photo = (type = 'image/jpeg', size = 2_000) => new File([new Uint8Array(size)], 'foto.jpg', { type });
const photoInput = () => screen.getByLabelText<HTMLInputElement>('Photo to send');
const messages = () => screen.findByRole('list', { name: 'Messages' });

async function openMenu() {
  await userEvent.click(await screen.findByRole('button', { name: 'More actions' }));
  return screen.getByRole('menu', { name: 'More actions' });
}

describe('Sending a photo (ADR 0031 §6)', () => {
  it('uploads the chosen photo and shows it in the team bubble', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');
    await messages();

    await user().upload(photoInput(), photo());

    const list = await messages();
    expect(await within(list).findByRole('button', { name: 'Open photo full size' })).toBeInTheDocument();
    const upload = fetchMock.mock.calls.find(([url]) => String(url).endsWith('/c-giulia/images'));
    const form = upload?.[1]?.body as FormData;
    expect((form.get('file') as File).name).toBe('foto.jpg');
    expect(new Headers(upload?.[1]?.headers).get('X-XSRF-TOKEN')).toBe('test-csrf-token');
  });

  it('refuses other files and photos over 10 MB before uploading', async () => {
    const { fetchMock } = fakeInboxApi();
    await renderApp('/inbox/c-giulia');
    await messages();

    await user().upload(photoInput(), new File(['MZ'], 'virus.exe', { type: 'application/x-msdownload' }));
    expect(await screen.findByText('Send a JPEG, PNG, WebP or GIF photo.')).toBeInTheDocument();

    await user().upload(photoInput(), photo('image/png', 10 * 1024 * 1024 + 1));
    expect(
      await screen.findByText('The photo is larger than 10 MB. Choose a smaller one.'),
    ).toBeInTheDocument();

    expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/images'))).toBe(false);
  });

  it("says when the server refuses it, e.g. the photo isn't really an image", async () => {
    fakeInboxApi({ image: () => problem(400, 'media.type_not_allowed') });
    await renderApp('/inbox/c-giulia');
    await messages();

    await user().upload(photoInput(), photo());

    expect(await screen.findByText('Send a JPEG, PNG, WebP or GIF photo.')).toBeInTheDocument();
  });

  it('switches "Send photo" off after the 24-hour window', async () => {
    const conversations = seedConversations();
    const giulia = conversations.find((c) => c.id === 'c-giulia') as FakeConversation;
    giulia.messages = [customerMessage('Ciao', 25 * 60)];
    fakeInboxApi({ conversations });
    await renderApp('/inbox/c-giulia');
    await messages();

    const menu = await openMenu();
    const item = within(menu).getByRole('menuitem', { name: 'Send photo' });
    expect(item).toBeDisabled();
    expect(item).toHaveAttribute(
      'title',
      "Photos can only be sent within 24 hours of the customer's last message.",
    );
  });

  it('shows media removed after the retention period', async () => {
    const conversations = seedConversations();
    const giulia = conversations.find((c) => c.id === 'c-giulia') as FakeConversation;
    giulia.messages.push({
      ...customerMessage('', 1),
      attachments: [attachment('a-old', 'image', 'deleted')],
    });
    fakeInboxApi({ conversations });
    await renderApp('/inbox/c-giulia');

    const list = await messages();
    await waitFor(() =>
      expect(within(list).getByText(/Photo · Removed after the retention period/)).toBeInTheDocument(),
    );
    expect(within(list).queryByRole('img')).not.toBeInTheDocument();
  });
});
