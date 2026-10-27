# web

React (Vite) dashboard: inbox, contacts, settings.

- `src/features/<feature>/` — feature code (hooks, components); server data via TanStack Query
- `src/stores/` — small client-only UI state (Zustand)
- `src/layouts/`, `src/pages/`, `src/routes.tsx` — app shell and routing
- `src/lib/api.ts` — API fetch with cookies; replaced by `@nihasdesk/api-client` once the OpenAPI spec exists
