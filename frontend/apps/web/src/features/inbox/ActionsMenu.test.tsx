import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { callsTo, json } from '../../test/fake-api';
import { fakeInboxApi, seedConversations } from '../../test/inbox-api';
import { renderApp } from '../../test/render-app';

const variant = (id: string, option1: string, price: number, available: boolean) => ({
  id,
  sku: `SKU-${id}`,
  barcode: null,
  option1,
  option2: null,
  option3: null,
  price,
  compareAtPrice: null,
  cost: null,
  currency: 'EUR',
  trackInventory: true,
  stock: available ? 3 : 0,
  inventoryPolicy: 'deny',
  weightGrams: null,
  requiresShipping: true,
  available,
});

const bag = {
  id: 'p-bag',
  title: 'Borsa nera',
  handle: 'borsa-nera',
  description: null,
  status: 'active',
  productType: null,
  category: null,
  vendor: null,
  tags: [],
  optionNames: ['Colore'],
  variants: [variant('v-nero', 'Nero', 8900, true), variant('v-rosso', 'Rosso', 9500, false)],
  createdAt: '2026-10-30T09:00:00Z',
  updatedAt: '2026-10-30T09:00:00Z',
};

const summary = {
  id: 'p-bag',
  title: 'Borsa nera',
  handle: 'borsa-nera',
  status: 'active',
  productType: null,
  category: null,
  vendor: null,
  variantCount: 2,
  priceMin: 8900,
  priceMax: 9500,
  currency: 'EUR',
  stock: 3,
  updatedAt: '2026-10-30T09:00:00Z',
};

function catalogRoutes() {
  return {
    'GET /api/v1/products': () => json({ items: [summary], nextCursor: null }),
    'GET /api/v1/products/p-bag': () => json(bag),
  };
}

async function openMenu() {
  await userEvent.click(await screen.findByRole('button', { name: 'More actions' }));
  return screen.getByRole('menu', { name: 'More actions' });
}

describe('+ menu', () => {
  it('lists the actions, keeps image/file disabled, and closes with Esc', async () => {
    fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    const menu = await openMenu();
    const items = within(menu).getAllByRole('menuitem');
    expect(items.map((i) => i.textContent)).toEqual([
      'Send products',
      'Create order',
      'Saved reply',
      'Send image or fileComing soon',
    ]);
    expect(within(menu).getByRole('menuitem', { name: /Send image or file/ })).toBeDisabled();
    expect(items[0]).toHaveFocus();
    await userEvent.keyboard('{ArrowDown}');
    expect(items[1]).toHaveFocus();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'More actions' })).toHaveFocus();
  });

  it('sends in-stock products by ID only and shows them as cards', async () => {
    const conversations = seedConversations();
    const { fetchMock, state } = fakeInboxApi({
      conversations,
      routes: {
        ...catalogRoutes(),
        'POST /api/v1/conversations/c-giulia/products': () => {
          const giulia = state.conversations.find((c) => c.id === 'c-giulia');
          giulia?.messages.push({
            id: 'm-card',
            author: 'agent',
            authorUserId: 'u-1',
            authorName: 'Md Yeakub',
            text: '🛍️ Borsa nera — Nero\n€89.00 · SKU SKU-v-nero',
            internal: false,
            sentAt: new Date().toISOString(),
            delivery: 'pending',
            product: {
              productId: 'p-bag',
              variantId: 'v-nero',
              title: 'Borsa nera',
              variant: 'Nero',
              sku: 'SKU-v-nero',
              price: 8900,
              currency: 'EUR',
            },
          } as never);
          return json([], 201);
        },
      },
    });
    await renderApp('/inbox/c-giulia');

    await userEvent.click(within(await openMenu()).getByRole('menuitem', { name: 'Send products' }));
    const picker = await screen.findByRole('region', { name: 'Send products' });
    await userEvent.type(within(picker).getByRole('searchbox'), 'borsa{Enter}');
    await userEvent.click(await within(picker).findByRole('button', { name: 'Borsa nera' }));

    // Only the black one is in stock.
    const nero = await within(picker).findByRole('checkbox', { name: /Nero/ });
    expect(within(picker).queryByRole('checkbox', { name: /Rosso/ })).not.toBeInTheDocument();
    await userEvent.click(nero);
    expect(within(picker).getByText(/1 of 5 selected/)).toBeInTheDocument();
    await userEvent.click(within(picker).getByRole('button', { name: 'Send 1 product' }));

    await waitFor(() =>
      expect(screen.queryByRole('region', { name: 'Send products' })).not.toBeInTheDocument(),
    );
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/products')[0]?.body).toEqual({
      items: [{ productId: 'p-bag', variantId: 'v-nero' }],
    });
    // Enter in the search box did not send the composer's text.
    expect(callsTo(fetchMock, '/api/v1/conversations/c-giulia/messages').filter((c) => c.body)).toEqual([]);

    const card = await screen.findByRole('group', { name: 'Product: Borsa nera' });
    expect(card).toHaveTextContent('Nero · €89.00 · SKU SKU-v-nero');
  });

  it('opens the order modal with the customer’s name and phone', async () => {
    fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    await userEvent.click(within(await openMenu()).getByRole('menuitem', { name: 'Create order' }));
    const dialog = await screen.findByRole('dialog', { name: 'Create order' });
    expect(within(dialog).getByRole('textbox', { name: 'Customer name' })).toHaveValue('Giulia Rossi');
    expect(within(dialog).getByRole('textbox', { name: 'Phone' })).toHaveValue('+39 333 000 1111');
    expect(within(dialog).getByRole('note')).toHaveTextContent('Orders come with the Orders module');
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('opens the saved replies list from the menu', async () => {
    fakeInboxApi();
    await renderApp('/inbox/c-giulia');

    await userEvent.click(within(await openMenu()).getByRole('menuitem', { name: 'Saved reply' }));
    expect(await screen.findByRole('listbox', { name: 'Saved replies' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Reply' })).toHaveValue('/');
  });
});
