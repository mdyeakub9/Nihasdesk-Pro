# Permissions

_Status: Phase 1. Roles: ADR 0011. Enforcement: SEC-10._

The API checks every permission on the server, reading the member's **current** role from the database on each request (a role change applies immediately). The dashboard only hides what a role can't do; that is never the security.

| Permission | Owner | Admin | Member |
|---|:-:|:-:|:-:|
| `team.view` — see members | ✅ | ✅ | ✅ |
| `team.invite` — invite, resend, revoke invitations (role Member) | ✅ | ✅ | ❌ |
| `team.invite_admin` — invite with role Admin | ✅ | ❌ | ❌ |
| `team.change_role` — Admin ⇄ Member | ✅ | ❌ | ❌ |
| `team.remove_member` — remove a Member | ✅ | ✅ | ❌ |
| `team.remove_admin` — remove an Admin | ✅ | ❌ | ❌ |
| Inbox work (read, reply, take over / return to AI, notes, status, assign, edit contacts; later order approvals) | ✅ | ✅ | ✅ |
| Settings, channels, AI, catalog, policies _(with those modules)_ | ✅ | ✅ | ❌ |
| Billing, workspace deletion, ownership transfer _(later)_ | ✅ | ❌ | ❌ |

## Rules
- A workspace has **exactly one Owner**. The Owner can't be removed, demoted or invited; ownership transfer comes later.
- Invitations carry the role **Admin** or **Member** only.
- Removing a member deletes their membership and signs out all their sessions for that workspace.
- Resources of another workspace answer **404**, never 403 (SEC-06).
- Team changes are audited (SEC-47) and will require re-authentication once it exists (ADR 0010, PR 3b).

## In code
`Nihasdesk.Modules.Identity.Domain.RolePermissions` holds this table; endpoints declare the permission they need (`RequirePermission`). Change both together, with a test per role. Work every role may do (inbox, contacts) uses `Identity.Contracts.RequireTeamMember`: the user must still be a member of the workspace.
