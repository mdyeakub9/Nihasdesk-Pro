import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo, fakeApi, json, problem, testSession } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import type { Role } from '../session/types';
import type { KnowledgeSource, ReviewConflict, ReviewSection, SourceReview } from './api';

const source = (over: Partial<KnowledgeSource> = {}): KnowledgeSource => ({
  id: 's-1',
  kind: 'pdf',
  title: 'Guida.pdf',
  origin: 'Guida.pdf',
  status: 'review',
  failureReason: null,
  sizeBytes: 1000,
  sectionCount: 2,
  pageCount: 2,
  warnings: [],
  lastReadAt: null,
  createdAt: '2026-11-05T10:00:00Z',
  ...over,
});

const section = (over: Partial<ReviewSection> = {}): ReviewSection => ({
  id: 'x-1',
  position: 0,
  headingPath: 'Guida › Spedizioni',
  page: 1,
  kind: 'text',
  text: 'La spedizione costa 5 €.',
  excluded: false,
  searchable: true,
  ...over,
});

const shippingConflict: ReviewConflict = {
  kind: 'shipping_cost',
  sectionId: 'x-1',
  page: 1,
  found: '5.00 EUR',
  expected: 'Italia: 4.90 EUR',
};

type Extra = NonNullable<Parameters<typeof fakeApi>[0]>;

function reviewApi(
  review: () => SourceReview,
  sections: () => ReviewSection[],
  role: Role = 'owner',
  extra: Extra = {},
) {
  return fakeApi({
    session: { ...testSession, role },
    ...extra,
    routes: {
      'GET /api/v1/ai/sources/s-1': () => json(review()),
      'GET /api/v1/ai/sources/s-1/sections': () => json({ items: sections(), nextAfter: null }),
      ...extra.routes,
    },
  });
}

const reviewOf = (over: Partial<SourceReview> = {}): SourceReview => ({
  source: source(),
  conflicts: [],
  priceListSections: [],
  ...over,
});

const open = () => renderApp('/settings/knowledge/sources/s-1');
const sectionsList = () => screen.findByRole('list', { name: /sections/ });

describe('Reviewing a source', () => {
  it('shows warnings, conflicts and every section with its heading and page', async () => {
    reviewApi(
      () =>
        reviewOf({
          source: source({ warnings: ['pages_without_text:3'] }),
          conflicts: [shippingConflict],
          priceListSections: ['x-2'],
        }),
      () => [
        section(),
        section({ id: 'x-2', position: 1, headingPath: null, page: 2, kind: 'table', text: 'Zona | Costo' }),
      ],
    );
    await open();

    expect(await screen.findByRole('heading', { name: 'Guida.pdf', level: 1 })).toBeInTheDocument();
    expect(screen.getByText(/No text found on pages 3/)).toBeInTheDocument();
    expect(screen.getByText(/looks like a price list/)).toBeInTheDocument();
    expect(
      screen.getByText(/This says 5\.00 EUR, but your shipping zones say Italia: 4\.90 EUR\. \(page 1\)/),
    ).toBeInTheDocument();

    const list = await sectionsList();
    expect(await within(list).findByText('La spedizione costa 5 €.')).toBeInTheDocument();
    expect(within(list).getByText('Guida › Spedizioni')).toBeInTheDocument();
    expect(within(list).getByText('Text · Page 1')).toBeInTheDocument();
    expect(within(list).getByText('Table · Page 2')).toBeInTheDocument();
    expect(within(list).getByText('No heading')).toBeInTheDocument();
  });

  it('shows imported text as plain text, never as HTML', async () => {
    const attack = '<img src=x onerror="alert(1)"><b>grassetto</b>';
    reviewApi(reviewOf, () => [section({ text: attack })]);
    await open();

    const list = await sectionsList();
    expect(await within(list).findByText(attack)).toBeInTheDocument();
    expect(list.querySelector('img')).toBeNull();
    expect(list.querySelector('b')).toBeNull();
  });

  it('edits a section and refreshes the warnings', async () => {
    let conflicts = [shippingConflict];
    let current = section();
    const fetchMock = reviewApi(
      () => reviewOf({ conflicts }),
      () => [current],
      'owner',
      {
        routes: {
          'PUT /api/v1/ai/sources/s-1/sections/x-1': (body) => {
            current = section({ text: (body as { text: string }).text, searchable: false });
            conflicts = [];
            return json(current);
          },
        },
      },
    );
    await open();

    const list = await sectionsList();
    await userEvent.click(await within(list).findByRole('button', { name: 'Edit' }));
    const field = within(list).getByRole('textbox', { name: 'Section text' });
    expect(field).toHaveValue('La spedizione costa 5 €.');
    await userEvent.clear(field);
    expect(within(list).getByRole('button', { name: 'Save' })).toBeDisabled();
    await userEvent.type(field, 'La spedizione costa 4,90 €.');
    await userEvent.click(within(list).getByRole('button', { name: 'Save' }));

    expect(await within(list).findByText('La spedizione costa 4,90 €.')).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/ai/sources/s-1/sections/x-1')[0]?.body).toEqual({
      text: 'La spedizione costa 4,90 €.',
    });
    await waitFor(() => expect(screen.queryByText(/This says 5\.00 EUR/)).not.toBeInTheDocument());
    // The embedding could not be made: said plainly.
    expect(within(list).getByText(/found only by exact words/)).toBeInTheDocument();
  });

  it('a conflict leads to the section or the settings, or excludes the section', async () => {
    let conflicts = [shippingConflict];
    let current = section();
    const fetchMock = reviewApi(
      () => reviewOf({ conflicts }),
      () => [current],
      'owner',
      {
        post: {
          '/api/v1/ai/sources/s-1/sections/x-1/exclude': () => {
            current = section({ excluded: true });
            conflicts = [];
            return json(current);
          },
        },
      },
    );
    await open();

    const heading = await screen.findByRole('heading', { name: /Differs from your settings/ });
    const box = within(heading.parentElement as HTMLElement);
    expect(box.getByRole('link', { name: 'Fix settings' })).toHaveAttribute('href', '/settings/knowledge');

    await userEvent.click(box.getByRole('button', { name: 'Fix section' }));
    const list = await sectionsList();
    expect(await within(list).findByRole('textbox', { name: 'Section text' })).toHaveValue(
      'La spedizione costa 5 €.',
    );
    await userEvent.click(within(list).getByRole('button', { name: 'Cancel' }));

    await userEvent.click(box.getByRole('button', { name: 'Exclude' }));
    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/ai/sources/s-1/sections/x-1/exclude')).toHaveLength(1),
    );
    expect(await within(list).findByText('Excluded')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText(/This says 5\.00 EUR/)).not.toBeInTheDocument());
    expect(within(list).getByRole('button', { name: 'Use again' })).toBeInTheDocument();
  });

  it('has no fix-settings link for a payment method, which has no setting yet', async () => {
    reviewApi(
      () =>
        reviewOf({
          conflicts: [
            {
              kind: 'payment_method',
              sectionId: 'x-1',
              page: null,
              found: 'PayPal',
              expected: 'cash on delivery',
            },
          ],
        }),
      () => [section()],
    );
    await open();

    expect(await screen.findByText(/This mentions PayPal/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Fix settings' })).not.toBeInTheDocument();
  });

  it('merges with the next section, but the last one has nothing to merge with', async () => {
    const fetchMock = reviewApi(
      reviewOf,
      () => [section(), section({ id: 'x-2', position: 1, text: 'Seconda sezione' })],
      'owner',
      { post: { '/api/v1/ai/sources/s-1/sections/x-1/merge-next': () => json(section()) } },
    );
    await open();

    const list = await sectionsList();
    const merges = await within(list).findAllByRole('button', { name: 'Merge with next' });
    expect(merges).toHaveLength(1);
    await userEvent.click(merges[0] as HTMLElement);
    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/ai/sources/s-1/sections/x-1/merge-next')).toHaveLength(1),
    );
  });

  it('loads further pages, also to reach a section a conflict points at', async () => {
    const fetchMock = fakeApi({
      session: testSession,
      routes: {
        'GET /api/v1/ai/sources/s-1': () =>
          json(reviewOf({ conflicts: [{ ...shippingConflict, sectionId: 'x-far' }] })),
        'GET /api/v1/ai/sources/s-1/sections': (_body, url) =>
          url.searchParams.get('after') === '99'
            ? json({ items: [section({ id: 'x-far', position: 100, text: 'Lontana' })], nextAfter: null })
            : json({ items: [section()], nextAfter: 99 }),
      },
    });
    await open();

    expect(await screen.findByRole('button', { name: 'Load more' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Fix section' }));

    expect(await screen.findByRole('textbox', { name: 'Section text' })).toHaveValue('Lontana');
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalled();
  });

  it('members read but do not change anything', async () => {
    reviewApi(
      () => reviewOf({ conflicts: [shippingConflict] }),
      () => [section()],
      'member',
    );
    await open();

    expect(await within(await sectionsList()).findByText('La spedizione costa 5 €.')).toBeInTheDocument();
    expect(screen.getByText(/This says 5\.00 EUR/)).toBeInTheDocument();
    for (const name of ['Edit', 'Exclude', 'Fix section', 'Merge with next']) {
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument();
    }
  });

  it('says so when the source does not exist and the list links here', async () => {
    fakeApi({
      session: testSession,
      routes: {
        'GET /api/v1/ai/sources/s-1': () => problem(404, 'source.not_found'),
        'GET /api/v1/ai/sources/s-1/sections': () => problem(404, 'source.not_found'),
      },
    });
    await open();

    expect(await screen.findByRole('alert')).toHaveTextContent('This source no longer exists.');
    expect(screen.getByRole('link', { name: 'Back to Shop knowledge' })).toHaveAttribute(
      'href',
      '/settings/knowledge',
    );
  });

  it('puts a source in use and takes it out again', async () => {
    let status: KnowledgeSource['status'] = 'review';
    const api = reviewApi(
      () => reviewOf({ source: source({ status }), conflicts: [shippingConflict] }),
      () => [section()],
      'owner',
      {
        routes: {
          'POST /api/v1/ai/sources/s-1/use': () => {
            status = 'live';
            return json(source({ status }));
          },
          'POST /api/v1/ai/sources/s-1/stop': () => {
            status = 'review';
            return json(source({ status }));
          },
        },
      },
    );
    await open();

    expect(await screen.findByText(/1 difference with your settings/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Use for AI' }));
    const stop = await screen.findByRole('button', { name: 'Stop using' });
    expect(callsTo(api, '/api/v1/ai/sources/s-1/use')).toHaveLength(1);
    expect(screen.getByText('The AI answers customers from this source.')).toBeInTheDocument();
    expect(screen.queryByText(/1 difference with your settings/)).not.toBeInTheDocument();

    await userEvent.click(stop);
    expect(await screen.findByRole('button', { name: 'Use for AI' })).toBeInTheDocument();
    expect(callsTo(api, '/api/v1/ai/sources/s-1/stop')).toHaveLength(1);
  });

  it('tests a question against this source and shows what it finds as plain text', async () => {
    const api = reviewApi(
      () => reviewOf(),
      () => [section()],
      'admin',
      {
        routes: {
          'POST /api/v1/ai/sources/search': () =>
            json([
              {
                sectionId: 'x-1',
                sourceId: 's-1',
                sourceTitle: 'Guida.pdf',
                headingPath: 'Guida › Ordini',
                page: 4,
                text: '<b>No</b>, due ordini restano separati.',
                similarity: 0.6,
              },
            ]),
        },
      },
    );
    await open();

    await userEvent.type(await screen.findByLabelText('Customer’s question'), '  can i combine two orders ');
    await userEvent.click(screen.getByRole('button', { name: 'Test' }));

    const found = await screen.findByRole('list', { name: 'Sections found' });
    expect(within(found).getByText('<b>No</b>, due ordini restano separati.')).toBeInTheDocument();
    expect(within(found).getByText(/Guida › Ordini/)).toBeInTheDocument();
    expect(callsTo(api, '/api/v1/ai/sources/search')[0]?.body).toEqual({
      query: 'can i combine two orders',
      sourceId: 's-1',
    });
  });

  it('says so when a question finds nothing', async () => {
    reviewApi(
      () => reviewOf(),
      () => [section()],
      'owner',
      {
        routes: { 'POST /api/v1/ai/sources/search': () => json([]) },
      },
    );
    await open();

    await userEvent.type(await screen.findByLabelText('Customer’s question'), 'orari');
    await userEvent.click(screen.getByRole('button', { name: 'Test' }));

    expect(await screen.findByText(/Nothing close enough was found/)).toBeInTheDocument();
  });

  it.each(['member'] as Role[])('hides using and testing from a %s', async (role) => {
    reviewApi(
      () => reviewOf(),
      () => [section()],
      role,
    );
    await open();

    await sectionsList();
    expect(screen.queryByRole('button', { name: 'Use for AI' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Customer’s question')).not.toBeInTheDocument();
  });

  it('offers neither for a source that could not be read', async () => {
    reviewApi(
      () => reviewOf({ source: source({ status: 'failed', failureReason: 'scanned' }) }),
      () => [],
      'owner',
    );
    await open();

    await screen.findByRole('heading', { name: 'Guida.pdf', level: 1 });
    expect(screen.queryByRole('button', { name: 'Use for AI' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Customer’s question')).not.toBeInTheDocument();
  });
});
