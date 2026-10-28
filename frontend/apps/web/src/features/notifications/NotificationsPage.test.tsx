import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { fakeApi } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';

async function notificationList() {
  const section = await screen.findByRole('region', { name: 'Notifications' });
  return section;
}

describe('Notifications', () => {
  beforeEach(() => {
    fakeApi();
  });

  it('lists open notifications with links to the right place', async () => {
    await renderApp('/notifications');
    const list = await notificationList();

    expect(await within(list).findByText('Sofia wants to speak with your team')).toBeInTheDocument();
    expect(within(list).getByText('New order request from Giulia')).toBeInTheDocument();
    expect(within(list).getByText('AI needs an answer about your return policy')).toBeInTheDocument();
    expect(within(list).queryByText('Luca wants to speak with your team')).not.toBeInTheDocument();
    expect(within(list).getByRole('link', { name: 'Take over' })).toHaveAttribute('href', '/inbox/c-sofia');
    expect(within(list).getByRole('link', { name: 'Review order' })).toHaveAttribute('href', '/orders');
  });

  it('resolving moves a notification to the Resolved tab', async () => {
    await renderApp('/notifications');
    const list = await notificationList();
    await within(list).findByText('Sofia wants to speak with your team');

    const row = within(list).getByText('Sofia wants to speak with your team').closest('li');
    if (!row) throw new Error('row not found');
    await userEvent.click(within(row).getByRole('button', { name: 'Resolve' }));

    expect(await screen.findByRole('tab', { name: /Action needed\s*3/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('tab', { name: /Resolved/ }));
    expect(await within(list).findByText('Sofia wants to speak with your team')).toBeInTheDocument();
  });

  it('resolves selected notifications in one go', async () => {
    await renderApp('/notifications');
    const list = await notificationList();
    await within(list).findByText('Sofia wants to speak with your team');

    await userEvent.click(within(list).getByRole('checkbox', { name: 'Select all' }));
    await userEvent.click(within(list).getByRole('button', { name: 'Resolve selected' }));

    expect(await within(list).findByText("Nothing here. You're all caught up.")).toBeInTheDocument();
  });

  it('filters by type', async () => {
    await renderApp('/notifications');
    const list = await notificationList();
    await within(list).findByText('Sofia wants to speak with your team');

    await userEvent.click(screen.getByRole('button', { name: 'Hot leads' }));

    expect(within(list).getByText('Elena is a hot lead')).toBeInTheDocument();
    expect(within(list).queryByText('Sofia wants to speak with your team')).not.toBeInTheDocument();
  });
});
