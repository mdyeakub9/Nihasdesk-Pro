import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiDelete, apiGet, apiPost } from '../../lib/api';
import { sessionKey } from '../session/useSession';
import { withReauth } from './reauth';

export type MfaStatus = { enabled: boolean; recoveryCodesLeft: number };
export type MfaSetup = { secret: string; otpauthUri: string };
export type AccountSession = {
  id: string;
  userAgent: string | null;
  remember: boolean;
  createdAt: string;
  lastSeenAt: string;
  current: boolean;
};

const keys = {
  mfa: ['account', 'mfa'] as const,
  sessions: ['account', 'sessions'] as const,
};

export const useMfaStatus = () =>
  useQuery({ queryKey: keys.mfa, queryFn: () => apiGet<MfaStatus>('/api/v1/account/mfa') });

export const useSessions = () =>
  useQuery({ queryKey: keys.sessions, queryFn: () => apiGet<AccountSession[]>('/api/v1/account/sessions') });

/** MFA changes also refresh the signed-in user (mfaEnabled drives the Owner reminder). */
function useAccountMutation<TArgs, TResult>(mutationFn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['account'] });
      void queryClient.invalidateQueries({ queryKey: sessionKey });
    },
  });
}

export const useStartMfaSetup = () =>
  useMutation({ mutationFn: () => apiPost<MfaSetup>('/api/v1/account/mfa/setup') });

export const useEnableMfa = () =>
  useAccountMutation((code: string) =>
    apiPost<{ recoveryCodes: string[] }>('/api/v1/account/mfa/enable', { code }),
  );

export const useDisableMfa = () =>
  useAccountMutation(() => withReauth(() => apiPost('/api/v1/account/mfa/disable')));

export const useNewRecoveryCodes = () =>
  useAccountMutation(() =>
    withReauth(() => apiPost<{ recoveryCodes: string[] }>('/api/v1/account/mfa/recovery-codes')),
  );

export const useRevokeSession = () =>
  useAccountMutation((id: string) => apiDelete(`/api/v1/account/sessions/${encodeURIComponent(id)}`));

export const useRevokeOtherSessions = () =>
  useAccountMutation(() => apiPost('/api/v1/account/sessions/revoke-others'));

export const useChangePassword = () =>
  useAccountMutation((body: { currentPassword: string; newPassword: string }) =>
    apiPost('/api/v1/account/password', body),
  );
