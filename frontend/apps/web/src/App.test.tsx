import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it } from 'vitest';
import { fakeApi } from './test/fake-api';
import { renderApp } from './test/render-app';
import { fakeInboxApi } from './test/inbox-api';

describe('App', () => {
  it('shows the system online and sends cookies with the health request', async () => {
    const fetchMock = fakeApi();

    await renderApp();

    expect(await screen.findByText('System online')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith('/health', expect.objectContaining({ credentials: 'include' }));
  });

  it('shows the system offline with a retry button', async () => {
    fakeApi({ health: () => new Response(null, { status: 503 }) });

    await renderApp();

    expect(await screen.findByText('System offline')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  describe('with a healthy API', () => {
    beforeEach(() => {
      fakeApi();
    });

    it('switches the UI to Italian', async () => {
      await renderApp();

      await userEvent.selectOptions(await screen.findByLabelText('Language'), 'it');

      expect(await screen.findByRole('heading', { name: 'Benvenuto in Nihasdesk' })).toBeInTheDocument();
      expect(document.documentElement.lang).toBe('it');
    });

    it('switches to the dark theme', async () => {
      await renderApp();

      await userEvent.click(await screen.findByRole('button', { name: 'Dark' }));

      expect(document.documentElement).toHaveClass('dark');
      expect(screen.getByRole('button', { name: 'Dark' })).toHaveAttribute('aria-pressed', 'true');
    });

    it('shows open counts in the navigation', async () => {
      fakeInboxApi();
      await renderApp();

      const nav = await screen.findByRole('navigation', { name: 'Main navigation' });
      // Six open conversations in the fake inbox; notifications come from the test fixture.
      expect(await within(nav).findByText('6')).toBeInTheDocument();
      expect(within(nav).getByRole('link', { name: /Inbox/ })).toHaveTextContent('Inbox6');
      expect(within(nav).getByRole('link', { name: /Notifications/ })).toHaveTextContent('Notifications4');
    });

    it('shows a placeholder for sections that are not built yet', async () => {
      await renderApp('/leads');

      expect(await screen.findByRole('heading', { name: 'Leads' })).toBeInTheDocument();
      expect(screen.getByText('This section is coming soon.')).toBeInTheDocument();
    });

    it('shows not found for unknown routes', async () => {
      await renderApp('/missing');

      expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
    });
  });
});
