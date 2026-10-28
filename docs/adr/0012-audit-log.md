# ADR 0012: Audit log — content, ownership and retention

Date: 2026-10-28 · Status: Accepted

## Context
SEC-47 asks for an append-only log of security-relevant actions. Some of them (failed logins) happen before a tenant is known; detecting abuse needs the client IP, which is personal data under GDPR.

## Decision
- **Table `identity.audit_events`** (owned by the Identity module for now): time, action (`area.verb`, e.g. `auth.login_failed`), user ID and tenant ID when known, **client IP address**. Nothing else from the request is stored (no email, user agent, body).
- **Append-only, enforced by the database:** the app role has `INSERT` and `SELECT` only; `UPDATE`, `DELETE` and `TRUNCATE` are revoked. A Critical test proves it.
- **Written in its own scope/connection**, so events survive a rolled-back request transaction.
- **Retention: 12 months**, then deleted by a scheduled job running as a separate, privileged role (to be built in Workers). Legal basis: legitimate interest in account security (GDPR Art. 6(1)(f)); documented in the privacy notice.
- **Other modules** (team changes, exports, order approvals) will write through an `IAuditLog` in Identity's `Contracts/` when they need it, instead of their own tables.

## Consequences
Failed-login spikes per IP and per account can be detected (SEC-48). The IP makes the log personal data, so access is limited to the app and internal admins, and the 12-month cleanup job must exist before public launch. Until it does, nothing is deleted.
