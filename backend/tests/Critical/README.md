# Critical tests

Non-negotiable business rules. Never modify or delete without owner approval.

- `TenantIsolation/` — CLAUDE.md rule 1 / SEC-11. Runs against a real PostgreSQL in Docker (Testcontainers), migrated as the owner and queried as the app role, like production. Requires Docker (Docker Desktop locally; available on GitHub runners).
