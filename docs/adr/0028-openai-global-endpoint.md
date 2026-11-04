# ADR 0028: OpenAI through its global endpoint

Date: 2026-11-05 · Status: Accepted · Replaces the EU data residency part of ADR 0006

## Context
ADR 0006 and CLAUDE.md rule 7 required an OpenAI project with European data residency. The owner wants to run with the standard OpenAI setup, because the product is meant to be sold globally in the future. The available key is from a standard project: the EU endpoint rejects it (`http_401`/`http_403`, seen in the eval runs).

## Decision
- The AI provider's default endpoint is `https://api.openai.com/v1/` (`Ai:OpenAi:BaseUrl`). It stays configurable, so a regional endpoint can come back without a code change.
- This applies to replies, the team's AI assistant, evals, and the embeddings of ADR 0026.
- Only calls to the AI provider change. The database, files, Redis and the email provider stay in the EU (Hetzner, ADR 0008).
- Unchanged: send the provider only what the reply needs (rule 7, SEC-37), no training on our data, a DPA with OpenAI, AI logs handled as personal data (SEC-40).

## Consequences
- Customer messages can be processed outside the EU, so "EU data residency" can't be promised to shops.
- **Before the first real customer in the EU** (owner's task; a legal question, ask a lawyer if unsure): accept OpenAI's DPA, check the transfer basis for data leaving the EU, name OpenAI as a processor in the privacy policy and in Nihasdesk's terms for shops.
- Regional features (per-customer data location) are a possible later plan feature.
