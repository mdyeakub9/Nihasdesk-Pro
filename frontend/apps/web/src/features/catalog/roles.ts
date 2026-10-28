import type { ProductStatus } from './api';

/** Owner and Admin manage the catalog (docs/standards/permissions.md); the API checks it again. */
export const canManageCatalog = (role: string | undefined) => role === 'owner' || role === 'admin';

export const statusTone: Record<ProductStatus, 'success' | 'neutral' | 'warning'> = {
  active: 'success',
  draft: 'neutral',
  archived: 'warning',
};
