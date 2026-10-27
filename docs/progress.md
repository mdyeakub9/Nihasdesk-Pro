# Progress Log

Weekly, 3–5 lines: done, blocked, decisions.

## 2026-W44
- Phase 1 PRD open questions answered (PRD v0.4).
- Decided: AI provider is OpenAI with European data residency (ADR 0006); dashboard in Italian + English; owner alerts via daily email + web push.
- Next: Meta Business Verification, customer interviews in Italy, Figma screens.
- Coding started: Issue #1 backend foundation (solution, SharedKernel, empty modules, health checks, architecture tests, docker-compose, CI).
- Issue #2 frontend foundation: pnpm workspace, dashboard shell (React + Vite), ui and i18n packages, frontend CI. Decided: TanStack Query + Zustand, no Redux (ADR 0007).
- Dashboard UI from the Figma app designs: Figma tokens in `packages/ui`, app shell, Inbox and Notifications screens on mock data (dev only). Left out as out of Phase 1 scope: comments, bookings, broadcasts, follow-ups, saved replies, agent "Create order". Added from the PRD: "Return to AI", order-request and hot-lead notifications.

## 2026-W43
- Repository created; folder structure and planning docs added (brief, architecture, AI agent, standards, ADRs).
- Decided: e-commerce first; flexible core with vertical packs (ADR 0003); Shopify/Woo moved to Phase 2, Phase 1 uses manual + CSV catalog.
- Phase 1 PRD drafted (`docs/prd/phase-1-mvp.md`). Decided: AI takes orders as requests with option buttons; a human approves before an order is created.
- Decided: payment is cash on delivery only in Phase 1; Stripe Connect later (ADR 0004).
- Decided: internal testing first, then beta billed by manual invoice; Stripe Billing before public launch.
- Added security standard (`docs/standards/security.md`, SEC-01…49) and ADR 0005 (cookie auth).
- Next: answer PRD open questions, Meta Business Verification, customer interviews in Italy, Figma screens.
