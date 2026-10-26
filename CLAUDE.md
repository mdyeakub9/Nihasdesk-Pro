# CLAUDE.md — Nihasdesk

Read this first in every session. Keep it short; details live in `docs/`.

## What Nihasdesk is
A subscription SaaS: one unified inbox for Messenger, Instagram, WhatsApp, website chat, Shopify and WooCommerce, where an AI agent and human team members answer customers together. Europe-first (start: Italy). Goal: business owners (shops, salons, agencies) no longer need to be online 24/7, and every conversation is turned into a known lead or customer.

Full context: `docs/project-brief.md`.

## Status
Phase 1 foundation. Backend skeleton exists (solution, SharedKernel, empty modules, health checks, architecture tests); no business features yet. Do not scaffold projects or write code unless the task (GitHub Issue) explicitly asks for it.

## Stack
- Backend: .NET (ASP.NET Core), modular monolith, EF Core code-first, PostgreSQL (+ pgvector), Redis, RabbitMQ + MassTransit, SignalR
- Frontend: pnpm monorepo — React (Vite) dashboard, Next.js marketing site, chat widget; Tailwind + shadcn/ui
- Hosting: Hetzner (EU) + Coolify, Docker
- AI: provider-agnostic `ILlmProvider`; cheap model for routing/classification, stronger model for replies

## Repository layout
```
backend/src/Nihasdesk.Api        host, middleware
backend/src/Nihasdesk.Workers    webhooks, broadcasts, background jobs
backend/src/BuildingBlocks       SharedKernel, Infrastructure
backend/src/Modules/<Module>     Identity, Contacts, Inbox, Channels, AiAgent, Commerce, Shipping, Billing
backend/tests                    Critical, Architecture, Modules
frontend/apps                    web, marketing, widget
frontend/packages                ui, api-client, i18n
infra                            docker-compose, Coolify
docs                             brief, prd, standards, adr, progress
```
Module internal layout: `Domain/`, `Features/<UseCase>/` (Endpoint, Request, Response, Validator, Handler), `Infrastructure/`, `Contracts/`, `<Module>Module.cs`.

## Non-negotiable rules
1. **Tenant isolation.** Every tenant-owned table has `TenantId`. `TenantId` comes only from the authenticated context — never from request bodies or from AI output. EF global query filters + PostgreSQL RLS. Why: one leak between businesses ends the company.
2. **Module boundaries.** Modules talk only through `Contracts/` or integration events. Each module has its own DbContext and DB schema.
3. **Never edit or delete files in `backend/tests/Critical/`** and never weaken a failing test to make it pass. Fix the code, or stop and ask.
4. **Migrations:** never edit a migration that has been applied to staging/production; no destructive migration (drop/rename column/table) without the expand → migrate → contract process. Always show migration files for review.
5. **AI agent:** prices, stock, order status and availability come only from tools, never from the model. The AI never creates a confirmed order — it creates an order request that a human approves. Tool code enforces permissions and tenant scope. Disclose AI to end customers (EU AI Act).
6. **Secrets** never in the repo. `.env` is git-ignored; document variables in `.env.example`.
7. **GDPR:** send the AI provider only the data needed for the reply; customer data stays in the EU.
8. **Security standard is mandatory.** Read `docs/standards/security.md` before any work on endpoints, auth, uploads, outbound HTTP, webhooks, AI tools, logging or infrastructure. Reference the `SEC-xx` rules you applied in the PR. If a rule can't be followed, stop and ask.

## How to work
- One GitHub Issue = one session = one branch (`feat/...`, `fix/...`) = one PR.
- Before changing anything: give a plan and the list of files you will touch. Wait for approval.
- Stay inside the folders the Issue names. Small diffs.
- Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`).
- If a decision isn't covered by the docs, ask — then record it as an ADR in `docs/adr/`.

## Which doc to read
| Task touches | Read |
|---|---|
| Product scope, priorities | `docs/project-brief.md`, `docs/prd/` |
| Architecture, modules, infra | `docs/architecture.md` |
| API endpoints, DTOs | `docs/standards/api.md` |
| Errors | `docs/standards/errors.md` |
| Database, migrations | `docs/standards/database.md` |
| AI agent, tools, prompts | `docs/ai-agent.md` |
| Integrations (channels, shipping, commerce) | `docs/standards/integrations.md` |
| UI | `docs/standards/design-system.md` |
| Git, PRs, CI | `docs/standards/git-workflow.md` |
| Security (always for endpoints, auth, uploads, webhooks, AI tools, infra) | `docs/standards/security.md` |
