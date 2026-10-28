import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, apiGet, apiPost, apiPostForm, apiPut } from '../../lib/api';

/* The catalog API (ADR 0018). Money is always integer minor units (8900 = €89.00). */

export const productStatuses = ['active', 'draft', 'archived'] as const;
export type ProductStatus = (typeof productStatuses)[number];
export type InventoryPolicy = 'deny' | 'continue';

export type Variant = {
  id: string;
  sku: string | null;
  barcode: string | null;
  option1: string | null;
  option2: string | null;
  option3: string | null;
  price: number;
  compareAtPrice: number | null;
  cost: number | null;
  currency: string;
  trackInventory: boolean;
  stock: number | null;
  inventoryPolicy: InventoryPolicy;
  weightGrams: number | null;
  requiresShipping: boolean;
  available: boolean;
};

export type Product = {
  id: string;
  title: string;
  handle: string;
  description: string | null;
  status: ProductStatus;
  productType: string | null;
  category: string | null;
  vendor: string | null;
  tags: string[];
  optionNames: string[];
  variants: Variant[];
  createdAt: string;
  updatedAt: string;
};

export type ProductSummary = {
  id: string;
  title: string;
  handle: string;
  status: ProductStatus;
  productType: string | null;
  category: string | null;
  vendor: string | null;
  variantCount: number;
  priceMin: number | null;
  priceMax: number | null;
  currency: string | null;
  stock: number | null;
  updatedAt: string;
};

/** What the editor sends; variants without an ID are new. */
export type ProductInput = {
  title: string;
  description: string | null;
  status: ProductStatus;
  productType: string | null;
  category: string | null;
  vendor: string | null;
  tags: string[];
  optionNames: string[];
  variants: (Omit<Variant, 'id' | 'available'> & { id?: string })[];
};

export type ImportIssue = { line: number; column: string | null; code: string; message: string };

export type ImportReport = {
  applied: boolean;
  rows: number;
  counts: {
    productsCreated: number;
    productsUpdated: number;
    variantsCreated: number;
    variantsUpdated: number;
  };
  issues: ImportIssue[];
};

type Page = { items: ProductSummary[]; nextCursor: string | null };

export const catalogKeys = {
  all: ['catalog'] as const,
  list: (query: string, status: ProductStatus | 'all') =>
    [...catalogKeys.all, 'list', query, status] as const,
  detail: (id: string) => [...catalogKeys.all, 'detail', id] as const,
};

export const templateUrl = '/api/v1/products/import/template';

export function useProducts(query: string, status: ProductStatus | 'all') {
  return useInfiniteQuery({
    queryKey: catalogKeys.list(query, status),
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) => {
      const params = new URLSearchParams({ limit: '30' });
      if (query) params.set('q', query);
      if (status !== 'all') params.set('status', status);
      if (pageParam) params.set('cursor', pageParam);
      return apiGet<Page>(`/api/v1/products?${params}`);
    },
    getNextPageParam: (last) => last.nextCursor,
  });
}

export function useProduct(id: string | undefined) {
  return useQuery({
    queryKey: catalogKeys.detail(id ?? 'new'),
    queryFn: () => apiGet<Product>(`/api/v1/products/${encodeURIComponent(id ?? '')}`),
    enabled: !!id,
  });
}

export function useSaveProduct(id: string | undefined) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: ProductInput) =>
      id
        ? apiPut<Product>(`/api/v1/products/${encodeURIComponent(id)}`, input)
        : apiPost<Product>('/api/v1/products', input),
    onSuccess: (product) => {
      queryClient.setQueryData(catalogKeys.detail(product.id), product);
      void queryClient.invalidateQueries({ queryKey: [...catalogKeys.all, 'list'] });
    },
  });
}

/**
 * Preview or apply a CSV file. Both answer with the same report; an apply with issues answers 422
 * and the report, which is returned like a preview.
 */
export function useImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ file, apply }: { file: File; apply: boolean }) => {
      const form = new FormData();
      form.append('file', file);
      try {
        return await apiPostForm<ImportReport>(
          `/api/v1/products/import/${apply ? 'apply' : 'preview'}`,
          form,
        );
      } catch (error) {
        if (error instanceof ApiError && error.status === 422 && error.body)
          return error.body as ImportReport;
        throw error;
      }
    },
    onSuccess: (report) => {
      if (report.applied) void queryClient.invalidateQueries({ queryKey: catalogKeys.all });
    },
  });
}

/** Field errors from a 400 validation answer, e.g. `{ "Variants[0].Price": ["…"] }`. */
export function fieldErrors(error: unknown): string[] {
  if (!(error instanceof ApiError) || error.status !== 400) return [];
  const errors = (error.body as { errors?: Record<string, string[]> } | undefined)?.errors;
  return errors ? Object.values(errors).flat() : [];
}
