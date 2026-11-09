# ADR 0032: Rich messages — product carousels, buttons, quick replies and taps

Date: 2026-11-09 · Status: Proposed

## Context
PRD v0.10 (4.3, 4.4, 4.5, 4.8, 4.9) asks for product carousels with **Lo voglio / Taglie e colori / Simili** buttons, quick replies for options, **Conferma / Modifica** under the order summary, and for photos and voice notes to continue the conversation with a carousel instead of a handoff.

Today:
- `OutboundMessage` (Inbox contract) carries only `Text`, and `MessengerSender` calls `SendTextAsync`. "Send products" (ADR 0024) sends one text message per product; option "buttons" in the order flow are text.
- The Messenger webhook processor handles customer text messages and attachments; postbacks and quick-reply payloads are ignored.

## Decision

### 1. One channel-neutral message model
A message gets an optional **rich content** part next to its text, stored as JSON on `inbox.messages` (`rich_kind`, `rich_content`), described by records in `Inbox/Contracts`:
- `ProductCarousel { intro?, cards[1..10] }`, card = `{ productId, variantId?, matchLabel? (likely | similar), buttons[] }`. **Cards hold ids, not texts or prices.**
- `QuickReplies { text, options[1..13] }`, option = `{ label (≤ 20 chars), action }`.
- `ButtonMessage { text (≤ 640 chars), buttons[1..3] }`.
- Actions are a closed set: `buy`, `options`, `similar`, `pick_variant`, `confirm_summary`, `edit_summary`, `more_products`, `talk_to_human`, `open_form` (URL).
- The **Channels** sender renders at send time: it resolves product ids through `ICatalog` (current name, price, availability, image), so a carousel can never show a price the catalog doesn't have (rule 5, SEC-41). Inactive products are dropped; an empty carousel falls back to text.
- The rendered result is stored with the message, so the team sees exactly what was sent (PRD 4.4).

### 2. Rendering per channel
| Content | Messenger / Instagram | WhatsApp (later) | Fallback |
|---|---|---|---|
| Carousel | generic template, ≤ 10 elements, image, title ≤ 80, subtitle ≤ 80, ≤ 3 postback buttons | interactive list (≤ 10 rows) or product messages if a Meta catalog is connected | numbered text list |
| Quick replies | `quick_replies` (≤ 13) | reply buttons (≤ 3) or list | numbered text list |
| Buttons | button template (≤ 3) | reply buttons (≤ 3) | numbered text list |
- Images use the signed product image URLs of ADR 0031 §7 (Meta fetches them). Card image: the variant image if the card has a variant, else the main image.
- Labels come from the i18n files in the conversation's language (Italian and English first).
- If Meta rejects a rich message (e.g. image not reachable), the sender retries once without the image, then sends the numbered text fallback and marks the message `fallback_text` for the team.

### 3. Payloads and taps
- A button or quick reply carries only an opaque token: `rm1:<id>` where `<id>` is a random 128-bit id of a row in `inbox.message_actions` (`TenantId`, conversation id, message id, action, product/variant id, created, expires (7 days), used at).
- On a postback or quick-reply webhook, Channels passes the token with the customer's message; Inbox looks it up **inside the tenant of the Page that received it** and checks: same conversation as the sender, not expired, product still active. Anything else is treated as plain text ("Lo voglio") and answered by the AI with the current state. Nothing from the payload is trusted (SEC-36).
- A tap is stored as a customer message with `kind = tap`, the label as text and a link to the action; it updates `LastInboundAt` (24-hour window) and wakes the reply pipeline like any message.

### 4. Who answers a tap: code first
A `TapHandler` in AiAgent runs before the model:
- `buy` / `options`: loads in-stock variants through `ICatalog`; one option dimension at a time as quick replies (colour, then size); a variant already chosen from a photo match is preselected and only confirmed; when the variant is complete it adds the item to the conversation's draft order request and asks for missing details (the AI takes over for free text, PRD 4.9 step 3).
- `pick_variant`: records the choice, asks the next dimension or continues.
- `similar`: `search_products` with the card product's category and attributes, excluding it; sends a new carousel with `similar` labels.
- `confirm_summary`: checks the summary hash (items, prices, shipping, address) still matches, then calls the same code as `create_order_request`; otherwise sends a fresh summary. `edit_summary`: asks what to change (AI).
- `more_products`: next page of the last search. `talk_to_human`: handoff reason `customer_asked`.
- No model call for these taps, so they are instant and cost nothing; every tap still writes a run trace (`source = tap`).

### 5. How the AI sends rich content
- New tool **`show_products(product_ids[1..10], intro, match_labels?)`**: the ids must come from tool results of this turn (`search_products`, `match_photo_to_products`, `get_customer_profile`), checked by code like prices (SEC-38). It attaches a carousel to the reply instead of listing products in text.
- New tool **`show_choices(question, options[])`** for free choices that are not product variants (e.g. delivery zone), limited to values returned by tools.
- The order summary step always sends `ButtonMessage` with **Conferma / Modifica** (code, not the model, builds it from `quote_order`).
- Guardrails reject a reply that lists more than 2 products in text when `show_products` was available.

### 6. Leads and interest
- Taps publish `ProductInterestRecorded` (contact, product, action); Contacts adds the product to products of interest; `buy` sets the lead class to `hot_lead` unless the contact is already a customer, with the reason shown to the team (PRD 4.5).

### 7. Team "Send products"
- The + menu builds a `ProductCarousel` (up to 10 products, optional variant) and sends it like a team reply (takes over, 24-hour window). Taps on team-sent carousels follow the same TapHandler; if the conversation is with the team, the tap is shown and the team is notified instead of the AI answering, except `options` and `pick_variant`, which code still answers.

## Alternatives considered
- **Letting the model write button payloads:** rejected; payloads would carry ids or prices the model chose. Opaque server-side tokens keep tenant, conversation and catalog checks in code.
- **Encoding data in signed payloads (no table):** smaller, but can't be revoked, marked used, or shown to the team with context.
- **Sending each product as its own image + text:** works everywhere but is long, noisy, and gives no buy button.
- **Checkout form only:** kept as an option in 4.9, but taps in the chat convert better on mobile and need no page load.

## Consequences
- `OutboundMessage` gains rich content; `MessengerSender` gains template and quick-reply sending; the webhook processor handles `postback` and `quick_reply`.
- New tables `inbox.message_actions` (and columns on `inbox.messages`), RLS; Critical tests: a token from workspace A can't act in workspace B, a token from conversation X can't act in Y.
- The inbox renders cards, chips and tap bubbles.
- Instagram and WhatsApp reuse the model; only rendering differs.
- Issue #87 (photos) now continues with a carousel through `show_products` instead of handing off.

## Update 2026-11-09 (Issue #91 PR 1, owner decisions)
- **Rendering lives in the Inbox, layout in Channels.** The Inbox already reads `ICatalog` (ADR 0024), so it resolves product ids at send time, drops inactive products, picks the labels and creates the `message_actions` rows; Channels gets a ready `OutboundRich` (titles, subtitles, labels, `rm1:` payloads) and only builds Messenger's JSON. Channels still doesn't reference Commerce. The rendered result is stored in `inbox.messages.rich_rendered` and reused on retries, so a retry sends the same buttons.
- **Columns:** `inbox.messages` gets `kind` (`text` | `tap`), `action_id`, `rich_kind`, `rich_content`, `rich_rendered`, `fallback_text`; new table `inbox.message_actions` with a unique `(tenant_id, token)`; additive migration `AddRichMessages`, RLS on.
- **Fallback only for refused content.** Meta's code 100 (invalid parameter) means the message itself was refused: retry without images (when a card has one), then the numbered text list. Other errors (blocked, outside the window, token) stay permanent or retryable as before.
- **Postbacks need a subscription.** Pages now subscribe to `messages,messaging_postbacks`. A Page connected before this change must be connected again before button taps arrive; quick replies arrive with `messages` already.
- **Labels** fit Messenger's 20 characters: "Talk to a person" is **Parla con noi** in Italian ("Parla con una persona" is 21).
- **Not yet:** `open_form` is refused until the checkout form exists (and its URL allow-list); a written "1" or "2" after the text fallback is ordinary text for the AI.
- **Interest (owner decision A):** there is no event bus yet, so taps record interest through a Contacts contract `IContactInterest` (in process) instead of a `ProductInterestRecorded` event (PR 3).
- **Choices so far (owner decision B):** the chosen product, option values and the summary hash live in `ai.order_drafts`, one row per conversation (PR 2).
- **Team "Send products"** (owner decision C): up to 10 products, variant optional, as one carousel with Lo voglio / Taglie e colori / Simili; earlier text cards stay as they were.
