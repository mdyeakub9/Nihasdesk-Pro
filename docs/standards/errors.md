# Error Handling

_Status: draft._

- All errors as RFC 9457 Problem Details.
- Stable error code catalog: `<area>.<reason>` (e.g. `conversation.not_found`, `credits.exhausted`). Frontend maps codes to translated messages.
- Expected failures → `Result`/`Error`; unexpected → exceptions handled by global middleware.
- Every error response includes a `traceId`. Never expose stack traces.

## Catalog

| Code | Status | Meaning |
|---|---|---|
| `validation.failed` | 400 | Request failed validation; `errors` lists the fields |
| `csrf.invalid` | 400 | Missing or invalid `X-XSRF-TOKEN` (SEC-03) |
| `auth.invalid_credentials` | 401 | Wrong email or password, or the account is locked — deliberately one answer (SEC-26) |
| `auth.email_not_verified` | 403 | Correct password, email not confirmed yet |
| `auth.invalid_token` | 400 | Verification or reset link invalid, used or expired (later also invitations) |
| `auth.no_workspace` | 403 | Account has no workspace membership |
| `auth.weak_password` | 400 | Password rejected by the password policy (ADR 0009) |
| `auth.breached_password` | 400 | Password found in Have I Been Pwned (ADR 0009) |
| `team.forbidden` | 403 | The member's role lacks the permission (docs/standards/permissions.md) |
| `team.not_found` | 404 | Invitation or member not in this workspace — also for other workspaces' IDs (SEC-06) |
| `team.already_member` | 409 | The invited email is already in the team |
| `team.owner_is_fixed` | 400 | The Owner can't be removed or have their role changed |
| `team.invitation_invalid` | 404 | Invitation link unknown, used, revoked or expired |
| `team.email_has_account` | 409 | The invited email already has an account (one workspace per person in Phase 1, ADR 0014) |
