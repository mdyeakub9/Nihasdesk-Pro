# ADR 0026: AI knowledge and memory — ingestion, retrieval, memory and cost

Date: 2026-11-03 · Status: Proposed (accepted part by part as the issues are built)

## Context
PRD 4.7, 4.8 and 4.11 (v0.8) ask for document and web page import with a review screen, hybrid search with sources, conversation memory and lower AI cost. Today (ADR 0019, 0020):
- `search_knowledge` scores policies and FAQ answers by keywords in memory; there are no embeddings.
- The agent gets the last 20 messages (`HistorySize`) and nothing older.
- Draft → publish and versions are written in the PRD but not built.
- `ai.runs` stores the sources of each answer as codes.

A competitor review (Intercom Fin, respond.io, Tidio Lyro, AiPraktor) showed the common baseline: several source types, short temporary notes, scheduled re-sync with failure alerts, testing before going live. None of them shows the owner what the AI actually read or warns about conflicts with the shop's settings; we do both.

## Decision

### 1. Where things live
Everything stays in the **AiAgent** module (schema `ai`, RLS, `TenantId` on every row). Other modules are read only through contracts: `ICatalog`, `IShippingRates`, `IOrderRequests`, `IAiConversations`, and a new narrow `IContactProfile` in Contacts.

Tables (names indicative, final in the migration PR):
- `ai.knowledge_sources`: type (`text`, `faq`, `pdf`, `docx`, `web_page`, `taught`), title, origin (file name or URL), content hash, status (`reading`, `review`, `live`, `failed`, `archived`), failure reason, last read, next re-read.
- `ai.knowledge_sections`: source id, heading path, page, block kind (`text`, `list`, `table`, `faq`), text, token count, excluded flag, embedding `vector(1536)`, full-text `tsvector`, language, embedding model + version, source version.
- `ai.knowledge_versions`: published snapshots of layers 2 and 3 (author, time, diff summary). Section rows carry the version they belong to; customers read only the published version.
- `ai.conversation_memory`: conversation id, running summary, covered-up-to message id, updated at.
- Raw files go to object storage (local: disk folder), never the database.

### 2. Ingestion pipeline (background, Workers)
`upload/URL → check → store → queue → parse → normalize → chunk → embed → index → status "review"`
- **Upload checks (SEC-04):** type by content (magic bytes), size and page limits from PRD 4.7, no encrypted files, file names never used as paths.
- **Fetching web pages (SEC-05):** only through `SafeHttpClient` (no private IPs, redirect and size limits, timeouts).
- **Parsers behind `IDocumentParser`**, one per type, all returning the same intermediate model: `Document → Sections (heading path, page) → Blocks (paragraph, list, table)`.
  - PDF: **PdfPig** (text with positions; headings by font size; pages without text → warning "scan").
  - DOCX: **Open XML SDK** (heading styles, lists, tables).
  - Web page: **AngleSharp** + **SmartReader** (Readability port; menus, footers, cookie banners removed).
  - All are .NET, MIT/Apache licensed, run in-process; no Python service in Phase 1. **Docling** (Python container) and **Tesseract OCR** are the upgrade path for scans and complex layouts.
- **Chunking:** follow headings; target 300–500 tokens, ~50 tokens overlap inside long sections only; never split a table, a list item or a FAQ pair; prefix each chunk with its title and heading path ("Shipping › Italy › Islands") for search, store the clean text separately for display.
- **Embeddings:** OpenAI `text-embedding-3-small` (1536 dims) through `IEmbeddingProvider`, on the same OpenAI project as replies (ADR 0028). Batched, retried, model and version stored per section so a model change can re-embed in the background. Unchanged text (hash) is not embedded again.
- **Untrusted content (SEC-35):** imported text is data. It is quoted to the model inside a clearly marked block and can never change instructions or tools.
- **Conflict check:** after parsing, code (not the model) looks for amounts, day counts and shipping words and compares them with Layer 1 values (shipping zones, return days, payment). Matches that differ become review warnings. Prices in documents are never used for totals (rule 5, SEC-41).

### 3. Retrieval (`search_knowledge`)
- **Query rewrite:** the cheap model turns the latest message plus the last 2–3 messages into one standalone search query (same language). Skipped when the message is already a full question.
- **Hybrid search, one SQL query, tenant- and version-scoped:**
  - meaning: pgvector cosine distance, **HNSW** index;
  - keywords: PostgreSQL full-text with the `italian` or `english` configuration chosen by the section's stored language (`simple` when unknown) plus `unaccent`;
  - fused with **Reciprocal Rank Fusion** (k = 60), top 20 candidates.
- **Rerank:** start without a separate reranker; take the top 3–4 by RRF. Add a cheap-model or cross-encoder rerank only if the eval set shows a gain worth the cost.
- **"Unknown" threshold:** if the best candidate is below a tuned score, the tool returns "no answer" and the agent hands off with `unknown_answer` (the question goes to "Teach the AI").
- **Context budget:** at most ~1,500 tokens of knowledge per reply; each passage keeps its source id, section id and version; the run stores them (`ai.runs.sources`), and the test box and AI assistant show them.
- The existing policy and FAQ answers become sections of the same index, so one search covers all of Layer 3. Layer 1 facts are never answered from Layer 3.

- _Built (Issue #53):_ one SQL query as above (HNSW and GIN indexes, `unaccent`, RRF k = 60), top 4 within about 1,500 tokens, for sources the owner put in use with "Use for AI" until draft and publish exist (#55). A section counts when it is close in meaning (`Ai:KnowledgeMinSimilarity`) or has every word of the question. Not built, to be added only if the eval set asks for it: query rewrite, and moving policies and FAQ answers into sections (they are searched as before, next to documents). Amounts in documents may not be quoted: the reply check holds them back. `Ai:KnowledgeMinSimilarity` is 0.3, measured with `--search-only` on the eval set: the right section scored 0.32–0.73, the closest wrong one for questions the shop can't answer 0.17–0.47. They overlap, so the threshold only drops what is clearly unrelated and the agent decides from what it reads; the 8 unknown cases still hand off. Measured on one short Italian document (10 questions), to check again with real documents.

### 4. Memory and context assembly
Per reply, a **context builder** fills a token budget in a fixed order, so the stable part stays cacheable:
1. system rules + tool definitions (static),
2. published Layer 2 instruction blocks (change only on publish),
3. conversation summary (if any),
4. recent messages, newest kept, until their budget is used (replaces the fixed `HistorySize = 20`),
5. tool results of this turn (knowledge passages, products, profile).
- **Rolling summary:** when a conversation has more messages than fit, a background job (cheap model) summarizes the older part into ≤ 120 words: wishes, chosen product/variant, open questions, promises made. Team notes and internal data are never included. Stored in `ai.conversation_memory`, regenerated on demand.
- **Customer profile:** tool `get_customer_profile` returns first name, language and the last 3 orders/order requests (number, status, date) through `IContactProfile` and `IOrderRequests`. No phone, email or address (SEC-37, rule 7). The contact comes from `ToolContext`, never from the model (SEC-36).
- Long-term facts about a customer are not written by the AI in Phase 1; the profile is computed from data the shop already has.

### 5. Draft, publish and versions
- Layer 2 blocks and Layer 3 sections are edited as a draft; the test chat and "test a question" read the draft; customers read the published version.
- Publish creates an immutable version with a diff summary; rollback publishes an earlier version again. Kill switches (AI off, block off, temporary note end date) act at once.
- Temporary notes: Layer 2 blocks with `show_until`; an expired note is treated as switched off without a publish.
- Web page re-read: weekly scheduled job; changed content becomes a draft for review; two failures in a row → in-app notice + email; the last good version stays live.

### 6. Observability and evals
- Each run stores: instruction version, knowledge section ids, tool calls with trimmed results, guardrail results, model, tokens and latency (SEC-40: tenant-scoped, retention-limited). The team sees it as "Why this answer".
  - _Built (Issue #50, PR 1):_ `ai.runs` gets `message_id` (the answer it sent; a disclosure before it has no trace), `instruction_version` (`Prompt.AgentVersion` + 8 hex of the tone and owner guidance) and `trace` (jsonb: tool calls with arguments and results cut at 500 characters, and the reply check). The delivery details of `create_order_request` are never stored, only which fields were given. Traces are cleared after 90 days by `ai.purge_old_traces` (`Ai:TraceRetentionDays`); the run row stays for limits and metering. Every member sees traces and rates replies (`ai.reply_feedback`, one rating per member and reply); Owner/Admin export 👎 replies as eval cases to clean by hand, never added to the repo automatically.
- An eval runner (CLI/test project, not in production paths) runs a JSON eval set against a seeded test workspace: retrieval recall@4, answer faithfulness (judge model), made-up prices/policies (must be 0), handoff correctness. Results are saved and compared with the previous run; it runs before prompt, model or search changes.
  - _Built (Issue #50, PR 3):_ `backend/tests/Evals` (console app; CI builds it, never runs it) seeds the made-up shop `docs/evals/test-shop.json` through the API and sends the 50 cases of `docs/evals/eval-set.json` through the test chat with the real provider. Made-up prices are checked by code against the shop's and the turn's tool amounts; a judge model (default `gpt-5`) grades each outcome and flags made-up facts. The runner exits 1 when either is above 0. Runs go to `docs/evals/runs/`, the comparison to `docs/evals/report.md`. Order cases stop at the quote, because the test chat can't create order requests.

### 7. Cost
- Static-first prompt order (above) for provider prompt caching.
- **Debounce:** the AI waits a few seconds (configurable, default 4 s) after a customer message for more messages, then replies once.
- **Model routing:** cheap model for query rewrite, language/intent and summaries; main model only for the reply.
- Tool outputs trimmed (top 5 products, top 3–4 passages).
- Summaries, re-reads, re-embedding and evals run as background or batch jobs.
- No answer caching between customers.

## Alternatives considered
- **Separate vector database (Qdrant, Pinecone):** rejected for Phase 1. pgvector keeps tenant isolation (RLS), backups and transactions in one place; a few thousand sections per shop are small.
- **Fine-tuning a model on shop data:** rejected. Shop data changes daily; fine-tuning is slow, costly, hard to delete per customer (SEC-45) and does not stop invented facts.
- **Sending whole documents every time:** rejected: expensive, slower and less accurate than finding the right sections.
- **Docling/Unstructured as a Python service now:** postponed. Better on scans and complex layouts, but adds a second runtime; .NET parsers cover the Phase 1 limits.
- **Hosted RAG (OpenAI file search / vector stores):** rejected: less control over tenant isolation, deletion, review and sources, and it ties us to one provider (`ILlmProvider` stays provider-agnostic).

## Consequences
- New dependencies to justify in their PRs (SEC-20): PdfPig, DocumentFormat.OpenXml, AngleSharp, SmartReader, Pgvector for EF Core.
- New Critical tests: sections, search and memory are tenant-isolated; customers never see draft content.
- Knowledge search changes from in-memory to SQL; the existing FAQ and policies are migrated into sections (expand → migrate → contract, rule 4).
- Erasure (SEC-45) must also delete sections, embeddings and conversation summaries of a contact or workspace.
- Built in steps (issues): evals and trace → ingestion → review → retrieval → memory → draft/publish → cost.
