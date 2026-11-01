import { create } from 'zustand';

/**
 * The newest AI order request waiting for a person (ADR 0025), from the hub's `orderRequested`
 * event (IDs only). Email and push come with the Notifications API.
 */
type Requested = { orderId: string; conversationId: string };

type OrderRequestState = {
  latest: Requested | null;
  received: (request: Requested) => void;
  dismiss: () => void;
};

export const useOrderRequestStore = create<OrderRequestState>()((set) => ({
  latest: null,
  received: (request) => set({ latest: request }),
  dismiss: () => set({ latest: null }),
}));
