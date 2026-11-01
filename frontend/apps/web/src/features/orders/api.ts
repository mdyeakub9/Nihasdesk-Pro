import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, apiGet, apiPost, apiPut } from '../../lib/api';

/*
 * Orders and order requests (PRD 4.9, ADR 0025). Money is integer cents; prices and totals always
 * come from the server (SEC-41) — the dashboard only sends IDs and quantities.
 */

export const orderStatuses = ['pending', 'confirmed', 'rejected'] as const;
export type OrderStatus = (typeof orderStatuses)[number];

export type OrderLine = {
  productId: string;
  variantId: string;
  title: string;
  variant: string;
  sku: string | null;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
};

export type Order = {
  id: string;
  number: number;
  status: OrderStatus;
  source: 'team' | 'ai';
  conversationId: string | null;
  customerName: string;
  phone: string;
  street: string;
  postalCode: string;
  city: string;
  province: string | null;
  country: string;
  notes: string | null;
  shippingZoneId: string;
  shippingZoneName: string;
  shippingCost: number;
  subtotal: number;
  total: number;
  currency: string;
  paymentMethod: string;
  items: OrderLine[];
  createdAt: string;
  decidedAt: string | null;
  rejectReason: string | null;
};

export type OrderSummary = Pick<
  Order,
  'id' | 'number' | 'status' | 'source' | 'customerName' | 'total' | 'currency' | 'createdAt'
> & { itemCount: number };

export type OrderCounts = Record<OrderStatus, number>;

/** What the form sends: the default order template (PRD 4.9). */
export type OrderInput = {
  conversationId: string | null;
  customerName: string;
  phone: string;
  street: string;
  postalCode: string;
  city: string;
  province: string | null;
  country: string;
  shippingZoneId: string;
  notes: string | null;
  items: { productId: string; variantId: string; quantity: number }[];
};

export type Quote = Pick<
  Order,
  'items' | 'shippingZoneName' | 'shippingCost' | 'subtotal' | 'total' | 'currency'
>;

type Page = { items: OrderSummary[]; nextCursor: string | null };

export const orderKeys = {
  all: ['orders'] as const,
  list: (status: OrderStatus) => [...orderKeys.all, 'list', status] as const,
  counts: () => [...orderKeys.all, 'counts'] as const,
  detail: (id: string) => [...orderKeys.all, 'detail', id] as const,
  forConversation: (id: string) => [...orderKeys.all, 'conversation', id] as const,
};

export function useOrders(status: OrderStatus) {
  return useInfiniteQuery({
    queryKey: orderKeys.list(status),
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) => {
      const params = new URLSearchParams({ status, limit: '30' });
      if (pageParam) params.set('cursor', pageParam);
      return apiGet<Page>(`/api/v1/orders?${params}`);
    },
    getNextPageParam: (last) => last.nextCursor,
  });
}

export function useOrderCounts() {
  return useQuery({
    queryKey: orderKeys.counts(),
    queryFn: () => apiGet<OrderCounts>('/api/v1/orders/counts'),
  });
}

/** One chat's orders, for the inbox's customer panel. */
export function useConversationOrders(conversationId: string) {
  return useQuery({
    queryKey: orderKeys.forConversation(conversationId),
    queryFn: async () =>
      (await apiGet<Page>(`/api/v1/orders?conversationId=${encodeURIComponent(conversationId)}&limit=20`))
        .items,
  });
}

export function useOrder(id: string) {
  return useQuery({
    queryKey: orderKeys.detail(id),
    queryFn: async () => {
      try {
        return await apiGet<Order>(`/api/v1/orders/${encodeURIComponent(id)}`);
      } catch (error) {
        // Unknown IDs and other workspaces' IDs both answer 404 (SEC-06).
        if (error instanceof ApiError && error.status === 404) return null;
        throw error;
      }
    },
  });
}

/** Server-side prices for a draft (nothing saved); null until there are lines and a zone. */
export function useQuote(draft: Pick<OrderInput, 'shippingZoneId' | 'items'>) {
  const ready = !!draft.shippingZoneId && draft.items.length > 0 && draft.items.every((i) => i.quantity >= 1);
  return useQuery({
    queryKey: [...orderKeys.all, 'quote', draft.shippingZoneId, draft.items] as const,
    queryFn: () => apiPost<Quote>('/api/v1/orders/quote', draft),
    enabled: ready,
    retry: false,
  });
}

function useOrderMutation<T>(fn: (input: T) => Promise<Order>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: orderKeys.all });
      // Stock changed: the catalog shows it.
      void queryClient.invalidateQueries({ queryKey: ['catalog'] });
    },
  });
}

/** A team order: confirmed at once (a person created it). */
export const useCreateOrder = () =>
  useOrderMutation((input: OrderInput) => apiPost<Order>('/api/v1/orders', input));

export const useUpdateOrder = (id: string) =>
  useOrderMutation((input: OrderInput) => apiPut<Order>(`/api/v1/orders/${encodeURIComponent(id)}`, input));

export const useApproveOrder = (id: string) =>
  useOrderMutation(() => apiPost<Order>(`/api/v1/orders/${encodeURIComponent(id)}/approve`));

export const useRejectOrder = (id: string) =>
  useOrderMutation((reason: string) =>
    apiPost<Order>(`/api/v1/orders/${encodeURIComponent(id)}/reject`, { reason }),
  );
