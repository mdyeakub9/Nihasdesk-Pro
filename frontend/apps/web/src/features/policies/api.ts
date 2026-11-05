import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiDelete, apiGet, apiPost, apiPostForm, apiPut } from '../../lib/api';

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

/* Knowledge sources: documents and web pages the shop gives the AI (PRD 4.7, ADR 0029). */

export type SourceStatus = 'reading' | 'review' | 'live' | 'failed' | 'archived';

export type KnowledgeSource = {
  id: string;
  kind: 'pdf' | 'docx' | 'txt' | 'webpage';
  title: string;
  origin: string;
  status: SourceStatus;
  /** A code, e.g. `scanned`; the screen says it in plain words. */
  failureReason: string | null;
  sizeBytes: number | null;
  sectionCount: number;
  pageCount: number | null;
  /** Codes, e.g. `pages_without_text:3,4`, `no_embeddings`. */
  warnings: string[];
  lastReadAt: string | null;
  createdAt: string;
};

const sourcesKey = ['knowledge', 'sources'] as const;
const sourcesPath = '/api/v1/ai/sources';
/** While something is being read, the list refreshes itself: the Workers host can't push updates (ADR 0029). */
const readingRefreshMs = 3000;

export function useSources() {
  return useQuery({
    queryKey: sourcesKey,
    queryFn: () => apiGet<KnowledgeSource[]>(sourcesPath),
    refetchInterval: (query) =>
      query.state.data?.some((source) => source.status === 'reading') ? readingRefreshMs : false,
  });
}

function useSourceMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: sourcesKey }),
  });
}

export const useUploadSource = () =>
  useSourceMutation((file: File) => {
    const form = new FormData();
    form.append('file', file);
    return apiPostForm(`${sourcesPath}/files`, form);
  });

export const useAddWebPage = () => useSourceMutation((url: string) => apiPost(`${sourcesPath}/web`, { url }));

export const useDeleteSource = () =>
  useSourceMutation((id: string) => apiDelete(`${sourcesPath}/${encodeURIComponent(id)}`));
