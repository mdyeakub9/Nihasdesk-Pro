# frontend

pnpm monorepo: `apps/web` (dashboard), `packages/ui` (design system), `packages/i18n` (translations). Libraries: ADR 0007.

## Setup

Requirements: Node 24. pnpm comes through corepack in the version pinned in `package.json`:

```bash
corepack enable pnpm
```

If that fails with a permission error on Windows, run pnpm as `corepack pnpm …` instead.

```bash
cd frontend
pnpm install
pnpm dev
```

The dashboard runs on http://localhost:5173 and calls the API on http://localhost:5080 (override with `VITE_API_URL` in `apps/web/.env.local`, see `apps/web/.env.example`). Start the API first (`infra/README.md`) to see "API: Healthy".

## Scripts

| Command | What it does |
|---|---|
| `pnpm dev` | Dashboard dev server |
| `pnpm lint` | ESLint, including security rules (SEC-02, SEC-14) |
| `pnpm typecheck` | TypeScript in all packages |
| `pnpm test` | Vitest in all packages |
| `pnpm build` | Production build (no source maps, SEC-18) |
| `pnpm format` | Prettier |
