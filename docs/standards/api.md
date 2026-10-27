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
- Signup and resend-verification always answer `202` without saying whether the email exists (SEC-26).
