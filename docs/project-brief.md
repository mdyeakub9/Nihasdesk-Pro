# Nihasdesk — Project Brief

_Last updated: 2026-10-23 (vertical: e-commerce first, see ADR 0003). Source of truth for vision and scope. Update it when decisions change._

## 1. Vision
One inbox where a business's customers are answered day and night — by an AI agent that knows the business, and by the team when a human is needed.

## 2. The problem
- Business owners (e-commerce shops, salons, clinics, agencies) must stay online all the time to answer messages, or they lose sales.
- Conversations are scattered across Messenger, Instagram, WhatsApp, website chat, email. It's hard to know **who is a lead and who is already a customer**.
- Small teams can't afford a night shift.

## 3. Target market
- **Europe first, starting with Italy.** One country + one vertical at a time.
- **First vertical: e-commerce** (small/medium online shops selling via Instagram, Facebook, WhatsApp). Core stays business-type agnostic via vertical packs (ADR 0003).
- Next: agencies, then booking businesses (salons, clinics), tourism.
- Later: agencies (white-label), other EU countries.

## 4. Core promise
1. AI agent replies in the customer's language, 24/7, using only the business's own information.
2. All channels in one inbox; one contact per real person across channels.
3. AI classifies every conversation (new lead, hot lead, existing customer, support, spam) and hands off to a human with a reason when needed.
4. A morning summary: what happened overnight, which leads need a call.

## 5. Feature map (full vision)
- **Inbox & team:** unified inbox, assignment, internal notes, tags, statuses, SLA timers, collision detection, canned replies, auto-translate, multi-member workspaces, roles.
- **AI agent:** knowledge base (docs, website, catalog), tools (search products, order status, booking, create lead, handoff), confidence + handoff, intent/sentiment, voice and image understanding, suggested replies for agents, owner instructions in plain language with test-before-live and versioning.
- **Commerce:** Shopify/WooCommerce sync (products, stock, orders), order taking in chat, payment links, order tracking via a shipping aggregator, abandoned cart, returns.
- **Bookings:** availability, booking, reminders.
- **CRM:** unified profiles, identity resolution (merge by phone/email), custom fields, segments, lead scoring, pipeline.
- **Marketing:** broadcasts (respecting channel rules), sequences, comment-to-DM, keyword triggers.
- **Automation:** flow builder, webhooks, public API, business hours.
- **Analytics:** AI resolution rate, response times, CSAT, revenue attribution.
- **Platform:** plan limits, usage-metered AI credits, audit log, GDPR export/delete, agency mode, mobile app.

## 6. Roadmap (phases)
| Phase | Scope |
|---|---|
| **1 — MVP (e-commerce)** | Auth + organization/workspace + team; Messenger, Instagram, WhatsApp (official Cloud API); unified inbox; native product catalog (manual + CSV import); knowledge base; AI agent with tools (search_products, get_product_options, search_knowledge, create_lead, create_order_request, handoff_to_human); AI takes orders as requests, a human approves; payment: cash on delivery only; contact merge; AI usage metering. See `docs/prd/phase-1-mvp.md` |
| 2 | Shopify + WooCommerce sync into the native catalog (products, stock, orders); website chat widget; lead pipeline |
| 3 | Online payments via Stripe Connect (payment links after approval; more methods later); shipping tracking; broadcasts; comment automation; agency multi-workspace |
| 4 | Booking pack (salons/clinics); email channel; mobile app; white-label; advanced analytics |

Web app first, mobile app later.

## 7. Differentiation
- vs **AiPraktor** (Bangladesh, Meta-only, unofficial WhatsApp, pay-as-you-go): Nihasdesk adds website/Shopify/WooCommerce, official WhatsApp API (broadcast-capable, no ban risk), inventory/order sync, EU focus.
- vs big global tools: simpler onboarding (live in ~10 minutes), strong UX, EU data residency and GDPR by default, local languages and integrations.
- Note: Meta now offers its own "Business Agent" AI — position Nihasdesk on multichannel + commerce + team workflow, not on "AI replies" alone.

## 8. Architecture summary
See `docs/architecture.md`. Modular monolith (.NET), PostgreSQL (schema per module, RLS, pgvector), Redis, RabbitMQ, SignalR, React/Next.js frontend, Hetzner + Coolify. Webhooks are stored raw and acknowledged immediately, then processed from a queue (idempotent). Channel adapter pattern normalizes all channels into one message format.

## 9. AI approach
See `docs/ai-agent.md`. Key points:
- The model has no memory; Nihasdesk stores everything and sends a small, selected context per call (instructions, recent messages, summary of older history, contact profile, relevant knowledge snippets, tool list).
- AI never invents facts; it calls tools implemented in our backend.
- Two-tier models: cheap model for classification/simple replies, stronger model for complex conversations.
- Prompt caching, per-workspace token metering, eval sets before changing prompts/models.

## 10. Costs & pricing (estimates, validate in beta)
- AI cost ≈ €0.01–0.012 per conversation with a mixed cheap/strong model setup (~50k tokens/conversation).
- **WhatsApp:** since 1 Oct 2026 Meta charges service (reply) messages after 1,000 free/number/month, at the utility rate per country. Each business connects its own WhatsApp Business account and pays Meta directly.
- Messenger/Instagram: no per-message Meta fee.
- Infra at start: ~€100–150/month (Hetzner staging + production + backups).
- Draft plans: Starter €39 (500 AI conversations), Pro €99 (2,000), Business €249 (6,000); extra AI credits sold separately. Target AI cost ≤ 15–25% of plan price.

## 11. Compliance
GDPR (DPA, data minimization, export/delete), EU data residency, EU AI Act disclosure, Meta platform policies (24-hour window, templates, App Review, Business Verification).

## 12. Non-technical prerequisites (start early)
- Meta Business Verification + App Review, WhatsApp Cloud API setup
- Domain(s), trademark check (EUIPO), social handles
- Company/tax setup, Stripe, Terms & Privacy Policy
- Talk to 5–10 target customers in Italy before building Phase 1 features

## 13. Open decisions
- AI provider(s) and EU-region hosting choice
- Final pricing and trial model
- Shipping aggregator (AfterShip / 17TRACK / other)
