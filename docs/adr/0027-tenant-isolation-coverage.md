# ADR 0027: Tenant isolation coverage for all modules, and the two RLS exceptions

Date: 2026-11-04 · Status: Accepted

## Context
Before AI brain v2 adds new tables (ADR 0026), every module with tenant data needs the same Critical tests Identity has (Issue #49, CLAUDE.md rule 1, SEC-06, SEC-11). A new test checks every table with a `tenant_id` column for RLS and the `tenant_isolation` policy. It found two Identity tables without the policy. Both were built that way on purpose, but this was not written down.

## Decision
- `backend/tests/Critical/TenantIsolation/` has one isolation test class per module (Contacts, Inbox, Channels, AiAgent, Commerce, Shipping) on a real PostgreSQL as `nihasdesk_app`. EF filter, RLS with the filter bypassed, inserts for another tenant, the AI's contracts, team notes out of the AI history, and AI orders only pending.
- `RlsCoverageTests` fails for any table with `tenant_id` that has no RLS policy, except this explicit list:
  - **`identity.sessions`**: the session is read from the cookie before any tenant is known, so a tenant policy would block sign-in. It is always loaded by session ID and user ID (ADR 0010), and removing a member deletes only that workspace's sessions.
  - **`identity.audit_events`**: it also stores events without a tenant (e.g. `auth.login_failed`), so `tenant_id` is nullable. The app may only insert (SEC-47, ADR 0012). It is not readable through any endpoint yet.
- Adding a table to the list needs the owner's approval and an ADR. A second test fails if a listed table no longer exists, so the list can't grow stale.

## Consequences
- A new tenant table without `EnableTenantRowLevelSecurity` fails CI.
- If an audit log screen per workspace is built, `audit_events` needs a read policy first (rows with the reader's tenant only). That is a separate ADR.
