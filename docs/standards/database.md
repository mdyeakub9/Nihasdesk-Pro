# Database & Migrations

_Status: draft._

- PostgreSQL; EF Core code-first; one DbContext + schema per module.
- Base entity: `Id` (Guid v7), `TenantId`, `CreatedAt`, `UpdatedAt` (UTC), optional soft delete.
- Create migrations with `dotnet ef migrations add <Name>`; commit them with the code; review every migration file in the PR.
- Never edit a migration already applied to staging/production — add a new one.
- Destructive changes use expand → migrate data → contract across releases.
- Deploy: CI builds `dotnet ef migrations bundle`; it runs before the new app version starts (staging first, then production after an automatic backup). The app never migrates itself on startup.
- Local DB via Docker Compose with seed data. Production data never comes to local machines.

## Tenant isolation (SEC-11)

Two independent layers; both are covered by `backend/tests/Critical/TenantIsolation`.

1. **EF Core.** Tenant-owned entities derive from `TenantEntity`. A module DbContext implements `ITenantDbContext` and calls `builder.ApplyTenantQueryFilters(this)` at the end of `OnModelCreating`: every query is filtered by the current tenant, and a query without a tenant throws. `TenantSaveChangesInterceptor` refuses to write a tenant-owned row for any other tenant.
2. **PostgreSQL Row-Level Security.** `TenantConnectionInterceptor` sets `app.tenant_id` on every connection EF opens (empty when there is no tenant). Each tenant-owned table gets the `tenant_isolation` policy: rows are visible and writable only when `tenant_id = app.tenant_id`; an empty setting matches nothing.

The tenant comes from `TenantContext` (scoped, set once per request or job from the authenticated context — never from request bodies or AI output).

### Database roles
| Role | Used by | Rights |
|---|---|---|
| `nihasdesk` (owner) | migrations only (`ConnectionStrings:PostgresMigrations`) | owns schemas and tables |
| `nihasdesk_app` | API and Workers (`ConnectionStrings:Postgres`) | data access only; not owner, not superuser, `NOBYPASSRLS` |

The app role must exist before migrations run (locally `infra/postgres/init`, in deployment created once by the operator).

### Checklist for a new module
- `services.AddModuleDbContext<TContext>(configuration, "<schema>")` in the module entry point.
- DbContext implements `ITenantDbContext` and calls `ApplyTenantQueryFilters(this)`.
- An `IDesignTimeDbContextFactory` like Identity's, using `PersistenceServiceCollectionExtensions.ConfigureNpgsql`.
- In the first migration: `migrationBuilder.GrantAppRoleAccess("<schema>")`; in every migration that creates a tenant-owned table: `migrationBuilder.EnableTenantRowLevelSecurity("<schema>", "<table>")`.
- Migration classes are made `internal` (only the module entry point is public) and the `Migrations/` folder has an `.editorconfig` with `generated_code = true`.
- Add the new tables to the Critical tenant isolation tests.
- Code that legitimately works across tenants (e.g. finding a user's workspaces at login) needs its own reviewed path; never `IgnoreQueryFilters()` in normal feature code.
