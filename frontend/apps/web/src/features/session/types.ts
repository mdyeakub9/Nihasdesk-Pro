/** Roles from PRD 4.1 / ADR 0011. */
export type Role = 'owner' | 'admin' | 'member';

export type Session = {
  user: { name: string; role: Role };
  workspace: { name: string };
  /** Onboarding progress (PRD 4.2). */
  setup: { completed: number; total: number };
};
