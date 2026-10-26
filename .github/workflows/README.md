# Workflows

See `docs/standards/git-workflow.md`.

- `backend.yml` — restore (locked), build, test, vulnerable-package check, secret scan (gitleaks)
- `codeql.yml` — CodeQL for C# on PRs, `main` and weekly

Frontend workflows are added with the frontend monorepo.
