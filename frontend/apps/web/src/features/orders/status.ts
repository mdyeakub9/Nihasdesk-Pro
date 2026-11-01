import type { OrderStatus } from './api';

/** Badge colour per order status: pending stands out until a person decides. */
export const statusTone: Record<OrderStatus, 'warning' | 'success' | 'neutral'> = {
  pending: 'warning',
  confirmed: 'success',
  rejected: 'neutral',
};
