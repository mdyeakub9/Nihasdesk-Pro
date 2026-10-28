# Progress Log

Weekly, 3–5 lines: done, blocked, decisions.

## 2026-W44
- Phase 1 PRD open questions answered (PRD v0.4).
- Decided: AI provider is OpenAI with European data residency (ADR 0006); dashboard in Italian + English; owner alerts via daily email + web push.
- Next: Meta Business Verification, customer interviews in Italy, Figma screens.
- Coding started: Issue #1 backend foundation (solution, SharedKernel, empty modules, health checks, architecture tests, docker-compose, CI).
- Issue #2 frontend foundation: pnpm workspace, dashboard shell (React + Vite), ui and i18n packages, frontend CI. Decided: TanStack Query + Zustand, no Redux (ADR 0007).
- Dashboard UI from the Figma app designs: Figma tokens in `packages/ui`, app shell, Inbox and Notifications screens on mock data (dev only). Left out as out of Phase 1 scope: comments, bookings, broadcasts, follow-ups, saved replies, agent "Create order". Added from the PRD: "Return to AI", order-request and hot-lead notifications.
- Identity decisions recorded: Brevo email behind `IEmailSender` with Mailpit locally (ADR 0008), password policy with HIBP check (ADR 0009), server-side sessions with "remember me" (2 h idle / 12 h without it) and re-auth (ADR 0010), email via one SMTP adapter (Brevo relay / Mailpit), roles Owner / Admin / Member (ADR 0011).
- Identity tenancy foundation: organizations, workspaces (TenantId = workspace), global users, memberships; tenant isolation with EF query filters + write guard and PostgreSQL RLS; app connects as non-owner `nihasdesk_app`; first migration; Critical tenant isolation tests on real PostgreSQL (Testcontainers).
- Identity auth (PR 2a): signup → email verification → login/logout/me with server-side sessions (2 h idle / 12 h, remember me 14 d / 30 d), CSRF tokens, per-IP rate limits, lockout, SMTP email (Mailpit locally), login workspace lookup via a narrow SECURITY DEFINER function.
- Identity password & audit (PR 2b): forgot/reset password (1 h token, single use, revokes all sessions, notice email), HIBP breached-password check (k-anonymity, fail-open), append-only audit log with IP and 12-month retention (ADR 0012). Fixed: signing up or in while already signed in returned 500.
- Email outbox (ADR 0013): API queues email in the module transaction (encrypted), Workers delivers with SKIP LOCKED, 8 retries over ~1 day, rows deleted after sending. Closes the response-time enumeration gap. Workers host now loads modules (`IModule.AddWorkerServices`).
- Dashboard auth: login, signup, check-email, verify-email, forgot/reset password pages (React Hook Form + Zod, it/en errors), real session from `/api/v1/me` with route guards and logout, CSRF-aware API client, Vite dev proxy for same-origin cookies. Mock session removed.
- Team (PR 3a backend): permission matrix (`docs/standards/permissions.md`), invitations (48 h, hashed single-use tokens, ADR 0014), team list, role changes, member removal with session revocation, audit events; public preview/accept creates the account. Re-auth and MFA follow in 3b.

## 2026-W43
- Repository created; folder structure and planning docs added (brief, architecture, AI agent, standards, ADRs).
- Decided: e-commerce first; flexible core with vertical packs (ADR 0003); Shopify/Woo moved to Phase 2, Phase 1 uses manual + CSV catalog.
- Phase 1 PRD drafted (`docs/prd/phase-1-mvp.md`). Decided: AI takes orders as requests with option buttons; a human approves before an order is created.
- Decided: payment is cash on delivery only in Phase 1; Stripe Connect later (ADR 0004).
- Decided: internal testing first, then beta billed by manual invoice; Stripe Billing before public launch.
- Added security standard (`docs/standards/security.md`, SEC-01…49) and ADR 0005 (cookie auth).
- Next: answer PRD open questions, Meta Business Verification, customer interviews in Italy, Figma screens.
