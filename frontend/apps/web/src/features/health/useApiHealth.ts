import { useQuery } from '@tanstack/react-query';
import { apiFetch } from '../../lib/api';

/** Liveness of the API (`GET /health`), re-checked every 30 seconds. */
export function useApiHealth() {
  return useQuery({
    queryKey: ['health'],
    queryFn: async () => (await apiFetch('/health')).text(),
    refetchInterval: 30_000,
  });
}
