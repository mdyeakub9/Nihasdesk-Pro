# ADR 0014: Team invitations — 48-hour links, one workspace per person

Date: 2026-10-28 · Status: Accepted

## Context
Owners and Admins invite team members by email (PRD 4.1). SEC-26 limits reset and invitation tokens to one hour; a new team member often opens the email later than that. Phase 1 has one workspace per person (ADR 0003) and no workspace switcher.

## Decision
- **Invitation links are valid for 48 hours** (password reset stays at 1 hour). In exchange the token is: 256-bit random, **stored only as a SHA-256 hash**, single use, revoked by resend or by an Owner/Admin, and only usable for the invited email's new account. SEC-26 is updated to say so.
- **Accepting creates the account**: name and password (password policy incl. HIBP, ADR 0009); the email counts as verified because the link was delivered to it; the person is signed in.
- **An email that already has an account can't accept** in Phase 1 (clear message: multi-workspace comes later). The check happens at accept time.
- The invitation lookup by token runs through a narrow `SECURITY DEFINER` function, like the login membership lookup, because the tenant isn't known before the token is.
- Invitations are tenant-owned and isolated with RLS; their actions are audited.

## Consequences
Fewer "the link expired" round trips for Owners. A leaked invitation email can be used for up to 48 hours, mitigated by single use, revocation and the audit log. Multi-workspace membership needs its own decision (accepting with an existing account, choosing the active workspace at login).
