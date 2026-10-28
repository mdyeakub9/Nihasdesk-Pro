/** Roles from PRD 4.1 / ADR 0011. */
export type Role = 'owner' | 'admin' | 'member';

/** `GET /api/v1/me` (and the login / verify-email responses). */
export type Session = {
  user: {
    id: string;
    name: string;
    email: string;
    language: string;
    emailVerified: boolean;
    /** Two-step sign-in on (ADR 0015); Owners without it see a reminder. */
    mfaEnabled: boolean;
  };
  workspace: { id: string; name: string };
  role: Role;
};
