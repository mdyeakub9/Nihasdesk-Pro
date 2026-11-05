import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo, fakeApi, json, problem, testSession } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import type { Role } from '../session/types';
import type { KnowledgeSource } from './api';

const source = (over: Partial<KnowledgeSource> = {}): KnowledgeSource => ({
  id: 's-1',
  kind: 'pdf',
  title: 'Guida.pdf',
  origin: 'Guida.pdf',
  status: 'review',
  failureReason: null,
  sizeBytes: 1000,
  sectionCount: 12,
  pageCount: 8,
  warnings: [],
  lastReadAt: null,
  createdAt: '2026-11-05T10:00:00Z',
  ...over,
});

type Extra = NonNullable<Parameters<typeof fakeApi>[0]>;

function sourcesApi(sources: () => KnowledgeSource[], role: Role = 'owner', extra: Extra = {}) {
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
      'GET /api/v1/ai/sources': () => json(sources()),
      ...extra.routes,
    },
  });
}

const list = () => screen.findByRole('list', { name: 'Sources' });

describe('Settings → Shop knowledge → Documents and web pages', () => {
  it('shows status, sections and warnings, and says the AI does not use them yet', async () => {
    sourcesApi(() => [
      source({ warnings: ['pages_without_text:3,4', 'no_embeddings'] }),
      source({
        id: 's-2',
        kind: 'webpage',
        title: 'Spedizioni',
        origin: 'https://shop.example/spedizioni',
        pageCount: null,
        sectionCount: 1,
      }),
    ]);
    await renderApp('/settings/knowledge');

    const sources = await list();
    expect(await within(sources).findByText('Guida.pdf')).toBeInTheDocument();
    expect(within(sources).getByText('PDF · 8 pages · 12 sections')).toBeInTheDocument();
    expect(within(sources).getAllByText('Ready to review')).toHaveLength(2);
    expect(within(sources).getByText(/No text found on pages 3, 4/)).toBeInTheDocument();
    expect(within(sources).getByText(/only be found by exact words/)).toBeInTheDocument();
    expect(within(sources).getByText('Web page · 1 section')).toBeInTheDocument();
    expect(within(sources).getByText('https://shop.example/spedizioni')).toBeInTheDocument();
    expect(screen.getByText(/The AI doesn’t use them yet/)).toBeInTheDocument();
  });

  it('explains each failure in plain words', async () => {
    sourcesApi(() => [
      source({ status: 'failed', failureReason: 'scanned', sectionCount: 0, pageCount: null }),
      source({
        id: 's-2',
        title: 'Altro.pdf',
        status: 'failed',
        failureReason: 'something_new',
        sectionCount: 0,
        pageCount: null,
      }),
    ]);
    await renderApp('/settings/knowledge');

    const sources = await list();
    expect(await within(sources).findByText(/This PDF is a scan/)).toBeInTheDocument();
    // A code this screen doesn't know yet still reads as a sentence.
    expect(within(sources).getByText(/Something went wrong while reading/)).toBeInTheDocument();
    expect(within(sources).getAllByText('Failed')).toHaveLength(2);
    expect(within(sources).queryByText(/sections/)).not.toBeInTheDocument();
  });

  it('uploads a file and refreshes while it is being read', async () => {
    let current: KnowledgeSource[] = [];
    const fetchMock = sourcesApi(() => current, 'owner', {
      post: {
        '/api/v1/ai/sources/files': () => {
          current = [source({ status: 'reading', sectionCount: 0, pageCount: null })];
          return json(current[0]);
        },
      },
    });
    await renderApp('/settings/knowledge');
    expect(await screen.findByText('Nothing added yet.')).toBeInTheDocument();

    const file = new File(['%PDF-1.7'], 'Guida.pdf', { type: 'application/pdf' });
    await userEvent.upload(screen.getByLabelText('Upload a file'), file);

    expect(await within(await list()).findByText('Reading…')).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/ai/sources/files')).toHaveLength(1);

    // The Workers host finishes; the list notices without a reload.
    current = [source()];
    await waitFor(() => expect(screen.getByText('Ready to review')).toBeInTheDocument(), { timeout: 4500 });
  });

  it('refuses a file over 20 MB before sending it, and shows what the server refuses', async () => {
    const fetchMock = sourcesApi(() => [], 'owner', {
      post: { '/api/v1/ai/sources/files': () => problem(400, 'source.unsupported_type') },
    });
    await renderApp('/settings/knowledge');
    const input = await screen.findByLabelText('Upload a file');

    const huge = new File(['x'], 'enorme.pdf');
    Object.defineProperty(huge, 'size', { value: 20 * 1024 * 1024 + 1 });
    await userEvent.upload(input, huge);
    expect(await screen.findByText('The file is larger than 20 MB.')).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/ai/sources/files')).toHaveLength(0);

    await userEvent.upload(input, new File(['x'], 'finto.pdf'));
    expect(await screen.findByText('Upload a PDF, Word (.docx) or text file.')).toBeInTheDocument();
  });

  it('adds a web page and shows why an address is refused', async () => {
    const fetchMock = sourcesApi(() => [], 'owner', {
      post: {
        '/api/v1/ai/sources/web': (body) =>
          (body as { url: string }).url.includes('10.0.0.1')
            ? problem(400, 'source.blocked_url')
            : json(source()),
      },
    });
    await renderApp('/settings/knowledge');

    const address = await screen.findByLabelText('Web page address');
    await userEvent.type(address, 'http://10.0.0.1/');
    await userEvent.click(screen.getByRole('button', { name: 'Add page' }));
    expect(await screen.findByText('This address isn’t a public web page.')).toBeInTheDocument();
    expect(address).toHaveValue('http://10.0.0.1/');

    await userEvent.clear(address);
    await userEvent.type(address, 'https://shop.example/resi');
    await userEvent.click(screen.getByRole('button', { name: 'Add page' }));
    await waitFor(() => expect(address).toHaveValue(''));
    expect(callsTo(fetchMock, '/api/v1/ai/sources/web').at(-1)?.body).toEqual({
      url: 'https://shop.example/resi',
    });
  });

  it('asks before deleting', async () => {
    let current = [source()];
    const fetchMock = sourcesApi(() => current, 'owner', {
      routes: {
        'DELETE /api/v1/ai/sources/s-1': () => {
          current = [];
          return new Response(null, { status: 204 });
        },
      },
    });
    await renderApp('/settings/knowledge');

    await userEvent.click(await within(await list()).findByRole('button', { name: 'Delete Guida.pdf' }));
    expect(callsTo(fetchMock, '/api/v1/ai/sources/s-1')).toHaveLength(0);
    expect(screen.getByText('Delete this source and all its sections?')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByText('Delete this source and all its sections?')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Delete Guida.pdf' }));
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }));
    expect(await screen.findByText('Nothing added yet.')).toBeInTheDocument();
  });

  it('members see the sources but can not add or delete', async () => {
    sourcesApi(() => [source()], 'member');
    await renderApp('/settings/knowledge');

    expect(await within(await list()).findByText('Guida.pdf')).toBeInTheDocument();
    expect(screen.queryByLabelText('Upload a file')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Web page address')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete Guida.pdf' })).not.toBeInTheDocument();
  });
});
