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
| `auth.invalid_token` | 400 | Verification (later: reset/invitation) link invalid or expired |
| `auth.no_workspace` | 403 | Account has no workspace membership |
| `auth.weak_password` | 400 | Password rejected by the password policy (ADR 0009) |
