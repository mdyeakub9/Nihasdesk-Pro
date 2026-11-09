import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
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

/* A whole website from its domain (ADR 0030): a job finds the pages, the owner confirms them. */
export type SitePage = {
  url: string;
  title: string;
  chosen: boolean;
  sourceId: string | null;
  sourceStatus: SourceStatus | null;
};

export type KnowledgeSite = {
  id: string;
  host: string;
  status: 'finding' | 'ready' | 'failed';
  failureReason: string | null;
  pages: SitePage[];
  createdAt: string;
};

const sitesKey = ['knowledge', 'sites'] as const;
const sitesPath = '/api/v1/ai/sites';
const sitePath = (id: string) => `${sitesPath}/${encodeURIComponent(id)}`;

export function useSites() {
  return useQuery({
    queryKey: sitesKey,
    queryFn: () => apiGet<KnowledgeSite[]>(sitesPath),
    refetchInterval: (query) =>
      query.state.data?.some(
        (site) => site.status === 'finding' || site.pages.some((page) => page.sourceStatus === 'reading'),
      )
        ? readingRefreshMs
        : false,
  });
}

function useSiteMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: sitesKey }),
        queryClient.invalidateQueries({ queryKey: sourcesKey }),
      ]),
  });
}

export const useAddSite = () =>
  useSiteMutation((domain: string) => apiPost(sitesPath, { domain, confirmed: true }));

export const useAddSitePages = (id: string) =>
  useSiteMutation((urls: string[]) => apiPost(`${sitePath(id)}/pages`, { urls }));

export const useUseSite = (id: string) => useSiteMutation(() => apiPost(`${sitePath(id)}/use`));

export const useDeleteSite = () => useSiteMutation((id: string) => apiDelete(sitePath(id)));

/** Differences between the site's read pages and the settings, before they are all used. */
export const useSiteDifferences = (id: string, enabled: boolean) =>
  useQuery({
    queryKey: [...sitesKey, id, 'differences'],
    queryFn: () => apiGet<{ count: number }>(`${sitePath(id)}/differences`),
    enabled,
  });

/* Reviewing a source: what the AI will be able to find, and where it differs from the settings. */

export type ReviewConflict = {
  kind: 'shipping_cost' | 'delivery_days' | 'return_days' | 'payment_method';
  sectionId: string;
  page: number | null;
  found: string;
  expected: string;
};

export type SourceReview = {
  source: KnowledgeSource;
  conflicts: ReviewConflict[];
  priceListSections: string[];
};

export type ReviewSection = {
  id: string;
  position: number;
  headingPath: string | null;
  page: number | null;
  kind: 'text' | 'list' | 'table' | 'faq';
  text: string;
  excluded: boolean;
  /** False when the embedding is missing: only exact words find it. */
  searchable: boolean;
};

type SectionPage = { items: ReviewSection[]; nextAfter: number | null };

const sourceKey = (id: string) => ['knowledge', 'source', id] as const;
const sectionPath = (sourceId: string, sectionId: string) =>
  `${sourcesPath}/${encodeURIComponent(sourceId)}/sections/${encodeURIComponent(sectionId)}`;

export function useSourceReview(id: string) {
  return useQuery({
    queryKey: sourceKey(id),
    queryFn: () => apiGet<SourceReview>(`${sourcesPath}/${encodeURIComponent(id)}`),
    refetchInterval: (query) => (query.state.data?.source.status === 'reading' ? readingRefreshMs : false),
  });
}

export function useSourceSections(id: string) {
  return useInfiniteQuery({
    queryKey: [...sourceKey(id), 'sections'],
    initialPageParam: null as number | null,
    queryFn: ({ pageParam }) =>
      apiGet<SectionPage>(
        `${sourcesPath}/${encodeURIComponent(id)}/sections${pageParam === null ? '' : `?after=${pageParam}`}`,
      ),
    getNextPageParam: (last) => last.nextAfter,
  });
}

/** A change can clear or add warnings and change the count in the list, so all of it refreshes. */
function useReviewMutation<TArgs>(sourceId: string, mutationFn: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: sourceKey(sourceId) }),
        queryClient.invalidateQueries({ queryKey: sourcesKey }),
      ]),
  });
}

export const useEditSection = (sourceId: string) =>
  useReviewMutation(sourceId, ({ sectionId, text }: { sectionId: string; text: string }) =>
    apiPut(sectionPath(sourceId, sectionId), { text }),
  );

export const useSetSectionExcluded = (sourceId: string) =>
  useReviewMutation(sourceId, ({ sectionId, excluded }: { sectionId: string; excluded: boolean }) =>
    apiPost(`${sectionPath(sourceId, sectionId)}/${excluded ? 'exclude' : 'include'}`),
  );

export const useMergeNextSection = (sourceId: string) =>
  useReviewMutation(sourceId, (sectionId: string) =>
    apiPost(`${sectionPath(sourceId, sectionId)}/merge-next`),
  );

/** Only sources in use reach customers (Issue #53). */
export const useSetSourceUsed = (sourceId: string) =>
  useReviewMutation(sourceId, (used: boolean) =>
    apiPost(`${sourcesPath}/${encodeURIComponent(sourceId)}/${used ? 'use' : 'stop'}`),
  );

export type FoundPassage = {
  sectionId: string;
  sourceId: string;
  sourceTitle: string;
  headingPath: string | null;
  page: number | null;
  text: string;
  similarity: number | null;
};

export const useTestQuestion = (sourceId: string) =>
  useMutation({
    mutationFn: (query: string) => apiPost<FoundPassage[]>(`${sourcesPath}/search`, { query, sourceId }),
  });
