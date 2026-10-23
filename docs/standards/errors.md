# Error Handling

_Status: draft._

- All errors as RFC 9457 Problem Details.
- Stable error code catalog: `<area>.<reason>` (e.g. `conversation.not_found`, `credits.exhausted`). Frontend maps codes to translated messages.
- Expected failures → `Result`/`Error`; unexpected → exceptions handled by global middleware.
- Every error response includes a `traceId`. Never expose stack traces.
