# ADR 0019: AI agent core — reply pipeline, tools, guardrails, learning from the team

Date: 2026-10-29 · Status: Accepted

## Context
PRD 4.8 and docs/ai-agent.md describe the agent. This is the first step: the AI answers customers from the catalog, hands off to the team when it can't, and learns from the team. Knowledge-base documents, order requests and the settings page come in later PRs. The provider is OpenAI with EU data residency (ADR 0006) behind `ILlmProvider`.

## Decision
- **Trigger:**
  - A customer message on a conversation where the AI answers (`responder = ai`, `aiEnabled`) sets `ai_reply_due_at` a few seconds ahead. Every further message moves it on, so a burst of short messages gets one answer.
  - A team reply, take-over or AI-off clears it.
  - A background processor in the API (database queue, `FOR UPDATE SKIP LOCKED`, same pattern as ADR 0017) finds due conversations through `inbox.due_ai_replies` (IDs only) and works on each one in its workspace's scope.
- **Context sent to the model (GDPR, CLAUDE.md rule 7):**
  - the last 20 messages without internal notes, and the customer's first name and language
  - the shop name and the rules below
  - never phone numbers, emails, addresses or costs, and nothing from other conversations or workspaces (SEC-37)
- **Tools (v1):** `search_products`, `get_product_options` (only available variants), `search_knowledge` (answers the team taught) and `handoff_to_human` (with a reason).
  - Tool code takes the workspace and conversation from the context, never from the model, and validates every argument (SEC-36).
  - At most 4 tool rounds per reply.
- **How it talks:** natural and warm, like a good shop assistant on chat. It uses short messages in the customer's language, mirrors their tone, and never sounds like a form. It never claims to be human.
- **Disclosure (EU AI Act Art. 50, CLAUDE.md rule 5):** before the first AI reply in a conversation, a short disclosure line from code (not from the model) says the customer is talking to the shop's virtual assistant. If asked, the AI says so and offers a person. Hiding that the customer talks to an AI is not an option.
- **Guardrails (SEC-35, SEC-38):** customer text is data, never instructions. A reply is not sent, and the conversation goes to the team instead, when it:
  - contains a price that no tool returned in this turn;
  - contains any link;
  - is empty or too long.
- **Limits (SEC-39):** a daily reply cap per workspace and an hourly cap per conversation. Over a cap, or on provider errors, the conversation goes to the team. Tokens are metered per workspace (`ai.runs`).
- **Learning from the team:**
  - When the AI can't answer (`handoff_to_human` with reason `unknown_answer`), the question is recorded as an **unanswered question**.
  - Owners and Admins answer it; the answer becomes a **taught answer**, which `search_knowledge` returns from then on.
  - Taught answers can be edited or switched off. They are shop knowledge, not code.
- **Logs (SEC-40):** per run we store the model, tokens, tool names, outcome and duration. Prompts and replies are not copied into logs; the reply itself is the inbox message.

## Consequences
- Without an API key the processor stays off and everything works for humans as before.
- Images and voice notes go to the team until media support exists.
- Search over taught answers is keyword based (ILIKE); pgvector embeddings come with the knowledge-base PR.
