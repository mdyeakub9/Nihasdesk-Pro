# AI evals

Measures the AI agent before every prompt, model or search change (PRD 4.8, ADR 0026 §6). It runs the eval set against the **real agent** on a **made-up test shop** and compares the result with the last run.

| File | What it is |
|---|---|
| `eval-set.json` | The cases: a chat (the last message is the customer's) and what a good outcome is |
| `test-shop.json` | "Bottega Eval", a made-up shop in the API's own request shapes: products (one is a draft), shipping zones, policies, FAQ answers |
| `runs/<date>-<model>.json` | Every run, case by case. Commit them: the next run compares with the newest one |
| `report.md` | The latest run, the change since the run before, and the cases to look at |

## Running it

Needs Docker (Docker Desktop locally) and an OpenAI key for the EU project. The key is read only from the environment; it is never printed or saved.

```bash
Ai__OpenAi__ApiKey=sk-... dotnet run --project backend/tests/Evals -c Release
```

Options:
- `--dry-run`: scripted AI, no judge, nothing saved. It checks the files, the seeding and the runner without a key.
- `--limit 10`: only the first cases.
- `--judge-model gpt-5`: the grading model. The default is `gpt-5`, a stronger model than the reply model.
- `--set <path>` / `--shop <path>`: other files.

The reply model is the API's own setting (`Ai:ReplyModel`, by default `gpt-5-mini`). Change it with `Ai__ReplyModel=...` to compare models.

The endpoint is the API's default, the EU one (`https://eu.api.openai.com/v1/`, ADR 0006). It needs a key from an OpenAI project with EU data residency. A key from another project gets `http_401`/`http_403`. The runner then stops after three cases and saves nothing. For this test only, where every chat is made up, `Ai__OpenAi__BaseUrl=https://api.openai.com/v1/` can point it elsewhere. Real customer chats must stay on the EU endpoint.

A full run of 50 cases costs a few cents. It sends only the made-up shop and chats to the provider, never customer data.

## What is measured

- **Correct / acceptable / wrong**: a judge model grades each outcome against the case's expectation and the whole shop data.
- **Made-up prices reaching the customer, must be 0.** Every amount in a reply must be one the shop states, or one this turn's tools returned (quotes add up totals). This is checked by code, independently of the reply check.
- **Made-up facts, must be 0.** The judge flags a price, stock level, delivery time, policy, contact detail or product that is not in the shop data.
- **Handoff right**: the AI hands over exactly when the case expects it (unknown answer, complaint, wants a person).
- Drafts held back by the reply check, provider failures, tokens and time per turn, the model and the prompt version (`Prompt.AgentVersion` + guidance hash).
- **Retrieval recall@4**: "n/a" until hybrid search returns ranked sections (Issue #53).

The runner exits with code 1 when a made-up price or fact was found, so a change can't slip through unnoticed.

## Limits

- Cases go through the test chat (`POST /api/v1/ai/test`): the same brain, prompt and tools as real chats. An order request can't be created there (that tool needs a real conversation), so order cases go as far as the summary from `quote_order`.
- The judge is a model too. Read the "To look at" list in `report.md` before trusting a change in the numbers.

## Adding cases

- Write them against `test-shop.json`. If a case needs a new fact, add it to the shop first.
- `expect.handoff: true` for questions the shop data can't answer. Otherwise `expect.answer` says what a correct reply contains (facts, not exact words).
- Keep Italian and English mixed, and the ids stable: the report compares cases by id.
- **👎 replies from real chats** (Settings → AI → "Download 👎 replies") contain customers' words. Remove names, phone numbers, emails and addresses, change the facts to the test shop's, then add the case. Never commit a real customer's message.
- Raise `version` in `eval-set.json` when cases change meaning, so reports show which set a run used.
