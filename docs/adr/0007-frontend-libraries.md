# ADR 0007: Frontend libraries for the dashboard

Date: 2026-10-27 · Status: Accepted

## Decision
The dashboard (`frontend/apps/web`) is React + Vite + TypeScript (ADR 0002) with:
- **Server state:** TanStack Query. All API data (conversations, messages, contacts, orders) lives in its cache; real-time SignalR events update that cache instead of a separate store.
- **Client UI state:** Zustand, only for small UI state (sidebar, selection, filters). No Redux: most state is server state, and Query + Zustand needs far less code. Redux Toolkit was considered and is a valid alternative, but the two approaches are not mixed.
- **Routing:** React Router.
- **UI:** Tailwind CSS + shadcn/ui (Radix) in `packages/ui`.
- **i18n:** i18next + react-i18next in `packages/i18n` (Italian + English, PRD 4.13).
- **Tests:** Vitest + Testing Library; Playwright for end-to-end flows later.
- **Added with the first feature that needs them:** React Hook Form + Zod (forms, starting with Identity), `@microsoft/signalr` (Inbox), TanStack Virtual (long lists), TanStack Table (orders, contacts), date-fns.
- **Tooling:** pnpm workspace (version pinned in `packageManager`), TypeScript strict, ESLint + Prettier. TypeScript stays on 6.0.x until typescript-eslint supports 7.

## Consequences
One pattern for data fetching and caching across all features; no hand-written loading/error/cache code. Libraries are added only when used, so the bundle stays small. Security rules are enforced by lint: no `dangerouslySetInnerHTML` (SEC-02) and no web storage for tokens or app data (SEC-14).
