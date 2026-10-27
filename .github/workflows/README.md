# Workflows

See `docs/standards/git-workflow.md`.

- `backend.yml` — restore (locked), build, test, vulnerable-package check, secret scan (gitleaks)
- `frontend.yml` — install (frozen lockfile), format, lint, typecheck, test, build, `pnpm audit`
- `codeql.yml` — CodeQL for C# and TypeScript on PRs, `main` and weekly
