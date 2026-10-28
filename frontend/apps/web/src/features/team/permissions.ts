import type { Role } from '../session/types';

/**
 * Mirror of docs/standards/permissions.md, used only to hide what a role can't do.
 * The API enforces the same table on every request (SEC-10).
 */
export type TeamPermission = 'invite' | 'inviteAdmin' | 'changeRole' | 'removeMember' | 'removeAdmin';

const matrix: Record<Role, TeamPermission[]> = {
  owner: ['invite', 'inviteAdmin', 'changeRole', 'removeMember', 'removeAdmin'],
  admin: ['invite', 'removeMember'],
  member: [],
};

export function can(role: Role, permission: TeamPermission): boolean {
  return matrix[role].includes(permission);
}

/** Whether `actor` may remove a member with role `target` (the Owner never can be). */
export function canRemove(actor: Role, target: Role): boolean {
  if (target === 'owner') return false;
  return can(actor, target === 'admin' ? 'removeAdmin' : 'removeMember');
}
