import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Role } from '../session/types';
import { callsTo, fakeApi, json, problem, testSession } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import { isFacebookLoginUrl } from './api';
import { leaveTo } from './navigate';

vi.mock('./navigate', () => ({ leaveTo: vi.fn() }));

const loginUrl = 'https://www.facebook.com/v24.0/dialog/oauth?client_id=1&state=abc';
const page = {
  id: 'c-1',
  kind: 'messenger',
  name: 'Bottega Verde',
  externalAccountId: '1001',
  connectedAt: '2026-10-28T10:00:00Z',
};

function channelsApi(role: Role = 'owner', extra: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    session: { ...testSession, role },
    ...extra,
    routes: { 'GET /api/v1/channels': () => json([]), ...extra.routes },
  });
}

beforeEach(() => vi.mocked(leaveTo).mockClear());

describe('Settings → Channels', () => {
  it('sends the Owner to Facebook Login to connect a Page', async () => {
    channelsApi('owner', { post: { '/api/v1/channels/meta/connect': () => json({ url: loginUrl }) } });
    await renderApp('/settings/channels');

    await userEvent.click(await screen.findByRole('button', { name: 'Connect a Facebook Page' }));
    await waitFor(() => expect(leaveTo).toHaveBeenCalledWith(loginUrl));
    expect(screen.getByRole('link', { name: 'Channels' })).toBeInTheDocument();
  });

  it('never leaves for anything but Facebook Login', async () => {
    channelsApi('owner', {
      post: { '/api/v1/channels/meta/connect': () => json({ url: 'https://evil.example/dialog/oauth' }) },
    });
    await renderApp('/settings/channels');

    await userEvent.click(await screen.findByRole('button', { name: 'Connect a Facebook Page' }));
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(leaveTo).not.toHaveBeenCalled();
  });

  it('lists connected Pages and disconnects after confirming', async () => {
    let pages = [page];
    const fetchMock = channelsApi('admin', {
      routes: {
        'GET /api/v1/channels': () => json(pages),
        'DELETE /api/v1/channels/c-1': () => {
          pages = [];
          return new Response(null, { status: 204 });
        },
      },
    });
    await renderApp('/settings/channels');

    const list = await screen.findByRole('list', { name: 'Connected Pages' });
    expect(within(list).getByText('Bottega Verde')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Connect another Page' })).toBeInTheDocument();

    await userEvent.click(within(list).getByRole('button', { name: 'Disconnect Bottega Verde' }));
    expect(callsTo(fetchMock, '/api/v1/channels/c-1')).toHaveLength(0);
    await userEvent.click(within(list).getByRole('button', { name: 'Yes, disconnect' }));

    await waitFor(() =>
      expect(screen.queryByRole('list', { name: 'Connected Pages' })).not.toBeInTheDocument(),
    );
    expect(callsTo(fetchMock, '/api/v1/channels/c-1')).toHaveLength(1);
  });

  it('explains what came back from Facebook', async () => {
    channelsApi('owner', { routes: { 'GET /api/v1/channels': () => json([page]) } });
    await renderApp('/settings/channels?connected=1&elsewhere=1');

    const status = await screen.findByRole('status', { name: 'Connection result' });
    expect(status).toHaveTextContent('1 Page connected.');
    expect(status).toHaveTextContent('already connected to another Nihasdesk workspace');
    await userEvent.click(within(status).getByRole('button', { name: 'Dismiss' }));
    expect(screen.queryByRole('status', { name: 'Connection result' })).not.toBeInTheDocument();
  });

  it('shows failures from the callback and the API', async () => {
    channelsApi('owner', {
      post: { '/api/v1/channels/meta/connect': () => problem(400, 'channel.not_configured') },
    });
    await renderApp('/settings/channels?error=invalid_state');

    expect(await screen.findByRole('status', { name: 'Connection result' })).toHaveTextContent('expired');
    await userEvent.click(screen.getByRole('button', { name: 'Connect a Facebook Page' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('set up on this server');
  });

  it('is hidden from Members, who only see a note', async () => {
    const fetchMock = channelsApi('member');
    await renderApp('/settings/channels');

    expect(await screen.findByRole('note')).toHaveTextContent('Only Owners and Admins');
    expect(screen.queryByRole('link', { name: 'Channels' })).not.toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/channels')).toHaveLength(0);
  });
});

describe('isFacebookLoginUrl', () => {
  it('accepts only https://www.facebook.com/…/dialog/oauth', () => {
    expect(isFacebookLoginUrl(loginUrl)).toBe(true);
    expect(isFacebookLoginUrl('http://www.facebook.com/v24.0/dialog/oauth')).toBe(false);
    expect(isFacebookLoginUrl('https://www.facebook.com.evil.example/dialog/oauth')).toBe(false);
    expect(isFacebookLoginUrl('https://www.facebook.com/somewhere/else')).toBe(false);
    expect(isFacebookLoginUrl('javascript:alert(1)')).toBe(false);
  });
});
