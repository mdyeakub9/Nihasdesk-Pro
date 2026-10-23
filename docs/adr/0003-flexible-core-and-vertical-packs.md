# ADR 0003: Flexible core, vertical packs, e-commerce first

Date: 2026-10-23 · Status: Accepted

## Context
Nihasdesk will eventually serve many business types (online shops, agencies, salons, clinics, tourism) and more channels (email, possibly LinkedIn). Building for all of them at once would make the MVP endless; hard-coding one vertical would force a rewrite later.

## Decision
1. **Market and build one vertical first: e-commerce** (Italian SMB online shops). Agencies and booking businesses come later.
2. **Tenancy model from day one:** `Organization → Workspace`. A single shop has one workspace; an agency later has many. `TenantId` = workspace; organization groups workspaces for billing and agency access.
3. **Core vs vertical packs.** Core (inbox, channels, contacts, team, AI agent, knowledge base, leads, handoff) is business-type agnostic. A **vertical pack** contributes AI tools, prompt templates, dashboard navigation/pages and lead-pipeline stages. Phase 1 ships only the e-commerce pack.
4. **No business-type conditionals in code.** Code checks "is pack/feature X enabled for this workspace", never `businessType == "ecommerce"`.
5. **Onboarding builds a workspace profile** (business type, channels, languages, team size) that enables packs and features. The profile is editable later in settings.
6. **Channels via adapters with declared capabilities** (buttons, media, templates, 24h window, subject/threads). The message model includes room for email-style fields (subject, thread, cc) so email can be added without reshaping the inbox.
7. LinkedIn messaging API is restricted to approved partners — treated as "if possible", not planned.
8. **Commerce integrations (Shopify, WooCommerce) are Phase 2.** Phase 1 uses a native product catalog (manual entry + CSV import). Shopify/Woo later sync into the same catalog model.

## Consequences
- Slightly more design work up front (organization layer, pack registry, capability model), but no rewrite when adding verticals, agencies or email.
- Phase 1 AI cannot see live stock or orders; owners maintain the catalog themselves until Phase 2.
