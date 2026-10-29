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

## Owner instructions
Owner writes rules in plain language → converted to settings → previewed in test mode → published → versioned with one-click rollback.
