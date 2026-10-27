# ADR 0010: Dashboard sessions, "remember me" and re-authentication

Date: 2026-10-27 · Status: Accepted

## Context
ADR 0005 fixes cookie authentication; SEC-14 and SEC-26 require short-lived, revocable sessions. Shop staff often share devices, owners want to stay signed in on their own laptop.

## Decision
- **Server-side sessions.** The cookie carries only a random session reference; the session itself is stored on the server (ASP.NET Core `ITicketStore`). Sessions can be listed and revoked by the user, by an Owner/Admin for their team, and are all revoked on password change (SEC-26). Session IDs are rotated at login and on privilege change (SEC-14).
- **"Remember me"** checkbox on login, **off by default**:
  - **Off:** browser-session cookie (no `Expires`), server-side absolute lifetime **12 hours**.
  - **On:** persistent cookie with **sliding expiration of 14 days** and an **absolute lifetime of 30 days**; after 30 days the user signs in again.
- **Re-authentication for sensitive actions:** the user confirms with their password, plus a TOTP code when MFA is enabled (always for Owners, SEC-09), and the confirmation is valid for a short window only. Sensitive actions include: changing email, password or MFA, managing team members and roles, connecting or disconnecting channels, data export and deletion, and billing changes. The exact list lives with the permission matrix (`docs/standards/permissions.md`).
- Cookie flags stay as in ADR 0005 / SEC-14 (`HttpOnly`, `Secure`, `SameSite`, host-scoped).

## Consequences
Revocation works immediately because every request checks the server-side session; this costs one store lookup per request (cache-friendly). The session store needs cleanup of expired entries. Re-authentication needs its own endpoint and UI dialog, and the audit log records it (SEC-47).
