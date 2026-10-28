# ADR 0015: MFA optional during beta, password re-authentication for risky actions

Date: 2026-10-28 · Status: Accepted (revisit before public launch)

## Context
SEC-09 makes TOTP MFA mandatory for Owners; ADR 0010 asks for re-authentication (password + MFA) before sensitive actions. The owner wants sign-in to stay simple for the first beta shops. Nihasdesk has no real customer data yet.

## Decision
- **MFA (TOTP) is available to everyone and optional for everyone, Owners included**, during the beta. Owners without MFA see a dismissible reminder in the dashboard. Nihasdesk internal admins (separate admin area, SEC-27) stay MFA-mandatory when that area is built.
- When a user has enabled MFA, sign-in asks for the 6-digit code (or a one-time recovery code) after the password; wrong codes count toward the normal lockout.
- The authenticator secret is stored **encrypted** (Data Protection) and the **10 recovery codes only as SHA-256 hashes**, each usable once.
- **Re-authentication = password only**, valid **10 minutes**; a fresh sign-in counts. Required for: changing a member's role, removing a member, turning MFA off and regenerating recovery codes. Changing the password asks for the current one instead. Sending invitations does not require it.
- Users can list their sessions, sign out one, or sign out all others; changing the password signs out all other sessions.

## Consequences
Lower friction for the beta, at the cost of weaker protection for Owner accounts, which can reach all of a shop's customer data. **Before public launch** we decide again whether MFA becomes mandatory for Owners (SEC-09 as written). SEC-09 is annotated accordingly.
