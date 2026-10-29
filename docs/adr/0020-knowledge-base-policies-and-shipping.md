# ADR 0020: Knowledge base — shop policies, shipping zones and FAQ

Date: 2026-10-29 · Status: Accepted

## Context
PRD 4.7: the AI must answer shop questions (shipping, returns, payment, contact) without making anything up, and the order summary (PRD 4.9) needs shipping costs from code, not from the model (CLAUDE.md rule 5, SEC-41). ADR 0019 left `search_knowledge` with only the answers the team taught.

## Decision
- **Shipping zones live in the Shipping module** (schema `shipping`, table `zones`, RLS). A zone has a name, the places it covers in the owner's words, a cost and a free-from order value in cents, a currency and delivery days (min–max).
  - Other modules read them only through `Contracts/IShippingRates`. The AI uses it now; order requests will use it for the summary.
  - Zones are saved as one ordered list (`PUT /api/v1/shipping/zones`). A zone sent with its id keeps it; unknown ids (also other workspaces') become new zones; zones left out are removed.
- **Shop policies live in the AiAgent module** (`ai.shop_policies`, one row per workspace): shipping notes, return days and rules, payment notes, contact phone/email/address and opening hours.
  - Payment is fixed to cash on delivery in Phase 1 (ADR 0004). It is shown, not chosen.
- **FAQ = the answers list.** Answers the team taught from unanswered questions and answers written by hand are one list. The API says which is which (`source`: `taught` / `added`) by looking at the questions, so no new column is needed.
- **Dashboard:** Settings → Shop knowledge (zones, policies, FAQ). Everyone sees it; Owner and Admin change it. Settings → AI keeps the AI settings, unanswered questions and the test chat.
- **AI tools:**
  - `get_shipping_options` (new) returns the zones. Their costs and free-from values become allowed prices for the reply check (SEC-38). No zones means: hand off with `unknown_answer`.
  - `search_knowledge` now takes a `topic` (`shipping`, `returns`, `payment`, `contact`, `other`) and returns the written policy for that topic plus matching FAQ answers.
  - Amounts written in the owner's own policy or answer text (e.g. "return shipping €5.00") may be quoted, because the owner wrote them, not the model.
  - The prompt says: shipping costs only from `get_shipping_options`, no adding up of totals, contact details only exactly as `search_knowledge` returns them, never links.
- **Sources (PRD 4.7):** every run stores what the answer was based on in `ai.runs.sources` as codes (`shipping`, `policy:returns`, `answer:{id}`), never customer text. The test chat shows them ("Based on: return policy, “Posso cambiare taglia?”").
- **Search stays keyword based, in memory**, with word starts ("spedizione" and "spedite" both match "spedi"). A shop has at most a few hundred answers. PostgreSQL full-text search was considered: with the `simple` dictionary it adds no stemming over this, and a language dictionary would need the language of each answer. Embeddings (pgvector) come with document and website import, where texts are long.

## Consequences
- Shipping costs have one source of truth for the AI and, next, for order summaries.
- The AI can't quote a total (product + shipping) yet; that reply is held back and goes to the team. The order summary will compute totals in code.
- PDF/DOCX upload and website import (SEC-04, SEC-05) are a separate PR.
- Removing a zone deletes it. Order requests will copy the cost they used, so later changes don't alter past requests.
