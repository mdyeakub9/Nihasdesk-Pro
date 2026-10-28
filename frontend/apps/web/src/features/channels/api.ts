import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiDelete, apiGet, apiPost } from '../../lib/api';

/** A connected channel account; the API never sends tokens (ADR 0017). */
export type ChannelConnection = {
  id: string;
  kind: 'messenger';
  name: string;
  externalAccountId: string;
  connectedAt: string;
};

const channelsKey = ['channels'] as const;

export function useChannelConnections(enabled: boolean) {
  return useQuery({
    queryKey: channelsKey,
    queryFn: () => apiGet<ChannelConnection[]>('/api/v1/channels'),
    enabled,
  });
}

/** Asks the API for the Facebook Login URL (with its one-time state). */
export function useStartMetaConnect() {
  return useMutation({
    mutationFn: () => apiPost<{ url: string }>('/api/v1/channels/meta/connect'),
  });
}

export function useDisconnectChannel() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiDelete(`/api/v1/channels/${encodeURIComponent(id)}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: channelsKey }),
  });
}

/** Only Facebook's own login dialog is a valid target for the connect redirect. */
export function isFacebookLoginUrl(url: string): boolean {
  try {
    const parsed = new URL(url);
    return (
      parsed.protocol === 'https:' &&
      parsed.hostname === 'www.facebook.com' &&
      parsed.pathname.endsWith('/dialog/oauth')
    );
  } catch {
    return false;
  }
}
