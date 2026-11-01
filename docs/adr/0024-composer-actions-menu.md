# ADR 0024: The composer's + menu — send products, create order

Date: 2026-11-01 · Status: Accepted

## Context
PRD 4.4 (v0.7), Issue #34 part 4: a + menu next to the composer with Send products (catalog search, in-stock variants), Create order (a modal with name and phone from the chat; the full fields come with the Orders module, PRD 4.9), Saved reply, and Send image or file. Prices and stock must come from the catalog, never from the client or the model (CLAUDE.md rule 5, SEC-22). The owner postponed images and files.

## Decision
- **Send products:** `POST /api/v1/conversations/{id}/products` with 1–5 `{ productId, variantId }`, for every team member.
  - The Inbox reads the catalog through `Commerce.Contracts.ICatalog` (active products only). The Inbox project now references the Commerce module; only its Contracts are public, and there is no cycle.
  - Every variant must be available now; otherwise nothing is sent (`404 product.not_found` for unknown, inactive or another workspace's products; `400 product.not_available`).
  - One team message per product, sent like a reply: it takes the conversation over, respects the 24-hour window and is queued for the channel.
- **Card format: text, for now.** The channel gets `🛍️ Title — Variant` and `€89.00 · SKU …`. This works on every channel. Messenger's image carousel (generic template) waits until products have images (uploads, SEC-04) and a shop link.
- **Snapshot:** the product as sent (IDs, title, variant, SKU, price in cents, currency) is stored with the message in `inbox.messages.product` (jsonb, additive migration `AddProductMessages`). The timeline shows the card as it was sent, even after the catalog changes.
- **Create order:** a modal with the contact's name and phone, read-only, and a note that orders come with the Orders module. Nothing is saved. A team-created order will be a confirmed order; the AI still only creates order requests.
- **Saved reply** starts a `/` command in the composer (same list as typing `/`). **Send image or file** is shown disabled, "Coming soon".

## Consequences
- Customers see products as text until images exist.
- The product picker uses the catalog API (`catalog.view`, every member) and offers only variants with `available`.
- Orders, checkout links and payment links are later work with the Orders module.
