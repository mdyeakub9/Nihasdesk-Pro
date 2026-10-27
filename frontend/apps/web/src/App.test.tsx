import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderApp } from './test/render-app';

function stubHealth(response: () => Promise<Response>) {
  const fetchMock = vi.fn(response);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

describe('App', () => {
  it('shows the system online and sends cookies with the health request', async () => {
    const fetchMock = stubHealth(async () => new Response('Healthy', { status: 200 }));

    await renderApp();

    expect(await screen.findByText('System online')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5080/health',
      expect.objectContaining({ credentials: 'include' }),
    );
  });

  it('shows the system offline with a retry button', async () => {
    stubHealth(async () => new Response(null, { status: 503 }));

    await renderApp();

    expect(await screen.findByText('System offline')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  describe('with a healthy API', () => {
    beforeEach(() => {
      stubHealth(async () => new Response('Healthy', { status: 200 }));
    });

    it('switches the UI to Italian', async () => {
      await renderApp();

      await userEvent.selectOptions(screen.getByLabelText('Language'), 'it');

      expect(await screen.findByRole('heading', { name: 'Benvenuto in Nihasdesk' })).toBeInTheDocument();
      expect(document.documentElement.lang).toBe('it');
    });

    it('switches to the dark theme', async () => {
      await renderApp();

      await userEvent.click(screen.getByRole('button', { name: 'Dark' }));

      expect(document.documentElement).toHaveClass('dark');
      expect(screen.getByRole('button', { name: 'Dark' })).toHaveAttribute('aria-pressed', 'true');
    });

    it('shows open counts in the navigation', async () => {
      await renderApp();

      const nav = screen.getByRole('navigation', { name: 'Main navigation' });
      expect(await within(nav).findByText('6')).toBeInTheDocument();
      expect(within(nav).getByRole('link', { name: /Inbox/ })).toHaveTextContent('Inbox6');
      expect(within(nav).getByRole('link', { name: /Notifications/ })).toHaveTextContent('Notifications4');
    });

    it('shows a placeholder for sections that are not built yet', async () => {
      await renderApp('/orders');

      expect(screen.getByRole('heading', { name: 'Orders' })).toBeInTheDocument();
      expect(screen.getByText('This section is coming soon.')).toBeInTheDocument();
    });

    it('shows not found for unknown routes', async () => {
      await renderApp('/missing');

      expect(screen.getByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
    });
  });
});
