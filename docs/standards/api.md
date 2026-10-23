# API Standards

_Status: draft._

- REST, versioned: `/api/v1/...`; plural resource names (`/conversations/{id}/messages`).
- `TenantId` always from the auth token, never from the request.
- Cursor pagination for lists; `Idempotency-Key` header on POST; rate limits per tenant.
- One Request/Response DTO pair per endpoint, as C# `record`s (`CreateContactRequest`, `ContactResponse`). Entities never leave the module.
- Validation with FluentValidation.
- OpenAPI spec is the contract; frontend `api-client` is generated from it. Breaking changes are checked in CI.
