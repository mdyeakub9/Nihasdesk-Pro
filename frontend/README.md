# frontend

pnpm monorepo: `apps/web` (dashboard), `packages/ui` (design system), `packages/i18n` (translations). Libraries: ADR 0007.

## Setup

Requirements: Node 24 and pnpm in the version pinned in `package.json` (`packageManager`):

```bash
corepack enable pnpm
```

If that fails with a permission error on Windows, install the same version globally instead (root scripts call `pnpm` themselves, so `corepack pnpm …` alone is not enough):

```bash
npm install -g pnpm@12.15.0
```

```bash
cd frontend
pnpm install
pnpm dev
```

The dashboard runs on http://localhost:5173. Vite proxies `/api` and `/health` to the API on https://localhost:5080, so the browser stays on one origin and the SameSite session/CSRF cookies work (see `apps/web/vite.config.ts`). Start the API **and** the Workers host first (`infra/README.md`): sign-up and password emails arrive in Mailpit (http://localhost:8025) only while Workers runs. Without the API every page redirects to the login screen.

## Scripts

| Command | What it does |
|---|---|
| `pnpm dev` | Dashboard dev server |
| `pnpm lint` | ESLint, including security rules (SEC-02, SEC-14) |
| `pnpm typecheck` | TypeScript in all packages |
| `pnpm test` | Vitest in all packages |
| `pnpm build` | Production build (no source maps, SEC-18) |
| `pnpm format` | Prettier |
