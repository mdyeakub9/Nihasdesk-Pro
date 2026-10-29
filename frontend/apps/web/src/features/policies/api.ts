import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiGet, apiPut } from '../../lib/api';

/* Shop policies and shipping zones (PRD 4.7, ADR 0020). Money is integer cents. */

export type ShopPolicies = {
  shippingNotes: string | null;
  returnDays: number | null;
  returnPolicy: string | null;
  /** Fixed in Phase 1: cash on delivery (ADR 0004). */
  paymentMethods: string[];
  paymentNotes: string | null;
  contactPhone: string | null;
  contactEmail: string | null;
  contactAddress: string | null;
  openingHours: string | null;
};

export type PoliciesInput = Omit<ShopPolicies, 'paymentMethods'>;

export type ShippingZone = {
  id: string;
  name: string;
  areas: string | null;
  price: number;
  currency: string;
  freeOver: number | null;
  minDays: number | null;
  maxDays: number | null;
};

/** A zone to save: without an id it is added; zones left out are removed. */
export type ZoneInput = Omit<ShippingZone, 'id'> & { id?: string };

const keys = {
  policies: ['policies'] as const,
  zones: ['shipping', 'zones'] as const,
};

export function usePolicies() {
  return useQuery({ queryKey: keys.policies, queryFn: () => apiGet<ShopPolicies>('/api/v1/ai/policies') });
}

export function useSavePolicies() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: PoliciesInput) => apiPut<ShopPolicies>('/api/v1/ai/policies', input),
    onSuccess: (saved) => queryClient.setQueryData(keys.policies, saved),
  });
}

export function useShippingZones() {
  return useQuery({ queryKey: keys.zones, queryFn: () => apiGet<ShippingZone[]>('/api/v1/shipping/zones') });
}

export function useSaveShippingZones() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (zones: ZoneInput[]) => apiPut<ShippingZone[]>('/api/v1/shipping/zones', { zones }),
    onSuccess: (saved) => queryClient.setQueryData(keys.zones, saved),
  });
}
