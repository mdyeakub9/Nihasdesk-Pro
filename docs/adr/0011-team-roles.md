# ADR 0011: Team roles — Owner, Admin, Member

Date: 2026-10-27 · Status: Accepted

## Context
The PRD named the roles Owner, Admin and **Agent**. In a product with an **AI agent**, "Agent" as a human role is easy to misread.

## Decision
The three workspace roles are **Owner**, **Admin** and **Member** (code values `owner`, `admin`, `member`; UI "Team member" / "Membro del team").
- **Owner:** one per workspace; everything, including billing, ownership transfer and workspace deletion.
- **Admin:** team, channels, AI settings, catalog and policies, plus all inbox work.
- **Member:** inbox work — reply, take over / return to AI, notes, approve or reject order requests, edit contacts. No settings, team or billing.

The detailed permission matrix goes into `docs/standards/permissions.md` with the Identity module.

## Consequences
"Agent" is used only for the AI agent. The rename happens before any role is stored, so no migration is needed.
