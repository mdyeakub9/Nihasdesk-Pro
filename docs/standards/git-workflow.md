# Git & Workflow

_Status: draft._

- `main` always deployable and protected (PR + green CI required). CODEOWNERS on critical paths.
- Short-lived branches: `feat/...`, `fix/...`, `docs/...`. One Issue → one branch → one PR.
- Conventional Commits. Releases tagged `vX.Y.Z`.
- CI (GitHub Actions): build, tests (unit, integration, architecture, critical), lint, typecheck, OpenAPI breaking-change check.
- Deploy: merge to `main` → staging (Coolify); tag `v*` → production.
- Every Issue contains: context, acceptance criteria, folders in scope, what must not change.
