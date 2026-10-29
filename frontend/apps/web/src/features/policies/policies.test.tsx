import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { Role } from '../session/types';
import { callsTo, fakeApi, json, testSession } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
import type { ShippingZone, ShopPolicies } from './api';

const policies = (over: Partial<ShopPolicies> = {}): ShopPolicies => ({
  shippingNotes: null,
  returnDays: null,
  returnPolicy: null,
  paymentMethods: ['cash_on_delivery'],
  paymentNotes: null,
  contactPhone: null,
  contactEmail: null,
  contactAddress: null,
  openingHours: null,
  ...over,
});

const italia: ShippingZone = {
  id: 'z-1',
  name: 'Italia',
  areas: null,
  price: 590,
  currency: 'EUR',
  freeOver: 5000,
  minDays: 2,
  maxDays: 4,
};

function knowledgeApi(role: Role = 'owner', extra: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    session: { ...testSession, role },
    ...extra,
    routes: {
      'GET /api/v1/shipping/zones': () => json([]),
      'GET /api/v1/ai/policies': () => json(policies()),
      'GET /api/v1/ai/answers': () => json([]),
      ...extra.routes,
    },
  });
}

describe('Settings → Shop knowledge', () => {
  it('adds shipping zones with costs in cents', async () => {
    const fetchMock = knowledgeApi('owner', {
      routes: { 'PUT /api/v1/shipping/zones': () => json([italia]) },
    });
    await renderApp('/settings/knowledge');

    expect(await screen.findByText(/No zones yet/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Add a zone' }));
    const zones = screen.getByRole('list', { name: 'Shipping zones' });
    await userEvent.type(within(zones).getByLabelText('Zone'), 'Italia');
    await userEvent.type(within(zones).getByLabelText('Cost (EUR)'), '5,90');
    await userEvent.type(within(zones).getByLabelText('Free from (EUR)'), '50');
    await userEvent.type(within(zones).getByLabelText('Min. days'), '2');
    await userEvent.type(within(zones).getByLabelText('Max. days'), '4');
    await userEvent.click(screen.getByRole('button', { name: 'Save zones' }));

    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/shipping/zones').find((c) => c.body)?.body).toEqual({
        zones: [
          {
            name: 'Italia',
            areas: null,
            price: 590,
            currency: 'EUR',
            freeOver: 5000,
            minDays: 2,
            maxDays: 4,
          },
        ],
      }),
    );
  });

  it('keeps zone ids, removes zones and refuses an invalid one', async () => {
    const fetchMock = knowledgeApi('admin', {
      routes: {
        'GET /api/v1/shipping/zones': () =>
          json([italia, { ...italia, id: 'z-2', name: 'Isole', price: 990 }]),
        'PUT /api/v1/shipping/zones': () => json([italia]),
      },
    });
    await renderApp('/settings/knowledge');

    const zones = await screen.findByRole('list', { name: 'Shipping zones' });
    const isole = within(zones).getAllByRole('listitem')[1] as HTMLElement;
    await userEvent.click(within(isole).getByRole('button', { name: 'Remove' }));
    const cost = within(zones).getByLabelText('Cost (EUR)');
    await userEvent.clear(cost);
    await userEvent.type(cost, 'gratis');
    await userEvent.click(screen.getByRole('button', { name: 'Save zones' }));
    expect(await screen.findByText(/Check this zone/)).toBeInTheDocument();
    expect(callsTo(fetchMock, '/api/v1/shipping/zones').filter((c) => c.body)).toHaveLength(0);

    await userEvent.clear(cost);
    await userEvent.type(cost, '6.90');
    await userEvent.click(screen.getByRole('button', { name: 'Save zones' }));
    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/shipping/zones').find((c) => c.body)?.body).toEqual({
        zones: [
          {
            id: 'z-1',
            name: 'Italia',
            areas: null,
            price: 690,
            currency: 'EUR',
            freeOver: 5000,
            minDays: 2,
            maxDays: 4,
          },
        ],
      }),
    );
  });

  it('saves returns and contact details and shows cash on delivery as fixed', async () => {
    const fetchMock = knowledgeApi('owner', {
      routes: { 'PUT /api/v1/ai/policies': () => json(policies({ returnDays: 14 })) },
    });
    await renderApp('/settings/knowledge');

    expect(await screen.findByText(/Cash on delivery/)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Return within (days after delivery)'), '14');
    await userEvent.type(screen.getByLabelText('Return rules'), 'Prodotti non usati.');
    await userEvent.type(screen.getByLabelText('Phone'), '+39 333 1234567');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/ai/policies').find((c) => c.body)?.body).toEqual({
        shippingNotes: null,
        returnDays: 14,
        returnPolicy: 'Prodotti non usati.',
        paymentNotes: null,
        contactPhone: '+39 333 1234567',
        contactEmail: null,
        contactAddress: null,
        openingHours: null,
      }),
    );
  });

  it('lists the FAQ with where each answer came from, and adds new ones', async () => {
    const fetchMock = knowledgeApi('owner', {
      routes: {
        'GET /api/v1/ai/answers': () =>
          json([
            {
              id: 'a-1',
              question: 'Spedite in Sardegna?',
              answer: 'Sì',
              active: true,
              updatedAt: '2026-10-28T10:00:00Z',
              source: 'taught',
            },
            {
              id: 'a-2',
              question: 'Orari?',
              answer: 'Lun–Sab 9–19',
              active: true,
              updatedAt: '2026-10-28T10:00:00Z',
              source: 'added',
            },
          ]),
        'PUT /api/v1/ai/answers/a-2': () => json({}),
      },
      post: { '/api/v1/ai/answers': () => json({}, 201) },
    });
    await renderApp('/settings/knowledge');

    const list = await screen.findByRole('list', { name: 'Answers' });
    const [taught, added] = within(list).getAllByRole('listitem') as [HTMLElement, HTMLElement];
    expect(within(taught).getByText('Taught')).toBeInTheDocument();
    expect(within(added).getByText('FAQ')).toBeInTheDocument();
    await userEvent.click(within(added).getByRole('switch', { name: 'In use' }));
    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/ai/answers/a-2')[0]?.body).toEqual({
        question: 'Orari?',
        answer: 'Lun–Sab 9–19',
        active: false,
      }),
    );

    const add = screen.getByRole('heading', { name: 'Add an answer' }).parentElement as HTMLElement;
    await userEvent.type(within(add).getByLabelText('Question'), 'Pagamento?');
    await userEvent.type(within(add).getByLabelText('Answer'), 'Contrassegno.');
    await userEvent.click(within(add).getByRole('button', { name: 'Add' }));
    await waitFor(() =>
      expect(callsTo(fetchMock, '/api/v1/ai/answers').find((c) => c.body)?.body).toEqual({
        question: 'Pagamento?',
        answer: 'Contrassegno.',
      }),
    );
  });

  it('lets Members read but not change anything', async () => {
    knowledgeApi('member', {
      routes: {
        'GET /api/v1/shipping/zones': () => json([italia]),
        'GET /api/v1/ai/policies': () => json(policies({ returnDays: 14 })),
      },
    });
    await renderApp('/settings/knowledge');

    expect(await screen.findByRole('note')).toHaveTextContent('Only Owners and Admins');
    expect(await screen.findByDisplayValue('Italia')).toBeDisabled();
    expect(screen.getByDisplayValue('14')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Save zones' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Add an answer' })).not.toBeInTheDocument();
  });
});
