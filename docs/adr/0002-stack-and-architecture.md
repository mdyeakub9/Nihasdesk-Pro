# ADR 0002: Stack and architecture

Date: 2026-10-23 · Status: Accepted

## Decision
.NET modular monolith, PostgreSQL (schema per module, RLS, pgvector), Redis, RabbitMQ + MassTransit, SignalR; React (Vite) dashboard + Next.js marketing site in a pnpm monorepo; Hetzner (EU) + Coolify; Europe-first market.

## Consequences
Simple deployment and low cost early; clear module boundaries allow later extraction. EU hosting supports GDPR positioning. Managed database is not available on Hetzner, so backups/restore are our responsibility.
