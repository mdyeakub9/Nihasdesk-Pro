# ADR 0008: Transactional email — Brevo (EU) behind IEmailSender

Date: 2026-10-27 · Status: Accepted

## Context
Identity needs email from the start (verification, invitations, password reset), later also the daily summary (PRD 4.10). Customer and team data must stay in the EU (CLAUDE.md rule 7).

## Decision
- Production sends through **Brevo** (EU-based, EU data processing, DPA signed).
- All modules send through one **`IEmailSender`** interface in BuildingBlocks; the Brevo adapter is the only code that knows the provider, so a switch to **Amazon SES (EU region)** is a new adapter plus configuration.
- Local development uses **Mailpit** in `infra/docker-compose.yml` (SMTP capture with a web UI); no real email leaves a developer machine.
- The sending domain has **SPF, DKIM and DMARC** configured before the first production email.
- API keys live only in the server secret store (SEC-13); email addresses and message bodies are never logged (SEC-19).

## Consequences
One extra vendor and DPA. Templates are rendered by us and passed to the adapter as subject + HTML/text, so they survive a provider change. Deliverability depends on correct DNS; DMARC starts at `p=none` with reports, then moves to `quarantine` once reports are clean.
