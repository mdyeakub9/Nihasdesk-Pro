# PRD — Phase 1 MVP (E-commerce)

_Status: draft v0.8 · 2026-11-03 · Owner: Yeakub_
_Related: `docs/project-brief.md`, ADR 0003, ADR 0026 (AI knowledge and memory)._

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

**Conversation header**
- Customer name, **channel icon and name** (moved here from the composer), status, assignee.
- **Responder chip**: "AI" or the team member's name; click to take over or return to AI. Replaces the "AI is answering this customer" banner.

**Team notes**
- Written from the side panel ("Team note → add"); the composer has no note mode.
- Each note also appears **in the conversation timeline** as a distinct internal bubble (different colour, lock icon, "Only your team sees this", author and time).
- A note can be **pinned** to the top of the side panel (e.g. "VIP, call after 6 pm").
- `@name` mentions notify that team member.
- Notes are **never** sent to the customer's channel and **never** sent to the AI provider (rule 7).

**Composer**
- One text box and **one Send button** for everything the team sends. While the AI is the responder the button reads **"Send & take over"**.
- When the 24-hour window is closed the composer is disabled with the reason (existing behaviour).
- Draft text is kept per conversation when switching chats. "X is typing…" shows when another team member writes in the same chat.
- Shortcuts: `Ctrl+Enter` send, `/` saved replies, `Esc` closes menus.

**Saved replies (`/`)**
- Typing `/` opens the list above the composer; typing filters it (e.g. `/ship`); ↑↓ and Enter insert.
- Variables: `{first_name}`, `{shop_name}`; per-language versions (Italian/English) — the customer's language is chosen automatically.
- Inserting puts the text in the composer for editing; it is never sent directly.
- Owner/Admin create and edit saved replies; Members use them.

**AI assistant (team helper)**
- The ✨ button switches the side panel from **Customer** to **AI assistant** (two tabs, one click back).
- Shows a 2–3 line **summary** of the conversation and a **suggested next reply** written from the whole history, the catalog and the knowledge layers, with the **sources used** (products, knowledge passages).
- Actions: **Insert into reply** (puts it in the composer to edit and send with the normal Send button), **Shorter**, **Friendlier**, **Regenerate**. No separate send button in the panel.
- Suggestions follow the same rules as the AI agent (prices and stock only from tools, rule 5). After sending an edited suggestion the team can **Save as knowledge** (4.7).

**+ Actions menu**
- **Send products**: search the catalog, send a card or carousel with in-stock variants.
- **Create order**: opens an order **modal** with name and phone pre-filled from the chat; the full field set is defined with the Orders module (4.9). An order created by a team member is a confirmed order (a human created it). The AI agent still only creates **order requests** that a human approves (rule 5).
- **Send image or file**.
- **Saved reply** (same list as `/`).
- Later: send checkout link, send payment link.

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

### 4.7 What the AI knows — three layers
The AI is never "trained"; it is given the right information per message. Information lives in three layers, each with a clear job (details: `docs/ai-agent.md` → Knowledge layers).

**Layer 1 — Business facts (structured, computed by code).** Typed fields, not free text:
- Shop name, address, opening hours, contact channels.
- **Shipping zones** (country/region/postal-code ranges) with **cost** and **delivery time** per zone, free-shipping threshold, dispatch cut-off time.
- Payment methods allowed (Phase 1: cash on delivery, with optional COD surcharge).
- Return window (days) and whether returns are free.
These values are used by **code**: order totals, shipping cost and delivery estimates are always computed from them, never written or calculated by the model (rule 5, SEC-41). The AI receives the relevant values through tools.

**Layer 2 — Instructions (short, always sent).** Small blocks the owner writes: about the shop, tone, do's and don'ts, current offer. Each block can be switched on/off. A size budget is shown (characters) because every block is sent with every message. Seeded from templates of the shop's vertical pack (ADR 0003).
- **Temporary notes (snippets with an end date):** a block can have "show until" (date and time), e.g. "Deliveries are 2 days late this week". It switches itself off at that time and moves to "Expired" (can be renewed). The owner sees active temporary notes at the top of the AI page.

**Layer 3 — Knowledge (long, looked up when needed).**
- Sources: text pasted with a title, FAQ entries, PDF/DOCX upload, single website page import (through the SSRF-safe client, SEC-05), and the answers the team teaches via "Teach the AI".
- Each source is split into sections (headings/paragraphs), indexed with **hybrid search** (meaning + keywords, multilingual) per workspace, and searched by the `search_knowledge` tool only when a question needs it.
- A **"test a question"** box shows which passages the AI would find for a question.
- Each answer shows the owner which sources/passages were used.

**Importing a source (ingestion)** — how a file or page becomes knowledge (technical design: ADR 0026)
- Upload or URL → the file is checked (type by content, size, SEC-04) and stored → a background job reads it → the source shows a status: **Reading… → Ready to review → Live**, or **Failed** with a plain reason ("This PDF is a scan; we can't read text from images yet").
- The AI never reads the raw file. Code reads it once, cleans it, splits it into sections and stores them with search indexes; the AI only gets the 3–4 best sections for a question later.
- Readers: PDF with selectable text, DOCX, plain text, one web page (main content only, menus and footers removed), or a whole website from its domain: the system finds the main pages and the owner confirms the list (ADR 0030). Tables are kept whole as tables; headings are kept as the section path ("Shipping › Italy › Islands").
- Limits per workspace (Phase 1 defaults, shown in the UI): file up to **20 MB** and **300 pages**, up to **100 sources**, text only (no images, no scanned PDFs, no password-protected files). Reaching a limit shows a clear message, never a silent failure.
- Same file uploaded again → detected (content hash) and not stored twice. A new version of a file replaces the old one only when published.
- **Web pages are re-read automatically once a week** (and on "Re-read now"). If the page changed, the new content becomes a draft to review; if re-reading fails twice in a row, the owner gets an in-app notice and an email, and the last good version stays live.
- Deleting a source deletes its sections and search data (SEC-45).

**Knowledge review (preview before it goes live)**
- After reading, the owner sees **exactly what the AI will be able to find**: the source split into sections, each with its heading path, text and page number.
- Per section: edit the text, exclude it ("AI should not use this"), or merge it with the next one. Edits are saved in the draft.
- Warnings shown at the top, in plain words:
  - "No text found on pages 4–6" (scanned or image pages).
  - "This looks like a price list — prices for customers always come from Products" (prices in a document are never used for totals).
  - **Conflicts with business facts:** the document says something different from Layer 1 (e.g. document "shipping €5", settings "Italy €4.90"; document "returns 30 days", settings "14 days"). The owner chooses: fix the settings, fix the section, or exclude it. Customers always get the Layer 1 value meanwhile.
- "Test a question" works on the reviewed draft before publishing.

**How the AI finds the right part (retrieval)**
- The customer's question is turned into a clear search question (using the last messages, e.g. "and to Sardinia?" → "shipping cost to Sardinia").
- Search by meaning and by keywords at the same time, in Italian and English, only inside this workspace; the best 3–4 sections are given to the AI with their source.
- If nothing matches well enough, the AI says it will check with the team and hands off (reason "unknown answer"); the question appears in "Teach the AI". It never guesses.
- The reply records which sections were used, shown to the team as sources.

**Draft → test → publish (Layers 1–3)**
- Changes are saved as a **draft**. The test chat uses the draft; customers keep getting the **published** version.
- **Publish** makes the draft live; every publish is a **version** with who/when and a short diff; **one-click rollback** to an earlier version.
- Exception: turning the AI off, or switching off a single instruction block, applies immediately (safety switch). A temporary note reaching its end date also switches off immediately.
- Before publishing, the dashboard shows what changed (added / edited / removed sections and blocks) and unresolved conflict warnings.

**Teach the AI (existing, extended)**
- Questions the AI could not answer are listed for the team; a taught answer becomes a Layer 3 source.
- When a team member edits an AI draft or takes over and answers, the dashboard offers "Save as knowledge" for that answer.

**Later (not Phase 1):** reading scanned PDFs and images (OCR), Google Drive/Notion sync, and "Tell the AI what to change" — the owner writes a change in plain language; an assistant shows the current text and the proposed change, applies it to the draft after confirmation and runs the test chat.

### 4.8 AI agent
- Replies in the customer's language; tone setting (formal/friendly); owner instructions as Layer 2 blocks (4.7).
- Knows the shop only through the three layers in 4.7; shipping cost, totals and delivery times always come from Layer 1 via tools.
- Tools: `search_products`, `get_product_options`, `get_shipping_options`, `search_knowledge`, `get_customer_profile`, `quote_order`, `create_lead`, `create_order_request`, `handoff_to_human`.
- Never invents prices, stock or policies; if unsure → asks a clarifying question or hands off.
- Handoff: reason recorded (complaint, refund, special request, low confidence, customer asked for a human); team notified; optional timer after which the conversation returns to AI.
- AI on/off per channel and per conversation; business-hours mode (AI only outside hours, or always).
- AI disclosure message (EU AI Act), editable text.
- **Test chat** inside the dashboard, using the draft (4.7), optionally continuing the same test conversation.
- Understands images (match to catalog when possible) and voice notes (transcribe).

**Memory (what the AI remembers in a conversation)** — technical design: ADR 0026
- The AI has no memory of its own; each reply gets a small, chosen context with a fixed size budget: instructions → conversation summary → customer profile → recent messages → found knowledge → tool results.
- **Recent messages:** the last messages in full (up to the budget).
- **Conversation summary:** older parts of a long conversation are kept as a short running summary (what the customer wants, chosen product/size, open questions), updated in the background. Team notes are never part of it.
- **Customer profile:** first name, language, earlier orders and order requests (number, status, date), only when the question needs it, through a tool (`get_customer_profile`). No phone, email or address is sent unless the order flow needs it (GDPR minimization, rule 7).
- The team can see the summary in the AI assistant tab; when the team takes over, the summary is what they read first.

**Quality and "why did the AI say this"**
- Every AI reply has a trace for the team: which instruction version, which knowledge sections, which tools with which results, which guardrail checks; no customer data beyond the conversation itself.
- An **eval set** (50–100 real-style questions per vertical, Italian + English, with the expected answer or "should hand off") runs before every prompt, model or search change; results are compared with the last run.
- Team 👍/👎 on AI replies is stored and reviewed; 👎 replies can be added to the eval set.

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

**Keeping AI cost low (no change visible to customers)** — technical design: ADR 0026
- The fixed part of every request (rules, instructions, tool list) is sent first and identically, so the provider's prompt cache makes it cheaper.
- Several quick messages from the same customer ("hi" / "do you have" / "this in black?") are waited for a few seconds and answered once.
- Simple steps (language, intent, needs a human?) use a cheaper model; replies use the main model.
- Tool results are trimmed to what the reply needs (e.g. top 5 products, not the whole catalog).
- Background work (summaries, web page re-reading, eval runs) runs in batches outside the reply path.
- Answers are never reused between customers (each conversation is different and prices must be live).

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
- No cross-workspace data access possible (critical tests) — for every module with tenant data (Identity, Contacts, Inbox, Channels, AiAgent, Commerce, Shipping), including knowledge sections and their search data.
- Knowledge: a PDF, DOCX and web page can be imported, reviewed and published; in the eval set the right section is among the found ones for ≥ 85% of questions, and questions without an answer in the knowledge hand off instead of guessing (0 made-up policies).
- Customers never get unpublished (draft) knowledge or instructions (test).
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
3. ~~Which AI provider(s) and EU hosting option?~~ **Decided 2026-10-26:** OpenAI API with European data residency (ADR 0006); changed on 2026-11-05 to the global endpoint (ADR 0028).
4. ~~Languages of the dashboard at launch?~~ **Decided 2026-10-26:** Italian + English (section 4.13).
5. ~~Is the daily summary email enough?~~ **Decided 2026-10-26:** daily summary email + web push for urgent events; no WhatsApp alert to the owner in Phase 1 (section 4.10).
