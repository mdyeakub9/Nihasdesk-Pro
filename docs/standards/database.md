# Database & Migrations

_Status: draft._

- PostgreSQL; EF Core code-first; one DbContext + schema per module.
- Base entity: `Id` (Guid v7), `TenantId`, `CreatedAt`, `UpdatedAt` (UTC), optional soft delete.
- Create migrations with `dotnet ef migrations add <Name>`; commit them with the code; review every migration file in the PR.
- Never edit a migration already applied to staging/production — add a new one.
- Destructive changes use expand → migrate data → contract across releases.
- Deploy: CI builds `dotnet ef migrations bundle`; it runs before the new app version starts (staging first, then production after an automatic backup). The app never migrates itself on startup.
- Local DB via Docker Compose with seed data. Production data never comes to local machines.
