import type { Session } from '../features/session/types';

/** Signed-in user until the Identity module exists (dev and tests only). */
export const mockSession: Session = {
  user: { name: 'Md Yeakub', role: 'owner' },
  workspace: { name: 'Almanaam' },
  setup: { completed: 5, total: 8 },
};
