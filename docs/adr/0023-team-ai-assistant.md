# ADR 0023: The team's AI assistant (summary and suggested reply)

Date: 2026-11-01 · Status: Accepted

## Context
PRD 4.4 (v0.7), Issue #34 part 3: the ✨ button switches the side panel to an AI assistant with a short summary and a suggested next reply with its sources. Actions are Insert into reply, Shorter, Friendlier and Regenerate; there is no send button. Suggestions follow the agent's rules (prices and stock only from tools, CLAUDE.md rule 5), notes never reach the AI (rule 7, SEC-37), and runs are metered.

## Decision
- **Same brain, read-only:** `SuggestionAgent` uses `AgentBrain` with `readOnly: true`. The model is offered only `search_products`, `get_product_options`, `get_shipping_options` and `search_knowledge`. A `handoff_to_human` call is refused as an unknown tool, so a suggestion never hands over, creates anything or changes the conversation.
- **Same reply check:** the brain runs `Guardrails.Check` on the whole output (summary and reply). A price no tool returned, a link or a too-long text is held back: the API returns `reply: null` with `blocked`.
- **Same history, without notes:** a new Inbox contract `IAiConversations.HistoryAsync` returns the context the agent would get (no internal messages, no team names) without claiming the conversation. The model gets the customer's first name and language only.
- **Prompt:** `Prompt.Assistant` asks for `SUMMARY:` (2–3 lines in the dashboard language, en/it) and `REPLY:` (the next message, in the customer's language). Without markers the whole text is the reply. Shorter and Friendlier add one instruction and run again; the client never sends a previous draft to the model, so every result is checked again.
- **API:** `POST /api/v1/conversations/{id}/assist` with `{ style: default|shorter|friendlier, language: en|it }` for every team member. It answers `{ summary, reply, sources, blocked, failed }`; sources are `catalog`, `shipping`, `policy` and `answer` (as in the test chat). Errors are `ai.off` (workspace AI off), `ai.suggest_limit`, `ai.not_configured` and `ai.no_messages`; other workspaces get 404.
- **Metering:** each request is an `ai.runs` row with the outcome `suggested` (a new text value, no migration). The limit is `Ai:MaxSuggestionsPerWorkspacePerDay` (500), plus the per-user `messages` rate limit.
- **Dashboard:** the side panel has two tabs, Customer and ✨ AI assistant. The assistant runs on a click (not on every open). Insert adds the reply to the composer draft; sending stays the normal Send.
  - When an inserted suggestion was edited before sending, Owner/Admin get "Save as knowledge". It is prefilled with the last customer message and the sent text and goes to the FAQ (`POST /api/v1/ai/answers`, `ai.teach`).

## Consequences
- The side panel, and so the assistant, is shown on wide screens only; a narrow-screen panel is later work.
- Suggestions count toward AI cost but not toward the agent's reply limits.
- The summary is part of the checked output, so a made-up price in the summary also holds the suggestion back.
