# ADR 0021: Inbox team notes, pins, @mentions and the responder chip

Date: 2026-10-31 · Status: Accepted

## Context
PRD 4.4 (v0.7), Issue #34 part 1: notes move from the composer to the side panel, show as internal bubbles in the timeline, can be pinned and can `@mention` a team member. The header shows who answers (AI or a person) instead of the "AI is answering" banner. Notes must never reach the customer's channel or the AI provider (CLAUDE.md rule 7, SEC-37). There is no Notifications API yet.

## Decision
- **Notes stay internal messages** (`inbox.messages`, `internal = true`), so the existing guards keep working: the channel sender refuses internal messages (`QueueDelivery` throws) and the AI history query filters `!internal`. No separate notes table.
- **Pins and mentions are columns on `inbox.messages`** (additive migration `AddNotePinsAndMentions`): `pinned_at`, `pinned_by_user_id`, `mentioned_user_ids uuid[]` (default empty), partial index on pinned notes. RLS already covers the table.
- **Notes API** (every team member, like all inbox work): `GET/POST /api/v1/conversations/{id}/notes`, `PUT/DELETE /api/v1/conversations/{id}/notes/{noteId}/pin`. `GET` returns pinned notes and the 20 newest others. At most 5 pinned notes per conversation (`note.pin_limit`). A message that isn't a note, or belongs to another conversation or workspace, answers `404 note.not_found` (SEC-06). The composer's note mode is removed; `POST /messages` sends replies only.
- **Mentions** must be members of the workspace (`note.mention_not_member`), at most 10. They are delivered **in-app only for now**: the hub sends `mentioned` (`{ conversationId, noteId }`, IDs only) to the mentioned member's own group (`tenant:{id}:user:{id}`), and the dashboard shows an alert linking to the conversation. The author isn't notified about their own mention. Email and push come with the Notifications API (PRD 4.10).
- **Responder chip** in the header: "AI" or the assigned member's name ("Team" when unassigned); one click takes over or returns to AI. While the AI answers, the composer button reads "Send & take over". _(Replaced, see Update.)_

## Update 2026-10-31 (owner feedback on PR #35)
- **Header as before:** the "AI answering / Team answering" status and the Take over / Return to AI buttons are back; the responder chip is removed. Only the text under the composer stays removed (channel label, "AI is answering…" banner). "Send & take over" stays.
- **Pinned notes sit above the composer** in the conversation view (max 5, oldest pin first), on every screen size. Pinning no longer moves a note in the side panel: `GET /notes` now returns `{ items, pinned }`, where `items` are the 50 newest notes in time order (pinned and resolved included) and `pinned` feeds the bar above the composer.
- **Resolve:** `PUT/DELETE /api/v1/conversations/{id}/notes/{noteId}/resolve`; additive migration `AddNoteResolve` (`resolved_at`, `resolved_by_user_id`). A resolved note is unpinned, can't be pinned until reopened (`409 note.resolved`), is dimmed with "Resolved by {name}" and grouped under a collapsed "Resolved (n)" in the side panel, and dimmed with "Resolved" in the timeline. Resolved notes are still internal: never sent to the channel or the AI.

## Consequences
- A mentioned member who is offline sees the note in the conversation but gets no alert until the Notifications API exists.
- The side panel (and so adding and resolving notes from the list) is shown on wide screens only, as before; pinned notes above the composer show everywhere. A narrow-screen panel comes with the AI assistant tab.
- Tenant-isolation tests for notes are API tests (`tests/Api/Inbox`); adding them to `backend/tests/Critical` waits for the owner's approval, because that folder's fixture and project file would change.
