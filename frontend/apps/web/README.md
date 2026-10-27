# web

React (Vite) dashboard: inbox, contacts, settings.

- `src/features/<feature>/` — feature code (hooks, components); server data via TanStack Query
- `src/stores/` — small client-only UI state (Zustand)
- `src/layouts/`, `src/pages/`, `src/routes.tsx` — app shell and routing
- `src/lib/api.ts` — API fetch with cookies; replaced by `@nihasdesk/api-client` once the OpenAPI spec exists
- `src/mocks/` — in-memory stand-ins for APIs that don't exist yet (inbox, notifications, session). Used by `pnpm dev` and tests only; each feature's `api.ts` reads them behind `import.meta.env.DEV`, so production builds contain no mock data and show empty states. When an API lands, change that feature's `api.ts` and delete its mock.
