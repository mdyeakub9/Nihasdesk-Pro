import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiDelete, apiGet, apiPatch, apiPost } from '../../lib/api';
import { withReauth } from '../account/reauth';
import type { Role } from '../session/types';

export type Member = { userId: string; name: string; email: string; role: Role; joinedAt: string };
export type Invitation = {
  id: string;
  email: string;
  role: 'admin' | 'member';
  invitedAt: string;
  expiresAt: string;
};
export type InvitationPreview = {
  workspaceName: string;
  inviterName: string;
  email: string;
  role: 'admin' | 'member';
  expiresAt: string;
};

const teamKeys = {
  members: ['team', 'members'] as const,
  invitations: ['team', 'invitations'] as const,
};

export function useMembers() {
  return useQuery({ queryKey: teamKeys.members, queryFn: () => apiGet<Member[]>('/api/v1/team/members') });
}

export function useInvitations(enabled: boolean) {
  return useQuery({
    queryKey: teamKeys.invitations,
    queryFn: () => apiGet<Invitation[]>('/api/v1/team/invitations'),
    enabled,
  });
}

/** All team changes refresh both lists. */
function useTeamMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['team'] }),
  });
}

export const useInvite = () =>
  useTeamMutation((body: { email: string; role: 'admin' | 'member' }) =>
    apiPost('/api/v1/team/invitations', body),
  );

export const useResendInvitation = () =>
  useTeamMutation((id: string) => apiPost(`/api/v1/team/invitations/${encodeURIComponent(id)}/resend`));

export const useRevokeInvitation = () =>
  useTeamMutation((id: string) => apiDelete(`/api/v1/team/invitations/${encodeURIComponent(id)}`));

export const useChangeRole = () =>
  useTeamMutation(({ userId, role }: { userId: string; role: 'admin' | 'member' }) =>
    withReauth(() => apiPatch(`/api/v1/team/members/${encodeURIComponent(userId)}`, { role })),
  );

export const useRemoveMember = () =>
  useTeamMutation((userId: string) =>
    withReauth(() => apiDelete(`/api/v1/team/members/${encodeURIComponent(userId)}`)),
  );
