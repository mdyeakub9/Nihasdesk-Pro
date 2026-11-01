# AI Agent

_Status: draft._

## Principles
- The LLM is stateless. "Memory" = our database. Each call gets a small, selected context.
- The model **asks**, our code **acts**: all facts and actions come through tools.
- Provider-agnostic `ILlmProvider`; can switch or fall back between providers. Phase 1 provider: OpenAI with European data residency (ADR 0006).

## Per-message pipeline
1. Message arrives via queue.
2. Build context: instructions, last ~10–20 messages, summary of older history, contact profile, workspace settings.
3. Cheap model: language, intent, needs-human?, lead classification.
4. Retrieve 2–3 relevant knowledge snippets (hybrid search, tenant-scoped).
5. Main model with tools → tool calls executed by backend → results back → final reply.
6. Guardrails check → send → store → meter tokens per workspace.

## Tool registry
Each tool implements `IAgentTool { Name, Description, Parameters, ExecuteAsync(ctx, input) }`. Tools are enabled per workspace by connected integrations/features.
- Phase 1: `search_products`, `get_product_options`, `get_shipping_options` (zones and costs, ADR 0020), `search_knowledge` (policies by topic + FAQ), `create_lead`, `create_order_request`, `handoff_to_human`
- Later: `get_order_status`, `check_availability`, `book_appointment`, custom API tool.
- There is deliberately **no** `create_order` tool: orders are created only by a human approving an order request (PRD Phase 1, section 4.9).

Tool code validates all inputs and takes `TenantId` from `ToolContext`, never from model output.

## Safety
- Never invent prices, stock, times, order status.
- Low confidence → ask the customer or hand off.
- Explicit customer confirmation before orders/bookings.
- AI disclosure to customers (EU AI Act).
- Prompt-injection resistant by design: tools enforce limits regardless of prompt.

## Cost control
Prompt caching for stable instructions; summaries instead of full history; per-workspace credit limits; batch processing for non-urgent jobs (summaries).

## Quality
Versioned prompts; eval set of 50–100 questions per vertical, run before prompt/model changes; agents' 👍/👎 feedback; "why did AI say this" trace for owners.

## Knowledge layers
The model is never fine-tuned. Per message it receives a small context assembled from three layers (PRD 4.7):

| Layer | What | Sent to the model | Who uses the values |
|---|---|---|---|
| 1 Business facts | typed fields: hours, address, shipping zones/costs/times, payment methods, return window | only the values a tool returns for this question | **code** computes totals, shipping and delivery dates |
| 2 Instructions | short owner blocks (about, tone, rules, offers), on/off per block, size budget | always, every message (cached prompt prefix) | model |
| 3 Knowledge | documents, FAQ, PDF/DOCX, one web page, taught answers | only the top 2–4 passages found by `search_knowledge` | model, with source references |

Rules:
- Put a fact in Layer 1 if code must calculate or validate with it; Layer 2 if it must shape every answer and is short; Layer 3 otherwise.
- Layer 3 content is **untrusted input** (SEC-35): imported pages and documents can contain instructions; they are quoted as data, never followed.
- Chunking: split by headings and paragraphs, keep the source title on each chunk, store source id + version for citations.
- Retrieval: hybrid (pgvector embeddings + full-text), tenant-scoped (rule 1), multilingual; return passages with source ids; the reply records which passages were used.
- `search_knowledge` is offered to the model only when the workspace has at least one Layer 3 source.

## Draft, publish and versions
- Layers 1–3 have a draft and a published state. The test chat runs on the draft; customers get the published version.
- Publishing creates an immutable version (author, time, diff summary). Rollback = publish an earlier version again.
- Every AI run stores which instruction version and which knowledge passages it used, so "why did the AI say this" can be answered.
- Kill switches (AI off, block off) apply immediately without publishing.

## Learning loop
- Unanswered questions → "Teach the AI" → taught answer becomes a Layer 3 source.
- Team edits of AI drafts and human answers can be saved as knowledge with one click.
- Agents' 👍/👎 on AI replies are stored for review and the eval set.
- Later: plain-language change requests ("Tell the AI what to change") that edit the draft after confirmation.

## Team AI assistant
In the inbox side panel the team can ask for a summary and a suggested reply (ADR 0023). It is the same brain and reply check as the agent, with read-only tools (no handoff, orders or leads) and the same history without notes. The text is only inserted into the composer and sent by a person; an edited suggestion can be saved as knowledge (learning loop).

## Order requests
The agent takes orders as requests a person approves (PRD 4.9, ADR 0025): `quote_order` gives the summary computed by code, `create_order_request` stores a pending request only after the customer confirms it, and the conversation comes from the turn, never from the model. The team approves or rejects in the Orders page, and the customer is told in the same chat.
