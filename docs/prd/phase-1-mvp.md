# PRD — Phase 1 MVP (E-commerce)

_Status: draft v0.5 · 2026-10-27 · Owner: Yeakub_
_Related: `docs/project-brief.md`, ADR 0003._

## 1. Problem
Owners of small online shops in Italy sell through Instagram, Facebook and WhatsApp. Customers ask the same questions all day and night (price, size, colour, stock, delivery, returns). If nobody answers quickly, the sale is lost. Owners can't be online 24/7, and messages are spread across three apps, so leads get forgotten.

## 2. Target user
- **Primary:** owner of an Italian online shop (fashion, beauty, jewellery, home), team of 1–10, 30+ messages/day, selling mainly via Instagram/Facebook/WhatsApp.
- **Secondary:** shop team members who answer messages.
- **End customer:** the shop's customer writing in Italian, English or another EU language.

## 3. Goal of Phase 1
A shop owner connects their channels, adds products and shop info, and within ~15 minutes an AI agent answers customers correctly in their language — and calls a human when needed. Every conversation ends up as a known contact/lead.

## 4. In scope

### 4.1 Account & team
- Sign up, log in, password reset, email verification.
- Organization → Workspace (one workspace per shop in Phase 1; model supports many — ADR 0003).
- Invite team members by email. Roles: **Owner**, **Admin**, **Member** (ADR 0011; "Agent" means only the AI agent).

### 4.2 Onboarding
- Steps: business info (name, country, languages, timezone, business hours) → connect channels → add products → shop policies → AI settings → test chat → go live.
- Business type selection exists, but only "Online shop" is available (others shown as "coming soon" or hidden).
- Progress is saved; user can leave and resume.

### 4.3 Channels
- Facebook Messenger, Instagram DM, WhatsApp (official Cloud API, business brings its own WABA).
- Text, images, voice notes received; text, images, buttons/quick replies sent where the channel supports them.
- Respect Meta rules (24-hour window, templates for WhatsApp outside the window).

### 4.4 Unified inbox
- All conversations in one list; filters by channel, status (waiting / open / done, ADR 0016), unread, assignee, AI vs human, tag.
- Conversation view with full history, contact panel, internal notes.
- Real-time updates; unread counts; "who is viewing" indicator.
- A team member can **take over** from AI with one click and **return to AI**.

### 4.5 Contacts & leads
- One contact per person; auto-created on first message.
- Merge contacts (manual; suggested merge when phone/email match).
- AI classification per conversation: new lead, hot lead, customer, support, spam (editable by team members).
- Contact fields: name, phone, email, language, channels, tags, notes, products of interest.
- Simple lead list with filters (no pipeline board yet).

### 4.6 Product catalog (native)
- Add/edit products manually: name, code/SKU, description, category, price, currency, images, variants (e.g. size, colour) with optional stock quantity, active/inactive.
- **CSV import** with a downloadable template, preview, validation errors per row, and update-by-SKU.
- Catalog is the only source of prices and stock for the AI.

### 4.7 Knowledge base
- Shop policies as structured fields: shipping (zones, costs, times), returns, payment methods, contact info.
- Free-text FAQ entries; upload PDF/DOCX; add a website URL to import text (single pages).
- Owner can see which sources were used in an AI answer.

### 4.8 AI agent
- Replies in the customer's language; tone setting (formal/friendly); custom instructions in plain language.
- Tools: `search_products`, `get_product_options`, `search_knowledge`, `create_lead`, `create_order_request`, `handoff_to_human`.
- Never invents prices, stock or policies; if unsure → asks a clarifying question or hands off.
- Handoff: reason recorded (complaint, refund, special request, low confidence, customer asked for a human); team notified; optional timer after which the conversation returns to AI.
- AI on/off per channel and per conversation; business-hours mode (AI only outside hours, or always).
- AI disclosure message (EU AI Act), editable text.
- **Test chat** inside the dashboard before going live.
- Understands images (match to catalog when possible) and voice notes (transcribe).

### 4.9 Order requests (AI takes the order, a human approves it)
**Rule: the AI never creates a confirmed order.** It collects the order and creates an *order request*; an order exists only after a human approves it.

Flow:
1. Customer shows interest → AI shows matching products (cards with image, price, code) from the catalog.
2. AI offers the **available options as buttons** (e.g. sizes/colours in stock) via `get_product_options`. Only in-stock variants are offered.
3. The remaining details are collected according to the shop's **order template** (see below) — **either in chat or through a checkout form**, whichever the customer prefers.
4. AI shows an **order summary** (items, variant, unit prices, shipping cost from shop policy, total) and asks the customer to confirm.
5. On customer confirmation → `create_order_request` → status **Pending approval**. Customer is told the shop will confirm shortly.
6. Team is notified. In the dashboard a team member can **Approve**, **Edit then approve**, or **Reject** (with reason).
7. On approval: stock is re-checked, an **Order** is created (status Confirmed), stock is decremented, and the customer receives a confirmation message on the same channel. On rejection: customer receives a polite message; conversation goes to the team if needed.

**Order template**
- Each workspace has an order template: the list of fields an order needs, each with type, required/optional and validation. Default for e-commerce: name, phone, delivery address (street, CAP, city, province, country), delivery zone, quantity, notes. Payment method is fixed to **cash on delivery** in Phase 1 (shown in the summary, not chosen). The owner can add fields (e.g. preferred delivery date, gift message) or make fields optional.
- The template drives both input paths, so chat and form always produce the same structured order request.

**Two input paths, one result**
- **Chat:** the customer can write the details in any order and any format, even in one messy message (e.g. "Marco Rossi via Roma 12 Sassari 07100 tel 333 1234567 consegna venerdì"). The AI **extracts** the values into the template fields, asks **only for missing or invalid fields**, and never asks again for something already given (also reuses known contact data, asking the customer to confirm it).
- **Checkout form:** a short hosted page (shop logo and colours, customer's language, mobile-first) opened from a button/link in the chat. Same fields, prefilled with anything already known from the conversation. Works on Messenger, Instagram and WhatsApp.
- Extraction is done by the model, but **validation is done by code**: phone format, postal code, required fields, allowed delivery zones, dates in the future. Invalid values go back to the AI to ask the customer again.

Rules:
- All prices, variants, stock and shipping costs come from the catalog/policies — never from the model.
- `TenantId` and contact come from the conversation context, not from model output.
- Buttons follow channel capabilities (Messenger/Instagram quick replies; WhatsApp reply buttons for ≤3 options, list message for more). If buttons aren't possible, use a numbered text list.
- If the approval happens after the 24-hour window, WhatsApp confirmation uses an approved utility template.
- Pending requests older than a configurable time are highlighted; optional reminder to the team.

**Payment (Phase 1): cash on delivery only.** The summary and confirmation state the total to pay on delivery. Online payment comes later via Stripe Connect (ADR 0004); the payment-method model must support adding methods without changing the order flow.

Dashboard: **Orders** page with tabs Pending approval / Confirmed / Rejected; order detail with items, customer, address, conversation link. Simple statuses only in Phase 1 (no fulfilment, payment or shipping tracking).

### 4.10 Notifications
- In-app notifications and email for handoffs, new order requests and new hot leads.
- Daily summary email: conversations handled by AI, handoffs, hot leads.
- Web push (browser/PWA) for urgent events: handoffs, new order requests, new hot leads. Each member can turn push on/off per event type. No WhatsApp alert to the owner in Phase 1.

### 4.11 Usage & limits
- Per-workspace metering of AI conversations and tokens.
- Plan limits enforced (AI stops replying when limit is reached; messages still arrive; owner notified at 80% and 100%).
- Usage page for the owner.

### 4.12 Compliance & security
- Tenant isolation (rules in CLAUDE.md), EU hosting, data export and deletion per workspace, audit log of key actions.

### 4.13 Dashboard languages
- Dashboard UI in **Italian and English** at launch; each user picks their language (default from browser). All UI text goes through the i18n package so more languages can be added later.
- Customer-facing texts (AI disclosure, checkout form, order confirmations) follow the customer's language, independent of the dashboard language.

## 5. Out of scope (Phase 1)
- Shopify / WooCommerce integration (Phase 2)
- Website chat widget (Phase 2)
- Online payments (Stripe or others), payment links, order fulfilment/tracking, shipping integrations (order *requests* + approval ARE in scope)
- Broadcasts, sequences, comment automation
- Bookings, other verticals, agency multi-workspace UI
- Email and other channels
- Mobile app (web app must be usable on mobile browsers)
- Flow builder, public API, Zapier

## 6. User stories (selection)
1. As an owner, I connect my Instagram and WhatsApp in under 5 minutes, so messages appear in one inbox.
2. As an owner, I upload my products via CSV, so the AI knows my prices and sizes.
3. As a customer, I ask "avete questa borsa in nero?" with a photo at 23:00 and get a correct answer within a minute.
4. As an owner, I'm notified when a customer complains, and I can take over the chat.
5. As a team member, I see which conversations are hot leads and who to follow up today.
6. As an owner, I see how many conversations the AI handled this month and how much of my limit is left.
7. As a customer, I pick my size from buttons, send my name, phone and address in one messy message (or fill a short form), confirm the summary, and get a confirmation once the shop approves.
8. As a team member, I review a pending order request, fix the address, and approve it in one click.

## 7. Acceptance criteria (Phase 1 done when)
- A new shop can sign up, finish onboarding and go live without help.
- Messages from all three channels arrive in the inbox in < 5 seconds; replies are delivered on the right channel.
- AI answers product questions only from the catalog; in the eval set, 0 invented prices and ≥ 90% correct/acceptable answers (IT + EN).
- Handoff works end to end with notification and return-to-AI timer.
- An order flows end to end via chat and via form: product → option buttons → details (messy chat input correctly extracted, only missing fields asked) → summary → customer confirm → pending request → approve → order created + customer notified. The AI can never create a confirmed order (critical test).
- No cross-workspace data access possible (critical tests).
- Usage limits enforced and visible.
- Internal testing passes on our own test shops, then 5 real Italian shops use it in a beta for 2 weeks.

## 8. Rollout & billing
1. **Internal testing (alpha):** the Nihasdesk team uses it on its own test shops and pages with real Messenger/Instagram/WhatsApp traffic until the acceptance criteria pass. No billing.
2. **Beta:** 5–10 Italian shops. Billing by **manual invoice** (bank transfer), issued by the owner's company with Italian e-invoicing (fattura elettronica). Price and any beta discount decided before beta starts.
3. **Phase 1 product support for billing:** usage metering, plan limits and a plan field per workspace that a Nihasdesk admin sets by hand. No self-serve checkout, no Stripe subscriptions.
4. Stripe Billing (self-serve subscriptions, upgrades, card payments) comes before public launch.

## 9. Success metrics (beta)
- % of conversations resolved without human (target ≥ 60%)
- Median first response time (target < 1 min)
- Weekly active owners; beta → paid conversion

## 10. Open questions
1. ~~Order capture in Phase 1?~~ **Decided 2026-10-23:** AI collects the order and creates an order request; a human approves before an order exists (section 4.9).
6. ~~Payment methods at launch?~~ **Decided 2026-10-23:** cash on delivery only in Phase 1; Stripe Connect is the first online method later (ADR 0004).
2. ~~Billing in beta?~~ **Decided 2026-10-23:** see section 8 — internal testing first, then beta with manual invoices; no Stripe subscriptions in Phase 1.
3. ~~Which AI provider(s) and EU hosting option?~~ **Decided 2026-10-26:** OpenAI API with European data residency (ADR 0006).
4. ~~Languages of the dashboard at launch?~~ **Decided 2026-10-26:** Italian + English (section 4.13).
5. ~~Is the daily summary email enough?~~ **Decided 2026-10-26:** daily summary email + web push for urgent events; no WhatsApp alert to the owner in Phase 1 (section 4.10).
