# API Standards

_Status: draft._

- REST, versioned: `/api/v1/...`; plural resource names (`/conversations/{id}/messages`).
- `TenantId` always from the auth token, never from the request.
- Cursor pagination for lists; `Idempotency-Key` header on POST; rate limits per tenant.
- One Request/Response DTO pair per endpoint, as C# `record`s (`CreateContactRequest`, `ContactResponse`). Entities never leave the module.
- Validation with FluentValidation.
- OpenAPI spec is the contract; frontend `api-client` is generated from it. Breaking changes are checked in CI.

## Authentication & CSRF (dashboard)
- Session: HttpOnly cookie `__Host-nd_session` (ADR 0005, 0010); fetch with `credentials: 'include'`.
- Every POST/PUT/PATCH/DELETE under `/api` needs header `X-XSRF-TOKEN` from `GET /api/v1/csrf` (SEC-03). The token is bound to the user: fetch a new one after signing in or out. Keep it in memory, never in browser storage (SEC-14).
- `/api/v1/auth/*` is anonymous and rate-limited per IP (SEC-07, `429` + `Retry-After`); everything else requires a session.
- Signup, resend-verification and forgot-password always answer `202` without saying whether the email exists (SEC-26). Signup checks the password policy before looking up the email, so a rejected password cannot reveal registered emails either.
- Password reset revokes every session of the user; the user signs in again with the new password.
- Login answers `{ "mfaRequired": true }` for users with MFA; the dashboard then posts the code to `/api/v1/auth/login/mfa` within 5 minutes.
- Risky actions (role change, member removal, turning MFA off, new recovery codes) answer `403 auth.reauth_required` unless the user signed in or re-entered the password in the last 10 minutes (ADR 0015).

## Realtime (dashboard)
- SignalR hub `/api/v1/hubs/inbox` (ADR 0016), signed-in members only. The negotiate request is a POST, so it needs `X-XSRF-TOKEN` like any other change.
- Events carry IDs only (`conversationChanged` → `{ conversationId }`); the dashboard reloads the data through the REST API.
- `mentioned` → `{ conversationId, noteId }` goes only to the mentioned member's own connections in that workspace (ADR 0021).
