import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { fakeApi } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';

const conversationList = () => screen.findByRole('list', { name: 'Conversations' });
const names = (list: HTMLElement) =>
  within(list)
    .queryAllByRole('link')
    .map((link) => link.querySelector('.truncate')?.textContent);
const waitForRows = (list: HTMLElement) => within(list).findAllByRole('link');

describe('Inbox', () => {
  beforeEach(() => {
    fakeApi();
  });

  it('lists open conversations, newest first, with an empty conversation area', async () => {
    await renderApp('/inbox');

    const list = await conversationList();
    await waitForRows(list);
    expect(names(list)).toEqual([
      'Giulia Rossi',
      'Luca Bianchi',
      'Sofia Conti',
      'Marco Romano',
      'Elena Ricci',
      'Alessandro Costa',
    ]);
    expect(screen.getByRole('heading', { name: 'No conversation selected' })).toBeInTheDocument();
  });

  it('filters by status, channel and search', async () => {
    await renderApp('/inbox');
    const list = await conversationList();
    await waitForRows(list);

    await userEvent.click(screen.getByRole('button', { name: /Waiting/ }));
    expect(names(list)).toEqual(['Sofia Conti']);

    await userEvent.click(screen.getByRole('button', { name: /^Open\s*\d/ }));
    await userEvent.click(screen.getByRole('button', { name: 'WhatsApp' }));
    expect(names(list)).toEqual(['Elena Ricci']);

    await userEvent.click(screen.getByRole('button', { name: 'All' }));
    await userEvent.type(screen.getByRole('searchbox'), 'milano');
    expect(names(list)).toEqual(['Marco Romano']);
  });

  it('opens a conversation with messages, catalog product and customer details', async () => {
    await renderApp('/inbox/c-giulia');

    expect(await screen.findByRole('heading', { name: 'Giulia Rossi', level: 2 })).toBeInTheDocument();
    const messages = screen.getByRole('list', { name: 'Messages' });
    expect(
      within(messages).getByText('Ciao! Do you have the Classic Leather Bag in black?'),
    ).toBeInTheDocument();
    expect(within(messages).getByText('Classic Leather Bag')).toBeInTheDocument();
    expect(within(messages).getByText('In stock')).toBeInTheDocument();
    expect(within(messages).getByText(/€89\.00/)).toBeInTheDocument();

    const details = screen.getByRole('complementary', { name: 'Customer details' });
    expect(within(details).getByText('giulia.rossi@example.com')).toBeInTheDocument();
    expect(within(details).getByText('Hot lead')).toBeInTheDocument();
  });

  it('lets a team member take over and return the conversation to the AI', async () => {
    await renderApp('/inbox/c-giulia');

    await userEvent.click(await screen.findByRole('button', { name: 'Take over' }));

    expect(await screen.findByText(/Team answering/)).toBeInTheDocument();
    expect(screen.getByText('2 handoffs', { exact: false })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Return to AI' }));
    expect(await screen.findByText(/AI answering/)).toBeInTheDocument();
  });

  it('turning AI assistance off hands the conversation to the team', async () => {
    await renderApp('/inbox/c-giulia');

    await userEvent.click(await screen.findByRole('switch', { name: 'AI assistance' }));

    expect(await screen.findByText(/Team answering/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Return to AI' })).toBeDisabled();
  });

  it('sends a reply and adds an internal note', async () => {
    await renderApp('/inbox/c-giulia');

    const reply = await screen.findByRole('textbox', { name: 'Reply' });
    await userEvent.type(reply, 'Sure, I will prepare it.{Enter}');

    const messages = screen.getByRole('list', { name: 'Messages' });
    expect(await within(messages).findByText('Sure, I will prepare it.')).toBeInTheDocument();
    expect(screen.getByText(/Team answering/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Internal note' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Internal note' }), 'Gift wrap requested');
    await userEvent.click(screen.getByRole('button', { name: 'Add note' }));

    expect(await within(messages).findByText('Gift wrap requested')).toBeInTheDocument();
  });

  it('marks a conversation done and moves it to the Done tab', async () => {
    await renderApp('/inbox/c-giulia');

    await userEvent.click(await screen.findByRole('button', { name: 'Done' }));

    expect(await screen.findByRole('button', { name: 'Reopen' })).toBeInTheDocument();
    const list = await conversationList();
    await waitForRows(list);
    expect(names(list)).not.toContain('Giulia Rossi');
  });

  it('shows not found for an unknown conversation', async () => {
    await renderApp('/inbox/missing');

    expect(await screen.findByRole('heading', { name: 'Conversation not found' })).toBeInTheDocument();
  });
});
