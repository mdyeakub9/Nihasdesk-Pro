import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo, fakeApi, json, problem } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import { hub } from '../../test/signalr-mock';
import type { Order, OrderSummary } from './api';

const request: Order = {
  id: 'o-1',
  number: 1001,
  status: 'pending',
  source: 'ai',
  conversationId: 'c-giulia',
  customerName: 'Giulia Rossi',
  phone: '+39 333 123 4567',
  street: 'Via Roma 12',
  postalCode: '07100',
  city: 'Sassari',
  province: 'SS',
  country: 'IT',
  notes: 'Citofono Rossi',
  shippingZoneId: 'z-sar',
  shippingZoneName: 'Sardegna',
  shippingCost: 700,
  subtotal: 17800,
  total: 18500,
  currency: 'EUR',
  paymentMethod: 'cod',
  items: [
    {
      productId: 'p-bag',
      variantId: 'v-nero',
      title: 'Borsa nera',
      variant: 'Nero',
      sku: 'BN-1',
      unitPrice: 8900,
      quantity: 2,
      lineTotal: 17800,
    },
  ],
  createdAt: new Date(Date.now() - 5 * 60_000).toISOString(),
  decidedAt: null,
  rejectReason: null,
};

const summary = (o: Order): OrderSummary => ({
  id: o.id,
  number: o.number,
  status: o.status,
  source: o.source,
  customerName: o.customerName,
  total: o.total,
  currency: o.currency,
  createdAt: o.createdAt,
  itemCount: 2,
});

function fakeOrders(options: { approve?: () => Response } = {}) {
  let order = { ...request };
  const fetchMock = fakeApi({
    routes: {
      'GET /api/v1/orders': (_body, url) =>
        json({
          items: url.searchParams.get('status') === order.status ? [summary(order)] : [],
          nextCursor: null,
        }),
      'GET /api/v1/orders/counts': () =>
        json({
          pending: order.status === 'pending' ? 1 : 0,
          confirmed: order.status === 'confirmed' ? 1 : 0,
          rejected: 0,
        }),
      'GET /api/v1/orders/o-1': () => json(order),
      'POST /api/v1/orders/o-1/approve': () => {
        if (options.approve) return options.approve();
        order = { ...order, status: 'confirmed', decidedAt: new Date().toISOString() };
        return json(order);
      },
      'POST /api/v1/orders/o-1/reject': (body) => {
        order = { ...order, status: 'rejected', rejectReason: (body as { reason: string }).reason };
        return json(order);
      },
      'PUT /api/v1/orders/o-1': (body) => {
        order = { ...order, notes: (body as { notes: string | null }).notes };
        return json(order);
      },
      'POST /api/v1/orders/quote': () =>
        json({
          items: [],
          shippingZoneName: 'Sardegna',
          shippingCost: 700,
          subtotal: 17800,
          total: 18500,
          currency: 'EUR',
        }),
      'GET /api/v1/shipping/zones': () =>
        json([
          {
            id: 'z-sar',
            name: 'Sardegna',
            areas: null,
            price: 700,
            currency: 'EUR',
            freeOver: null,
            minDays: 2,
            maxDays: 4,
          },
        ]),
      'GET /api/v1/products': () => json({ items: [], nextCursor: null }),
    },
  });
  return fetchMock;
}

describe('Orders', () => {
  it('lists pending AI requests with counts and switches tabs', async () => {
    fakeOrders();
    await renderApp('/orders');

    expect(await screen.findByRole('heading', { name: 'Orders', level: 1 })).toBeInTheDocument();
    const tabs = screen.getByRole('group', { name: 'Order status' });
    expect(within(tabs).getByRole('button', { name: /Pending approval/ })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(await within(tabs).findByRole('button', { name: /Pending approval\s*1/ })).toBeInTheDocument();
    const row = (await screen.findByRole('link', { name: '#1001' })).closest('tr') as HTMLElement;
    expect(row).toHaveTextContent('From the AI');
    expect(row).toHaveTextContent('Giulia Rossi');
    expect(row).toHaveTextContent('€185.00');

    await userEvent.click(within(tabs).getByRole('button', { name: /Confirmed/ }));
    expect(await screen.findByText('No confirmed orders yet.')).toBeInTheDocument();
  });

  it('shows a request with server prices and approves it', async () => {
    const fetchMock = fakeOrders();
    await renderApp('/orders/o-1');

    expect(await screen.findByRole('heading', { name: '#1001' })).toBeInTheDocument();
    expect(screen.getByText('2 × €89.00')).toBeInTheDocument();
    expect(screen.getByText('Total, cash on delivery').nextSibling).toHaveTextContent('€185.00');
    expect(screen.getByRole('link', { name: 'Open chat' })).toHaveAttribute('href', '/inbox/c-giulia');

    await userEvent.click(screen.getByRole('button', { name: 'Approve' }));
    expect(await screen.findByText('Confirmed')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/orders/o-1/approve')).toHaveLength(1);
  });

  it('explains when the stock is gone', async () => {
    fakeOrders({ approve: () => problem(409, 'product.not_available') });
    await renderApp('/orders/o-1');

    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }));
    expect(await screen.findByText(/Not enough stock for one of the products/)).toBeInTheDocument();
  });

  it('rejects only with a reason', async () => {
    const fetchMock = fakeOrders();
    await renderApp('/orders/o-1');

    await userEvent.click(await screen.findByRole('button', { name: 'Reject' }));
    const confirm = screen.getByRole('button', { name: 'Reject request' });
    expect(confirm).toBeDisabled();
    await userEvent.type(screen.getByRole('textbox', { name: 'Reason (the team sees it)' }), 'Fuori zona');
    await userEvent.click(confirm);

    expect(await screen.findByRole('note')).toHaveTextContent('Rejected: Fuori zona');
    expect(callsTo(fetchMock, '/api/v1/orders/o-1/reject')[0]?.body).toEqual({ reason: 'Fuori zona' });
  });

  it('edits a request in the shared form, sending IDs and quantities only', async () => {
    const fetchMock = fakeOrders();
    await renderApp('/orders/o-1');

    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }));
    const notes = screen.getByRole('textbox', { name: 'Notes' });
    await userEvent.clear(notes);
    await userEvent.type(notes, 'Dopo le 18');
    expect(
      await within(screen.getByRole('region', { name: 'Order total' })).findByText('€185.00'),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/orders/o-1').find((c) => c.body)?.body).toMatchObject({
        notes: 'Dopo le 18',
        items: [{ productId: 'p-bag', variantId: 'v-nero', quantity: 2 }],
      }),
    );
    const sent = callsTo(fetchMock, '/api/v1/orders/o-1').find((c) => c.body)?.body as Record<
      string,
      unknown
    >;
    expect(sent).not.toHaveProperty('total');
    expect(JSON.stringify(sent.items)).not.toContain('unitPrice');
    expect(await screen.findByText('Dopo le 18')).toBeInTheDocument();
  });

  it('shows not found for an unknown order', async () => {
    fakeApi({ routes: { 'GET /api/v1/orders/o-x': () => problem(404, 'order.not_found') } });
    await renderApp('/orders/o-x');
    expect(await screen.findByRole('heading', { name: 'Order not found' })).toBeInTheDocument();
  });
});

describe('Order request alert', () => {
  it('tells the team about a new AI request and links to it', async () => {
    fakeOrders();
    await renderApp('/orders');
    await waitFor(() => expect(hub.started).toBe(1));

    act(() => hub.emit('orderRequested', { conversationId: 'c-giulia', orderId: 'o-1' }));

    const text = await screen.findByText('New order request from the AI, waiting for approval.');
    const alert = text.closest('[role="status"]') as HTMLElement;
    await userEvent.click(within(alert).getByRole('link', { name: 'Open' }));
    expect(await screen.findByRole('heading', { name: '#1001' })).toBeInTheDocument();
    expect(
      screen.queryByText('New order request from the AI, waiting for approval.'),
    ).not.toBeInTheDocument();
  });
});

describe('New order from the Orders page', () => {
  it('creates an order without a chat, with the server total and IDs only', async () => {
    const fetchMock = fakeApi({
      routes: {
        'GET /api/v1/orders': () => json({ items: [], nextCursor: null }),
        'GET /api/v1/orders/counts': () => json({ pending: 0, confirmed: 0, rejected: 0 }),
        'GET /api/v1/shipping/zones': () =>
          json([
            {
              id: 'z-sar',
              name: 'Sardegna',
              areas: null,
              price: 700,
              currency: 'EUR',
              freeOver: null,
              minDays: 2,
              maxDays: 4,
            },
          ]),
        'GET /api/v1/products': () =>
          json({
            items: [
              {
                id: 'p-bag',
                title: 'Borsa nera',
                handle: 'borsa',
                status: 'active',
                productType: null,
                category: null,
                vendor: null,
                variantCount: 1,
                priceMin: 8900,
                priceMax: 8900,
                currency: 'EUR',
                stock: 5,
                updatedAt: '2026-10-30T09:00:00Z',
              },
            ],
            nextCursor: null,
          }),
        'GET /api/v1/products/p-bag': () =>
          json({
            id: 'p-bag',
            title: 'Borsa nera',
            handle: 'borsa',
            description: null,
            status: 'active',
            productType: null,
            category: null,
            vendor: null,
            tags: [],
            optionNames: ['Colore'],
            variants: [
              {
                id: 'v-nero',
                sku: 'BN-1',
                barcode: null,
                option1: 'Nero',
                option2: null,
                option3: null,
                price: 8900,
                compareAtPrice: null,
                cost: null,
                currency: 'EUR',
                trackInventory: true,
                stock: 5,
                inventoryPolicy: 'deny',
                weightGrams: null,
                requiresShipping: true,
                available: true,
              },
            ],
            createdAt: '2026-10-30T09:00:00Z',
            updatedAt: '2026-10-30T09:00:00Z',
          }),
        'POST /api/v1/orders/quote': () =>
          json({
            items: [],
            shippingZoneName: 'Sardegna',
            shippingCost: 700,
            subtotal: 8900,
            total: 9600,
            currency: 'EUR',
          }),
        'POST /api/v1/orders': () =>
          json(
            {
              ...request,
              id: 'o-2',
              number: 1002,
              status: 'confirmed',
              source: 'team',
              conversationId: null,
            },
            201,
          ),
      },
    });
    await renderApp('/orders');

    await userEvent.click(await screen.findByRole('button', { name: 'New order' }));
    const dialog = await screen.findByRole('dialog', { name: 'Create order' });
    expect(dialog.closest('form')).toBeNull();
    expect(within(dialog).getByRole('textbox', { name: 'Name' })).toHaveValue('');

    await userEvent.click(await within(dialog).findByRole('button', { name: 'Borsa nera' }));
    await userEvent.click(await within(dialog).findByRole('button', { name: 'Add Borsa nera — Nero' }));
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Name' }), 'Marco Bianchi');
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Phone' }), '+39 340 1112233');
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Street and number' }), 'Via Mare 3');
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Postal code' }), '07100');
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'City' }), 'Sassari');
    await userEvent.selectOptions(within(dialog).getByRole('combobox', { name: 'Delivery zone' }), 'z-sar');
    expect(
      await within(within(dialog).getByRole('region', { name: 'Order total' })).findByText('€96.00'),
    ).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create order' }));

    const status = await within(dialog).findByRole('status');
    expect(status).toHaveTextContent('Order #1002 is confirmed');
    // No chat: nobody is written to.
    expect(status).not.toHaveTextContent('summary');
    expect(callsTo(fetchMock, '/api/v1/orders').find((c) => c.body)?.body).toMatchObject({
      conversationId: null,
      customerName: 'Marco Bianchi',
      items: [{ productId: 'p-bag', variantId: 'v-nero', quantity: 1 }],
    });
  });
});
