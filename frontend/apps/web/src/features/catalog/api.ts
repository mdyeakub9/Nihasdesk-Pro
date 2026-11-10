import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError, apiDelete, apiGet, apiPost, apiPostForm, apiPut, apiUrl } from '../../lib/api';

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
  /** A photo of the same product; null = the main photo (ADR 0031 §12). */
  imageId: string | null;
};

/** Formats the server checks by content (SEC-04); the browser only checks to answer sooner. */
export const imageTypes = ['image/jpeg', 'image/png', 'image/webp'] as const;
export const maxImages = 8;
export const maxImageBytes = 10 * 1024 * 1024;

/** Links need a signed-in member; the first saved photo is the main one. */
export type ProductImage = {
  id: string;
  position: number;
  status: 'pending' | 'saved' | 'failed';
  failureCode: string | null;
  width: number | null;
  height: number | null;
  url: string | null;
  thumbnailUrl: string | null;
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
  images: ProductImage[];
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
  thumbnailUrl: string | null;
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

/** An API image path as an <img> source; in production the API is on its own origin (cookies go along). */
export const imageSrc = (path: string) => apiUrl(path);

const imagesPath = (productId: string) => `/api/v1/products/${encodeURIComponent(productId)}/images`;

/**
 * Photos are saved at once, apart from the product form (ADR 0031 §12). Each change updates the
 * cached product, so the editor and the list show it without a reload.
 */
export function useProductImages(productId: string) {
  const queryClient = useQueryClient();
  const setImages = (update: (images: ProductImage[]) => ProductImage[]) => {
    queryClient.setQueryData<Product>(catalogKeys.detail(productId), (product) =>
      product ? { ...product, images: update(product.images) } : product,
    );
    void queryClient.invalidateQueries({ queryKey: [...catalogKeys.all, 'list'] });
  };

  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append('file', file);
      return apiPostForm<ProductImage>(imagesPath(productId), form);
    },
    onSuccess: (image) => setImages((images) => [...images, image]),
  });

  const reorder = useMutation({
    mutationFn: (imageIds: string[]) =>
      apiPut<ProductImage[]>(`${imagesPath(productId)}/order`, { imageIds }),
    onSuccess: (images) => setImages(() => images),
  });

  const remove = useMutation({
    mutationFn: (imageId: string) => apiDelete(`${imagesPath(productId)}/${encodeURIComponent(imageId)}`),
    onSuccess: (_, imageId) => {
      // The server moved the rest up and freed the variants that used it; read them again.
      setImages((images) =>
        images.filter((i) => i.id !== imageId).map((i, index) => ({ ...i, position: index + 1 })),
      );
      void queryClient.invalidateQueries({ queryKey: catalogKeys.detail(productId) });
    },
  });

  return { upload, reorder, remove };
}

/** Field errors from a 400 validation answer, e.g. `{ "Variants[0].Price": ["…"] }`. */
export function fieldErrors(error: unknown): string[] {
  if (!(error instanceof ApiError) || error.status !== 400) return [];
  const errors = (error.body as { errors?: Record<string, string[]> } | undefined)?.errors;
  return errors ? Object.values(errors).flat() : [];
}
