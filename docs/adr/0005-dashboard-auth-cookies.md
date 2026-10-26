# ADR 0005: Dashboard authentication with HttpOnly cookies

Date: 2026-10-23 · Status: Accepted

## Decision
The dashboard (`app.` subdomain) authenticates to the API with ASP.NET Core Identity **HttpOnly, Secure, SameSite cookies**, not bearer tokens in browser storage. All state-changing requests require antiforgery tokens. MFA (TOTP) is mandatory for Owners and internal admins. JWTs are used only where cookies don't fit (service-to-service, later the widget, signed customer links), with asymmetric keys.

## Consequences
Tokens can't be stolen by XSS from localStorage; CSRF protection becomes required (SEC-03). The mobile app later will need its own token flow (separate ADR).
