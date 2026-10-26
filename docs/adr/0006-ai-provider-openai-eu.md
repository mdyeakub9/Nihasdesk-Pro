# ADR 0006: AI provider — OpenAI with European data residency

Date: 2026-10-26 · Status: Accepted

## Decision
Phase 1 uses the **OpenAI API in a project with European data residency** as the only LLM provider. Two tiers as in `docs/ai-agent.md`: a cheap model for routing/classification/simple replies and a stronger model for complex replies; speech-to-text for voice notes and vision for images from the same provider. Conditions (SEC-40): EU data residency enabled, no training on our data, DPA signed, zero/limited data retention where available. Concrete model names live in configuration, not in code, and are chosen with the eval set.

## Consequences
One vendor and one DPA keep Phase 1 simple. All calls go through `ILlmProvider`, so another provider (e.g. Azure OpenAI EU, Anthropic, Mistral) can be added later as a fallback or replacement without changing the agent logic; switching requires passing the eval set. Features unavailable under EU data residency must not be used. Token usage is metered per workspace for plan limits.
