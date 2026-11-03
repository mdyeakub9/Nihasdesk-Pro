# ADR 0025: Orders and order requests

Date: 2026-11-01 · Status: Accepted

## Context
PRD 4.9: the AI collects an order and creates an **order request**; an order exists only after a person approves it (CLAUDE.md rule 5). A team member can also create an order from the inbox, and that order is confirmed (PRD 4.4). Totals must be computed by code from catalog prices and the shipping zone, and stock re-checked and taken atomically at approval (SEC-41). Payment is cash on delivery in Phase 1 (ADR 0004). This is part 1 of 3: the backend core. The Orders page with the real Create order modal, and the AI tool with customer messages, follow.

## Decision
- **Orders live in the Commerce module**, next to the stock they take: tables `commerce.orders` and `commerce.order_items`, tenant-owned with RLS (additive migration `AddOrders`). Commerce now references the Shipping module, using only its Contracts (`IShippingRates`).
- **Statuses:** `pending` (AI request) → `confirmed` or `rejected` (with a reason). Team orders are `confirmed` at creation. The source is stored as `team` or `ai`. Numbers run per workspace from **#1001** (unique index; retried when two orders take the same number at once).
- **Default order template:** customer name, phone, street, postal code, city, province, country (ISO-2), delivery zone and notes, plus 1–20 lines with a quantity of 1–99. FluentValidation checks them in code (phone format; Italian CAP is 5 digits; country code). Owner-defined fields and the hosted checkout form come later.
- **Pricing by code:** `OrderPricer` reads active products and their variants from the catalog and the zone from `IShippingRates`, and checks there is enough stock for the quantity and one currency. Shipping is free from the zone's threshold. Lines keep the title and unit price at that time. Clients send only IDs and quantities; prices, totals and statuses in a request are ignored (SEC-22). Check constraints make sure `total = subtotal + shipping_cost` and `line_total = unit_price × quantity`.
- **Stock at confirmation:** confirming (a team order, or approving a request) runs one conditional `UPDATE … WHERE stock >= quantity` per line inside the order's transaction. If any line fails, nothing changes and the API answers `409 product.not_available`. Approving locks the order row (`FOR UPDATE`), so a request is never approved twice (`409 order.not_pending`). Requests don't take stock while pending.
- **Edit then approve:** `PUT /api/v1/orders/{id}` replaces a pending request's details and lines and prices them again.
- **API** (every team member: order work is inbox work, docs/standards/permissions.md):
  - `GET /api/v1/orders?status=`, `GET /{id}`
  - `POST /api/v1/orders` (team order)
  - `PUT /{id}`, `POST /{id}/approve`, `POST /{id}/reject`
  - Approvals, rejections, edits and team orders are written to the audit log (SEC-47).
- **The AI's entry point:** `Commerce.Contracts.IOrderRequests.CreateAsync` uses the same validation and pricing and creates a pending request only. When validation fails, the error lists field names only (never the customer's values), so the AI knows what to ask again.
- **No module circle:** Commerce defines `IOrderConversations` (which contact a conversation belongs to, in this workspace) and the Inbox implements it. Other workspaces' conversation IDs give `404 conversation.not_found`.

## Update 2026-11-01 (part 2: Orders page and Create order)
- **Orders page** `/orders`: tabs Pending approval / Confirmed / Rejected with counts (`GET /api/v1/orders/counts`). The detail page `/orders/{id}` shows lines, totals, customer, address and a link to the chat.
  - A pending request gets **Approve**, **Edit** (the shared order form; priced again on the server) and **Reject** (a reason is required).
- **Create order** in the inbox + menu is the real form now: name and phone from the contact, products with in-stock variants only, quantities 1–99, address, zone, notes. Saving creates a confirmed team order and takes the stock.
- **Prices in the form come from the server:** `POST /api/v1/orders/quote` runs the same pricing as a saved order, without saving or reserving anything. The dashboard never computes a total.
- The inbox **customer panel** lists the chat's orders (`GET /api/v1/orders?conversationId=`), replacing the placeholder count.

## Update 2026-11-01 (part 3: the AI takes order requests)
- **Two new AI tools.** `quote_order` (items + zone) returns the summary computed by code: unit prices, line totals, shipping, subtotal and total. These amounts become the only totals the reply may state (SEC-38). `create_order_request` (items, zone, name, phone, address) creates a **pending request** through `IOrderRequests`.
  - The conversation comes from the claimed turn (`AgentTools.ConversationId`), never from the model (SEC-36). The dashboard's test chat can't create requests.
  - Neither tool can confirm an order.
- **Prompt:** offer only available variants; collect the template fields and ask only for what is missing or wrong; show the quote; create the request only after the customer confirms; then say the shop will confirm shortly, never that the order is confirmed. Complaints, refunds, a request for a person, or things outside an order request still hand off.
- **Errors the AI can act on:**
  - Invalid fields come back as field names only (no customer values), so the AI asks again.
  - Not enough stock → the AI offers what is available.
  - A second request while one is pending → `order.pending_exists` (one open request per chat, SEC-42).
- `get_shipping_options` now also returns each zone's `id`. The team's assistant (ADR 0023) doesn't get `create_order_request`.
- **Customer and team, through `IOrderNotifications`** (defined in Commerce, implemented by the Inbox):
  - A new request sends `orderRequested` `{ conversationId, orderId }` to the workspace's dashboards, which show an alert linking to the order.
  - On approval the customer gets, in their language and from the person who decided: "Ordine #1001 confermato! ✅ Totale da pagare alla consegna: €185.00." On rejection they get a polite message; the reason stays with the team.
  - These messages don't change who answers the chat.
  - Outside Meta's 24-hour window nothing is sent (SEC-43); the team gets a note in the chat to contact the customer another way. WhatsApp utility templates come later.


## Update 2026-11-01 (owner feedback: summary to the customer, New order)
- **The customer gets the order summary in the chat** whenever an order of that chat is confirmed: a team order created from the chat, or an approved AI request. It lists the lines (quantity, variant, line total), shipping with the zone, the total to pay on delivery and the delivery address, in the customer's language, from the person who confirmed it. All amounts come from the saved order (rule 5). Outside the 24-hour window the team gets a note instead (SEC-43). `IOrderNotifications.ApprovedAsync` became `ConfirmedAsync(conversationId, ConfirmedOrderSummary)`.
- **Orders page → New order:** the same create dialog as the inbox, without a chat (e.g. an order taken by phone). Such an order sends nobody a message.

## Consequences
- Prices are fixed when the request is created or edited. A catalog price change between request and approval isn't applied automatically: edit the request to price it again.
- Customer messages on approval and rejection and team alerts came with part 3. Outside Meta's 24-hour window the team only gets a note; the WhatsApp utility template comes with the WhatsApp channel.
- Orders hold delivery addresses (personal data): they are covered by the workspace export and deletion work (SEC-45), and retention rules come with it.

## Still open from PRD 4.9
Not built yet; each gets its own Issue and PR:
- **Option buttons:** variants as Messenger/Instagram quick replies (WhatsApp reply buttons or a list later), with a numbered text list as fallback. Today the AI lists them as text.
- **Hosted checkout form:** the same template fields on a short page opened from the chat, with signed, expiring, single-purpose links (SEC-29), and rate limits plus CAPTCHA when abused (SEC-42).
- **Owner-defined order template:** the owner adds fields (e.g. delivery date, gift message) or makes fields optional; chat and form use the same template.
- **Old pending requests:** highlighted after a configurable time, with an optional reminder to the team.
- **WhatsApp utility template** for confirmations after the 24-hour window (with the WhatsApp channel).
