import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { Role } from '../session/types';
import { callsTo, fakeApi, json, problem, testSession } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import type { Product, ProductImage, ProductSummary } from './api';
import { formatMoney, moneyInput, parseMoney } from './money';

const summary = (over: Partial<ProductSummary> = {}): ProductSummary => ({
  id: 'p-1',
  title: 'Classic Leather Bag',
  handle: 'classic-leather-bag',
  status: 'active',
  productType: 'Bag',
  category: 'Handbags',
  vendor: 'Almanaam',
  variantCount: 2,
  priceMin: 8900,
  priceMax: 9900,
  currency: 'EUR',
  stock: 16,
  updatedAt: '2026-10-28T10:00:00Z',
  thumbnailUrl: null,
  ...over,
});

const bag: Product = {
  id: 'p-1',
  title: 'Classic Leather Bag',
  handle: 'classic-leather-bag',
  description: 'Soft leather.',
  status: 'active',
  productType: 'Bag',
  category: 'Handbags',
  vendor: 'Almanaam',
  tags: ['leather'],
  optionNames: ['Colour'],
  variants: [
    {
      id: 'v-1',
      sku: 'BAG-BLK',
      barcode: null,
      option1: 'Black',
      option2: null,
      option3: null,
      price: 8900,
      compareAtPrice: null,
      cost: 4500,
      currency: 'EUR',
      trackInventory: true,
      stock: 12,
      inventoryPolicy: 'deny',
      weightGrams: 800,
      requiresShipping: true,
      available: true,
      imageId: null,
    },
  ],
  images: [],
  createdAt: '2026-10-28T10:00:00Z',
  updatedAt: '2026-10-28T10:00:00Z',
};

function catalogApi(role: Role = 'owner', extra: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    session: { ...testSession, role },
    ...extra,
    routes: {
      'GET /api/v1/products': () => json({ items: [summary()], nextCursor: null }),
      'GET /api/v1/products/p-1': () => json(bag),
      ...extra.routes,
    },
  });
}

const urlsTo = (fetchMock: ReturnType<typeof fakeApi>, path: string) =>
  fetchMock.mock.calls
    .map(([input]) => String(input))
    .filter((url) => new URL(url, 'http://x').pathname === path);

describe('Products', () => {
  it('lists products with price range and stock, and searches and filters through the API', async () => {
    const fetchMock = catalogApi();
    await renderApp('/products');

    const table = await screen.findByRole('table', { name: 'Products' });
    const row = within(table).getByRole('link', { name: 'Classic Leather Bag' }).closest('tr') as HTMLElement;
    expect(row).toHaveTextContent('€89.00 – €99.00');
    expect(row).toHaveTextContent('16 in stock');
    expect(screen.getByRole('link', { name: 'Add product' })).toHaveAttribute('href', '/products/new');

    await userEvent.click(screen.getByRole('button', { name: 'Draft' }));
    await userEvent.type(screen.getByRole('searchbox', { name: 'Search products' }), 'bag');
    await waitFor(() => expect(urlsTo(fetchMock, '/api/v1/products').at(-1)).toContain('status=draft'));
    await waitFor(() => expect(urlsTo(fetchMock, '/api/v1/products').at(-1)).toContain('q=bag'));
  });

  it('shows Members the catalog without the buttons to change it', async () => {
    catalogApi('member');
    await renderApp('/products');

    await screen.findByRole('table', { name: 'Products' });
    expect(screen.queryByRole('link', { name: 'Add product' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Import CSV' })).not.toBeInTheDocument();

    await renderApp('/products/p-1');
    expect(await screen.findByRole('note')).toHaveTextContent('Only Owners and Admins');
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
  });

  it('creates a product with prices in cents and opens it', async () => {
    const fetchMock = catalogApi('owner', {
      post: { '/api/v1/products': () => json({ ...bag, id: 'p-1', title: 'Silk Scarf' }, 201) },
    });
    await renderApp('/products/new');

    await userEvent.type(await screen.findByLabelText('Title'), 'Silk Scarf');
    await userEvent.type(screen.getByLabelText('Price'), '35,50');
    await userEvent.type(screen.getByLabelText('SKU'), 'SCARF-01');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(callsTo(fetchMock, '/api/v1/products').length).toBeGreaterThan(0));
    const body = callsTo(fetchMock, '/api/v1/products').find((c) => c.body)?.body as {
      title: string;
      status: string;
      optionNames: string[];
      variants: { price: number; sku: string; currency: string; stock: number | null }[];
    };
    expect(body).toMatchObject({ title: 'Silk Scarf', status: 'active', optionNames: [] });
    expect(body.variants).toEqual([
      expect.objectContaining({ price: 3550, sku: 'SCARF-01', currency: 'EUR', stock: null }),
    ]);
    expect(await screen.findByRole('heading', { name: 'Classic Leather Bag', level: 1 })).toBeInTheDocument();
  });

  it('edits options and variants, keeping existing variant IDs', async () => {
    const fetchMock = catalogApi('admin', { routes: { 'PUT /api/v1/products/p-1': () => json(bag) } });
    await renderApp('/products/p-1');

    const price = await screen.findByLabelText('Price');
    expect(price).toHaveValue('89.00');
    await userEvent.clear(price);
    await userEvent.type(price, '95');
    await userEvent.click(screen.getByRole('button', { name: 'Add variant' }));
    const second = screen.getByRole('listitem', { name: 'Variant 2' });
    await userEvent.type(within(second).getByLabelText('Colour'), 'Brown');
    await userEvent.type(within(second).getByLabelText('Price'), '95');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(callsTo(fetchMock, '/api/v1/products/p-1').some((c) => c.body)).toBe(true));
    const body = callsTo(fetchMock, '/api/v1/products/p-1').find((c) => c.body)?.body as {
      variants: { id?: string; option1: string; price: number; stock: number | null }[];
    };
    expect(body.variants).toEqual([
      expect.objectContaining({ id: 'v-1', option1: 'Black', price: 9500, stock: 12 }),
      expect.objectContaining({ option1: 'Brown', price: 9500 }),
    ]);
    expect(body.variants[1]).not.toHaveProperty('id');
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
  });

  it('checks money and numbers before sending, and shows what the server refused', async () => {
    const fetchMock = catalogApi('owner', {
      post: { '/api/v1/products': () => problem(409, 'catalog.sku_taken') },
    });
    await renderApp('/products/new');

    await userEvent.type(await screen.findByLabelText('Title'), 'Bag');
    await userEvent.type(screen.getByLabelText('Price'), 'ten');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText('Use an amount like 89.00.')).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/products').filter((c) => c.body)).toHaveLength(0);

    await userEvent.clear(screen.getByLabelText('Price'));
    await userEvent.type(screen.getByLabelText('Price'), '10');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('already used by another product');
  });
});

describe('CSV import', () => {
  const report = (over = {}) => ({
    applied: false,
    rows: 3,
    counts: { productsCreated: 2, productsUpdated: 0, variantsCreated: 3, variantsUpdated: 0 },
    issues: [],
    ...over,
  });

  it('previews a file, then imports it', async () => {
    const fetchMock = catalogApi('owner', {
      post: {
        '/api/v1/products/import/preview': () => json(report()),
        '/api/v1/products/import/apply': () => json(report({ applied: true })),
      },
    });
    await renderApp('/products/import');

    expect(await screen.findByRole('link', { name: 'Download template' })).toHaveAttribute(
      'href',
      '/api/v1/products/import/template',
    );
    const file = new File(['Handle,Title,Variant Price\nbag,Bag,1.00\n'], 'products.csv', {
      type: 'text/csv',
    });
    await userEvent.upload(screen.getByLabelText(/CSV file/), file);
    await userEvent.click(screen.getByRole('button', { name: 'Check file' }));

    expect(await screen.findByText('2 new products')).toBeInTheDocument();
    expect(screen.getByText('3 new variants')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Import' }));

    expect(await screen.findByRole('heading', { name: 'Import finished' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to products' })).toBeInTheDocument();
    const sent = fetchMock.mock.calls.find(([input]) => String(input).endsWith('/import/apply'))?.[1];
    expect(sent?.body).toBeInstanceOf(FormData);
    expect(new Headers(sent?.headers).get('X-XSRF-TOKEN')).toBe('test-csrf-token');
  });

  it('lists problems by line and column, and offers no import then', async () => {
    catalogApi('owner', {
      post: {
        '/api/v1/products/import/preview': () =>
          json(
            report({
              issues: [
                { line: 3, column: 'Variant Price', code: 'required', message: 'A price is required.' },
              ],
            }),
          ),
      },
    });
    await renderApp('/products/import');

    await userEvent.upload(
      await screen.findByLabelText(/CSV file/),
      new File(['x'], 'products.csv', { type: 'text/csv' }),
    );
    await userEvent.click(screen.getByRole('button', { name: 'Check file' }));

    const table = await screen.findByRole('table', { name: 'Problems in the file' });
    expect(within(table).getByText('Variant Price')).toBeInTheDocument();
    expect(within(table).getByText('A price is required.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Import' })).not.toBeInTheDocument();
  });

  it('explains files that are not CSV text', async () => {
    catalogApi('owner', {
      post: { '/api/v1/products/import/preview': () => problem(400, 'catalog.import_not_csv') },
    });
    await renderApp('/products/import');

    await userEvent.upload(
      await screen.findByLabelText(/CSV file/),
      new File(['PK'], 'products.csv', { type: 'text/csv' }),
    );
    await userEvent.click(screen.getByRole('button', { name: 'Check file' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Save it as CSV (UTF-8)');
  });
});

const photo = (id: string, position: number): ProductImage => ({
  id,
  position,
  status: 'saved',
  failureCode: null,
  width: 800,
  height: 600,
  url: `/api/v1/products/p-1/images/${id}`,
  thumbnailUrl: `/api/v1/products/p-1/images/${id}?size=thumbnail`,
});

const withPhotos: Product = { ...bag, images: [photo('img-1', 1), photo('img-2', 2)] };

describe('Product photos', () => {
  it('uploads photos at once and shows the first as the main one', async () => {
    const fetchMock = catalogApi('owner', {
      post: { '/api/v1/products/p-1/images': () => json(photo('img-1', 1), 201) },
    });
    await renderApp('/products/p-1');

    expect(await screen.findByText('No photos yet.')).toBeInTheDocument();
    await userEvent.upload(
      screen.getByLabelText('Choose photos'),
      new File(['jpeg'], 'bag.jpg', { type: 'image/jpeg' }),
    );

    const photos = await screen.findByRole('listitem', { name: 'Photo 1' });
    expect(within(photos).getByRole('img', { name: 'Photo 1' })).toHaveAttribute(
      'src',
      '/api/v1/products/p-1/images/img-1?size=thumbnail',
    );
    expect(within(photos).getByText('Main')).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/products/p-1/images')[0]?.body).toBeInstanceOf(FormData);
  });

  it('refuses other file types and files over 10 MB before sending them', async () => {
    const fetchMock = catalogApi('owner');
    await renderApp('/products/p-1');
    const user = userEvent.setup({ applyAccept: false });

    await user.upload(await screen.findByLabelText('Choose photos'), [
      new File(['gif'], 'cat.gif', { type: 'image/gif' }),
      new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'huge.jpg', { type: 'image/jpeg' }),
    ]);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('cat.gif: Only JPG, PNG or WebP photos.');
    expect(alert).toHaveTextContent('huge.jpg: Larger than 10 MB');
    expect(callsTo(fetchMock, '/api/v1/products/p-1/images')).toHaveLength(0);
  });

  it('shows the server’s reason when it refuses a photo', async () => {
    catalogApi('owner', {
      post: { '/api/v1/products/p-1/images': () => problem(400, 'catalog.image_type_not_allowed') },
    });
    await renderApp('/products/p-1');

    await userEvent.upload(
      await screen.findByLabelText('Choose photos'),
      new File(['MZ'], 'program.jpg', { type: 'image/jpeg' }),
    );

    expect(await screen.findByRole('alert')).toHaveTextContent('program.jpg: Only JPG, PNG or WebP photos.');
  });

  it('moves a photo and deletes one after asking', async () => {
    const fetchMock = catalogApi('admin', {
      routes: {
        'GET /api/v1/products/p-1': () => json(withPhotos),
        'PUT /api/v1/products/p-1/images/order': () => json([photo('img-2', 1), photo('img-1', 2)]),
        'DELETE /api/v1/products/p-1/images/img-2': () => new Response(null, { status: 204 }),
      },
    });
    await renderApp('/products/p-1');

    await userEvent.click(await screen.findByRole('button', { name: 'Move photo 1 later' }));
    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/products/p-1/images/order')[0]?.body).toEqual({
        imageIds: ['img-2', 'img-1'],
      }),
    );
    const main = await screen.findByRole('listitem', { name: 'Photo 1' });
    await waitFor(() =>
      expect(within(main).getByRole('img')).toHaveAttribute('src', expect.stringContaining('img-2')),
    );

    await userEvent.click(within(main).getByRole('button', { name: 'Delete photo 1' }));
    expect(callsTo(fetchMock, '/api/v1/products/p-1/images/img-2')).toHaveLength(0);
    await userEvent.click(within(main).getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(callsTo(fetchMock, '/api/v1/products/p-1/images/img-2')).toHaveLength(1));
  });

  it('gives a variant its own photo and sends the choice with the product', async () => {
    const fetchMock = catalogApi('owner', {
      routes: {
        'GET /api/v1/products/p-1': () => json(withPhotos),
        'PUT /api/v1/products/p-1': () => json(withPhotos),
      },
    });
    await renderApp('/products/p-1');

    await userEvent.selectOptions(await screen.findByLabelText('Photo'), 'Photo 2');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(callsTo(fetchMock, '/api/v1/products/p-1').some((c) => c.body)).toBe(true));
    const body = callsTo(fetchMock, '/api/v1/products/p-1').find((c) => c.body)?.body as {
      variants: { id: string; imageId: string | null }[];
    };
    expect(body.variants).toEqual([expect.objectContaining({ id: 'v-1', imageId: 'img-2' })]);
  });

  it('asks to save a new product first and lets Members only look', async () => {
    catalogApi('owner');
    await renderApp('/products/new');
    expect(await screen.findByText('Save the product first, then add its photos.')).toBeInTheDocument();

    catalogApi('member', { routes: { 'GET /api/v1/products/p-1': () => json(withPhotos) } });
    await renderApp('/products/p-1');
    expect(await screen.findByRole('listitem', { name: 'Photo 1' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add photos' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete photo 1' })).not.toBeInTheDocument();
  });

  it('shows the main photo in the product list', async () => {
    catalogApi('owner', {
      routes: {
        'GET /api/v1/products': () =>
          json({
            items: [summary({ thumbnailUrl: '/api/v1/products/p-1/images/img-1?size=thumbnail' })],
            nextCursor: null,
          }),
      },
    });
    await renderApp('/products');

    const table = await screen.findByRole('table', { name: 'Products' });
    const row = within(table).getByRole('link', { name: 'Classic Leather Bag' }).closest('tr') as HTMLElement;
    expect(row.querySelector('img')).toHaveAttribute(
      'src',
      '/api/v1/products/p-1/images/img-1?size=thumbnail',
    );
  });
});

describe('money', () => {
  it('reads and writes cents', () => {
    expect(parseMoney('89')).toBe(8900);
    expect(parseMoney('89.5')).toBe(8950);
    expect(parseMoney('89,50')).toBe(8950);
    expect(parseMoney('')).toBeNull();
    expect(parseMoney('ten')).toBeUndefined();
    expect(parseMoney('1.234')).toBeUndefined();
    expect(parseMoney('-1')).toBeUndefined();
    expect(moneyInput(8950)).toBe('89.50');
    expect(moneyInput(5)).toBe('0.05');
    expect(formatMoney(8950, 'EUR', 'en')).toBe('€89.50');
  });
});
