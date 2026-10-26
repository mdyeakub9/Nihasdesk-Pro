# Security Standard

_Status: v1 · 2026-10-23 · Mandatory for all code._

Every rule has an ID (`SEC-xx`) so Issues, PRs and tests can reference it. **If a rule cannot be followed, stop and ask** — do not work around it. A deviation needs an ADR.

Threat model in one paragraph: Nihasdesk stores many businesses' customer conversations, personal data (names, phones, addresses) and access tokens to their Meta/WhatsApp accounts. The worst outcomes are (1) one business seeing another's data, (2) stolen channel tokens used to message a shop's customers, (3) leaking end-customer personal data (GDPR breach), (4) the AI being tricked into wrong actions, (5) abuse that runs up AI/WhatsApp costs.

---

## 1. Input & injection

- **SEC-01 SQL injection.** Data access only through EF Core LINQ or parameterized queries. `FromSqlRaw`/string-built SQL is forbidden; if raw SQL is unavoidable use `FromSqlInterpolated`/parameters and get review. Dynamic sorting/filtering uses allow-listed column names.
- **SEC-02 XSS.** All user, customer and AI content is untrusted. React escapes by default; `dangerouslySetInnerHTML` is forbidden unless content is sanitized with DOMPurify (required later for email HTML). AI/markdown output is rendered with a safe renderer (no raw HTML). Links get `rel="noopener noreferrer"`; only `http(s)` and `mailto`/`tel` URLs are clickable.
- **SEC-03 CSRF.** Dashboard auth uses cookies (SEC-14), so every state-changing request (POST/PUT/PATCH/DELETE) requires ASP.NET Core antiforgery token validation. GET requests never change state.
- **SEC-04 File uploads** (CSV, PDF, DOCX, images, voice, Meta media):
  - Allow-list file types; verify by content (magic bytes), not by extension or client `Content-Type`.
  - Size limits per type (configured, enforced server-side and at the reverse proxy); row/page limits for CSV/PDF.
  - Store in private object storage under a random key; never in the web root; never use the client filename as a path.
  - Serve only via short-lived signed URLs with `Content-Disposition` and correct `Content-Type`; never serve user-uploaded SVG/HTML inline.
  - Images are re-encoded (strips EXIF/GPS and payloads). Documents are parsed in a background worker with timeouts and memory limits.
  - Malware scanning (e.g. ClamAV) for documents before processing.
- **SEC-05 SSRF.** Applies to website import, any URL fetched from user input, and later the custom API tool. All outbound requests to user-supplied URLs go through one hardened `SafeHttpClient`:
  - only `https` (and `http` if needed), ports 80/443;
  - resolve DNS and **block private, loopback, link-local, multicast and cloud metadata ranges** (`10/8`, `172.16/12`, `192.168/16`, `127/8`, `169.254/16`, `::1`, `fc00::/7`, `fe80::/10`, `100.64/10`, `0.0.0.0`), re-checked after every redirect and at connect time (DNS rebinding);
  - max 3 redirects, connect/read timeouts, max response size, no forwarding of our auth headers/cookies.
- **SEC-21 Validation everywhere.** Every request DTO has a FluentValidation validator (types, lengths, ranges, formats). Reject unknown enum values. Limit request body size and collection lengths.
- **SEC-22 Mass assignment.** Bind only to request DTOs, never to entities. Server-controlled fields (`TenantId`, `Id`, `Role`, `Status`, prices, totals) are never accepted from clients.
- **SEC-23 CSV/formula injection.** On CSV export, prefix cells starting with `= + - @ \t \r` with `'`. On import, treat all values as data.
- **SEC-24 Safe deserialization.** System.Text.Json only; no polymorphic type-name deserialization.
- **SEC-25 Open redirects.** Redirect targets (after login, checkout) must be relative paths or allow-listed hosts.

## 2. Authentication, authorization & data access

- **SEC-06 BOLA / IDOR.** Every endpoint that takes an ID loads the resource **through the tenant-scoped query** and checks the current user's permission for that resource. A resource from another tenant returns `404` (not `403`). Each endpoint has an authorization test including a "wrong tenant" case.
- **SEC-07 Rate limiting.** ASP.NET Core rate limiter: per IP for anonymous endpoints, per user and per tenant for authenticated ones. Strict limits on login, signup, password reset, invitation, email verification, checkout submission and file upload. Return `429` with `Retry-After`.
- **SEC-08 Password hashing.** ASP.NET Core Identity hasher (PBKDF2, high iteration count) or Argon2id. Never store or log plain passwords. Minimum length 12, check against breached-password lists, no composition rules.
- **SEC-09 MFA.** TOTP MFA available to all users; **mandatory for Owners and for Nihasdesk internal admins.** Recovery codes stored hashed.
- **SEC-10 Server-side authorization.** All permission checks happen in the API (policy-based, role + resource). The frontend hiding a button is never security. Roles: Owner, Admin, Agent (permission matrix in `docs/standards/permissions.md` when written).
- **SEC-11 Database RLS.** Tenant isolation in two layers: EF Core global query filters + PostgreSQL Row-Level Security using a per-connection tenant setting. The app DB user is not the table owner and cannot bypass RLS. Migrations run with a separate user.
- **SEC-26 Account security.** Email verification before going live; generic responses for login/reset ("if the account exists…") to prevent enumeration; lockout/backoff after failed logins; single-use, expiring (≤ 1 h), hashed reset and invitation tokens; all sessions revoked on password change; users can see and revoke sessions.
- **SEC-27 Internal admin & support access.** Nihasdesk staff access to a tenant's data is a separate, MFA-protected admin area, only with a recorded reason, time-limited, and written to the audit log. Never share production database credentials for support.

## 3. Secrets, tokens & sessions

- **SEC-12 Signing keys.** If JWTs are used (service-to-service, widget, signed links), keys are ≥ 256-bit random (HS256) or asymmetric (preferred: ES256/RS256), stored in the secret store, rotatable, with short lifetimes and validated `iss`, `aud`, `exp`, `alg` (never `none`).
- **SEC-13 Secrets server-side only.** OpenAI, Meta, WhatsApp, Stripe and storage keys live only on the server (Coolify env/secret store). Never in frontend code, the repo, logs, error messages or AI prompts. Frontend gets only public identifiers.
- **SEC-14 Sessions: HttpOnly cookies, no tokens in localStorage/sessionStorage.** Dashboard session cookie: `HttpOnly`, `Secure`, `SameSite=Lax` (or Strict), host-scoped, short idle timeout with sliding renewal, absolute lifetime. Session IDs are rotated at login and privilege change.
- **SEC-15 No default credentials.** Every service (Postgres, Redis, RabbitMQ, Coolify, object storage) gets unique strong credentials; default users disabled; nothing reused between staging and production.
- **SEC-28 Tenant integration credentials** (Meta page tokens, WhatsApp tokens, later Stripe/Shopify) are encrypted at rest with keys outside the database (ASP.NET Core Data Protection with protected key storage), decrypted only in the backend at use time, never returned by any API, and deleted when the integration is disconnected.
- **SEC-29 Signed public links.** Checkout-form and similar customer links use unguessable, expiring, single-purpose tokens tied to one order request; they reveal only that request's data.

## 4. Production & hygiene

- **SEC-16 Strict CORS.** Allow-list exact origins (`https://app.<domain>`, later the widget origin); no `*` with credentials; no reflected origins.
- **SEC-17 Webhook verification.** Verify Meta `X-Hub-Signature-256` (HMAC-SHA256 with app secret) and Stripe signatures on the **raw body** with constant-time comparison before processing; reject on failure. Protect against replay (timestamp/event-ID dedupe) and process idempotently.
- **SEC-18 No production source maps** publicly served (upload them only to error tracking if used). No debug endpoints, Swagger UI or detailed errors in production.
- **SEC-19 Log redaction.** Never log passwords, tokens, cookies, API keys, full message bodies, phone numbers, emails or addresses. Use structured logging with a redaction policy; log IDs, not personal data. Error tracking scrubs request bodies.
- **SEC-20 Dependencies.** Dependabot enabled; CI fails on known high/critical vulnerabilities (`dotnet list package --vulnerable`, `pnpm audit`); lockfiles committed; new dependencies need a reason in the PR.
- **SEC-30 Security headers.** HSTS (with preload once stable), Content-Security-Policy (no inline scripts), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `frame-ancestors 'none'` (except the widget later), `Permissions-Policy`.
- **SEC-31 TLS everywhere.** HTTPS only (TLS 1.2+); internal services on Hetzner private network; database, Redis, RabbitMQ never exposed to the internet.
- **SEC-32 Server & container hardening.** SSH keys only, no root/password login, Hetzner firewall (80/443 public; 22 only from admin IPs), automatic OS security updates, Coolify panel protected (MFA, IP restriction). Containers run as non-root with minimal base images; images scanned (e.g. Trivy) in CI.
- **SEC-33 Backups.** Nightly encrypted backups to separate object storage, retention policy, keys stored apart from backups, monthly restore test.
- **SEC-34 CI security checks.** Secret scanning + push protection, CodeQL (C#, TypeScript), dependency and container scans. A failing security check blocks merge.

## 5. AI agent security (Nihasdesk-specific)

- **SEC-35 Prompt injection.** Customer messages, imported web pages, documents and product descriptions are **untrusted input**, never instructions. The system prompt says so, but real protection is in code: tools enforce tenant, permissions and limits regardless of what the model asks.
- **SEC-36 Tool safety.** Allow-list of tools per workspace; strict JSON-schema validation of every tool argument; tool code takes `TenantId`, contact and conversation from `ToolContext`; no tool can create confirmed orders, change prices, give discounts, access other conversations or reveal internal data.
- **SEC-37 No secrets or other tenants' data in prompts.** Prompts contain only what the reply needs (GDPR minimization). Never include API keys, internal notes not meant for the AI, or data from other conversations/tenants.
- **SEC-38 Output checks.** Before sending: block replies that contain prices/totals not returned by tools in this turn, URLs not on the shop's allow-list, or internal identifiers. On failure → safe fallback or handoff.
- **SEC-39 Cost & abuse limits.** Per conversation: max tool-call loop iterations, max tokens, max AI replies per time window; per tenant: daily/monthly caps; detection of spam/flooding contacts (auto-pause AI for that contact). Protects against "denial of wallet".
- **SEC-40 AI logs are sensitive.** Stored prompts/responses are tenant-scoped personal data: access-controlled, retention-limited, and redacted in general logs. Use an AI provider project with EU data residency and no training on our data (DPA signed).

## 6. Business logic & abuse

- **SEC-41 Integrity of money and stock.** Totals are always recomputed on the server from catalog prices and shipping policy. Stock is re-checked and decremented atomically (transaction/row lock or optimistic concurrency) at order approval.
- **SEC-42 Fake/spam orders.** Rate-limit order requests per contact and per IP (checkout form), detect duplicates, CAPTCHA/turnstile on the public checkout form when abuse is detected.
- **SEC-43 Channel compliance.** Respect Meta's 24-hour window and template rules in code; never send bulk/unsolicited messages; honour opt-outs. Violations can get a shop's number or page banned.

## 7. Privacy (GDPR) & data lifecycle

- **SEC-44 Data minimization & retention.** Collect only fields the order template needs. Configurable retention for conversations and media; automatic deletion after retention.
- **SEC-45 Data subject rights.** Per-contact export and erasure (including media, AI logs and search embeddings); per-workspace full export and deletion on account closure.
- **SEC-46 Encryption.** Encryption in transit (SEC-31) and at rest for database volumes, backups and object storage. Highly sensitive fields (integration tokens) additionally application-encrypted (SEC-28).
- **SEC-47 Audit log.** Append-only log of security-relevant actions: logins, MFA changes, role changes, invitations, integration connect/disconnect, exports, deletions, order approvals, staff support access.

## 8. Monitoring & incidents

- **SEC-48 Monitoring & alerts.** Alerts on spikes in 401/403/429, failed webhook signatures, failed logins, unusual export volume, AI cost spikes, error-rate increases.
- **SEC-49 Incident response.** Written runbook: contain, rotate affected secrets, investigate with audit logs, notify affected businesses, and notify the supervisory authority within **72 hours** for personal-data breaches (GDPR Art. 33).

---

## PR security checklist (copy into PRs that touch these areas)
- [ ] New endpoint: validator, auth policy, tenant-scoped query, rate limit, "wrong tenant" test (SEC-06/07/10/21)
- [ ] No raw SQL / `dangerouslySetInnerHTML` / secrets in code (SEC-01/02/13)
- [ ] Uploads or outbound URLs go through the shared safe components (SEC-04/05)
- [ ] Logs contain no personal data or secrets (SEC-19)
- [ ] AI tool changes keep tenant/permission enforcement in code (SEC-35–38)
- [ ] New dependency justified; CI security checks green (SEC-20/34)
