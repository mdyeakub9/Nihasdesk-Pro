import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo, fakeApi, json, problem, testSession } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import type { Role } from '../session/types';
import type { KnowledgeSite, SitePage } from './api';

const page = (path: string, title: string, over: Partial<SitePage> = {}): SitePage => ({
  url: `https://shop.example${path}`,
  title,
  chosen: true,
  sourceId: null,
  sourceStatus: null,
  ...over,
});

const site = (over: Partial<KnowledgeSite> = {}): KnowledgeSite => ({
  id: 'w-1',
  host: 'shop.example',
  status: 'ready',
  failureReason: null,
  pages: [
    page('/', 'Home'),
    page('/spedizioni', 'Spedizioni'),
    page('/lookbook', '<b>Lookbook</b>', { chosen: false }),
  ],
  createdAt: '2026-11-09T10:00:00Z',
  ...over,
});

type Extra = NonNullable<Parameters<typeof fakeApi>[0]>;

function sitesApi(sites: () => KnowledgeSite[], role: Role = 'owner', extra: Extra = {}) {
  return fakeApi({
    session: { ...testSession, role },
    ...extra,
    routes: {
      'GET /api/v1/shipping/zones': () => json([]),
      'GET /api/v1/ai/policies': () =>
        json({
          shippingNotes: null,
          returnDays: null,
          returnPolicy: null,
          paymentMethods: ['cash_on_delivery'],
          paymentNotes: null,
          contactPhone: null,
          contactEmail: null,
          contactAddress: null,
          openingHours: null,
        }),
      'GET /api/v1/ai/answers': () => json([]),
      'GET /api/v1/ai/sources': () => json([]),
      'GET /api/v1/ai/sites': () => json(sites()),
      ...extra.routes,
    },
  });
}

const list = () => screen.findByRole('list', { name: 'Websites' });

describe('Settings → Shop knowledge → Website', () => {
  it('finds a site only after the owner confirms it is theirs', async () => {
    let current: KnowledgeSite[] = [];
    const fetchMock = sitesApi(() => current, 'owner', {
      post: {
        '/api/v1/ai/sites': () => {
          current = [site({ status: 'finding', pages: [] })];
          return json(current[0]);
        },
      },
    });
    await renderApp('/settings/knowledge');

    await userEvent.type(await screen.findByLabelText('Website address'), ' shop.example ');
    const find = screen.getByRole('button', { name: 'Find pages' });
    expect(find).toBeDisabled();

    await userEvent.click(screen.getByLabelText(/This website is mine/));
    await userEvent.click(find);

    expect(callsTo(fetchMock, '/api/v1/ai/sites').find((c) => c.body)?.body).toEqual({
      domain: 'shop.example',
      confirmed: true,
    });
    expect(await within(await list()).findByText('Looking for pages…')).toBeInTheDocument();
  });

  it('shows the main pages ticked and adds what is ticked; other pages stay unticked', async () => {
    let current = [site()];
    const fetchMock = sitesApi(() => current, 'owner', {
      post: {
        '/api/v1/ai/sites/w-1/pages': () => {
          current = [
            site({
              pages: [
                page('/', 'Home', { sourceId: 's-1', sourceStatus: 'reading' }),
                page('/spedizioni', 'Spedizioni'),
                page('/lookbook', '<b>Lookbook</b>', { chosen: false }),
              ],
            }),
          ];
          return json({ added: 1, alreadyAdded: 0 });
        },
      },
    });
    await renderApp('/settings/knowledge');

    const sites = await list();
    const main = await within(sites).findByRole('list', { name: 'Main pages' });
    expect(within(main).getByLabelText(/Home/)).toBeChecked();
    expect(within(main).getByLabelText(/Spedizioni/)).toBeChecked();
    // Text from the website is plain text.
    expect(within(sites).getByText('<b>Lookbook</b>')).toBeInTheDocument();
    expect(within(sites).getByLabelText(/Lookbook/)).not.toBeChecked();

    await userEvent.click(within(main).getByLabelText(/Spedizioni/));
    await userEvent.click(within(sites).getByRole('button', { name: 'Add 1 page' }));

    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/ai/sites/w-1/pages')[0]?.body).toEqual({
        urls: ['https://shop.example/'],
      }),
    );
    expect(await within(main).findByText('Reading…')).toBeInTheDocument();
  });

  it('uses all read pages and says how many differences the settings have first', async () => {
    let current = [
      site({
        pages: [page('/spedizioni', 'Spedizioni', { sourceId: 's-1', sourceStatus: 'review' })],
      }),
    ];
    const fetchMock = sitesApi(() => current, 'owner', {
      routes: { 'GET /api/v1/ai/sites/w-1/differences': () => json({ count: 2 }) },
      post: {
        '/api/v1/ai/sites/w-1/use': () => {
          current = [
            site({
              pages: [page('/spedizioni', 'Spedizioni', { sourceId: 's-1', sourceStatus: 'live' })],
            }),
          ];
          return json({ used: 1, notReady: 0 });
        },
      },
    });
    await renderApp('/settings/knowledge');

    const sites = await list();
    expect(await within(sites).findByText(/2 differences with your settings/)).toBeInTheDocument();
    await userEvent.click(within(sites).getByRole('button', { name: 'Use all for AI' }));

    expect(await within(sites).findByText('Live')).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/ai/sites/w-1/use')).toHaveLength(1);
    expect(within(sites).queryByRole('button', { name: 'Use all for AI' })).not.toBeInTheDocument();
  });

  it('explains why a site failed in plain words', async () => {
    sitesApi(() => [site({ status: 'failed', failureReason: 'site_robots_forbid', pages: [] })]);
    await renderApp('/settings/knowledge');

    expect(await within(await list()).findByText(/doesn’t allow automatic reading/)).toBeInTheDocument();
  });

  it('deletes a site with its pages after confirming', async () => {
    let current = [site()];
    sitesApi(() => current, 'owner', {
      routes: {
        'DELETE /api/v1/ai/sites/w-1': () => {
          current = [];
          return new Response(null, { status: 204 });
        },
      },
    });
    await renderApp('/settings/knowledge');

    await userEvent.click(await within(await list()).findByRole('button', { name: 'Delete shop.example' }));
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(screen.queryByRole('list', { name: 'Websites' })).not.toBeInTheDocument());
  });

  it('shows a member the sites but no way to change them', async () => {
    sitesApi(() => [site()], 'member');
    await renderApp('/settings/knowledge');

    const sites = await list();
    await within(sites).findByText('shop.example');
    expect(screen.queryByLabelText('Website address')).not.toBeInTheDocument();
    expect(within(sites).queryByRole('button', { name: /Add/ })).not.toBeInTheDocument();
    expect(within(sites).queryByRole('button', { name: /Delete/ })).not.toBeInTheDocument();
    expect(within(sites).getByLabelText(/Home/)).toBeDisabled();
  });

  it('shows the plain reason when a site can’t be added', async () => {
    sitesApi(() => [], 'owner', {
      post: {
        '/api/v1/ai/sites': () => problem(409, 'site.duplicate'),
      },
    });
    await renderApp('/settings/knowledge');

    await userEvent.type(await screen.findByLabelText('Website address'), 'shop.example');
    await userEvent.click(screen.getByLabelText(/This website is mine/));
    await userEvent.click(screen.getByRole('button', { name: 'Find pages' }));

    expect(await screen.findByText('This website was already added.')).toBeInTheDocument();
  });
});
