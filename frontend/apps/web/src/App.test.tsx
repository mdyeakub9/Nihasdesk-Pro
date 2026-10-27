import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { renderApp } from './test/render-app';

function stubHealth(response: () => Promise<Response>) {
  const fetchMock = vi.fn(response);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

describe('App', () => {
  it('shows a healthy API and sends cookies with the request', async () => {
    const fetchMock = stubHealth(async () => new Response('Healthy', { status: 200 }));

    await renderApp();

    expect(await screen.findByText('API: Healthy')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      'http://localhost:5080/health',
      expect.objectContaining({ credentials: 'include' }),
    );
  });

  it('shows an unreachable API with a retry button', async () => {
    stubHealth(async () => new Response(null, { status: 503 }));

    await renderApp();

    expect(await screen.findByText('API: Unreachable')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('switches the UI to Italian', async () => {
    stubHealth(async () => new Response('Healthy', { status: 200 }));
    await renderApp();

    await userEvent.selectOptions(screen.getByLabelText('Language'), 'it');

    expect(await screen.findByRole('heading', { name: 'Benvenuto in Nihasdesk' })).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('it');
  });

  it('shows not found for unknown routes', async () => {
    stubHealth(async () => new Response('Healthy', { status: 200 }));

    await renderApp('/missing');

    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
  });
});
