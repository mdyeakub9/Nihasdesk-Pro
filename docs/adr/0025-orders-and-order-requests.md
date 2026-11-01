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

## Consequences
- Prices are fixed when the request is created or edited. A catalog price change between request and approval isn't applied automatically: edit the request to price it again.
- Customer messages on approval and rejection, team alerts and the WhatsApp template outside the 24-hour window come with part 3.
- Orders hold delivery addresses (personal data): they are covered by the workspace export and deletion work (SEC-45), and retention rules come with it.
