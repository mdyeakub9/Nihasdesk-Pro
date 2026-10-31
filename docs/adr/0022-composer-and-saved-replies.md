# ADR 0022: Composer, saved replies and "X is typing…"

Date: 2026-11-01 · Status: Accepted

## Context
PRD 4.4 (v0.7), Issue #34 part 2: one Send for everything, a draft kept per conversation, "X is typing…", keyboard shortcuts, and saved replies inserted with `/` in the customer's language. Saved replies must only fill the composer, never send (Issue acceptance: one Send path).

## Decision
- **Saved replies live in the Inbox module**, table `inbox.saved_replies` (tenant-owned, RLS, additive migration `AddSavedReplies`): `shortcut` (lower-case letters, digits, dashes; unique per workspace), `title`, `text_en`, `text_it` (at least one). At most 200 per workspace.
- **API:** `GET /api/v1/saved-replies` for every member; `POST`, `PUT /{id}`, `DELETE /{id}` for Owner and Admin only (`RequireTeamRole("owner","admin")`, permission `inbox.saved_replies.manage`). Other workspaces' IDs answer 404 (SEC-06).
- **Inserting is done in the browser:** typing `/` (at the start or after a space) opens the list above the composer. Typing filters by shortcut or title; ↑↓ move, Enter or Tab insert, Esc closes. The text is the Italian version when the customer's language is Italian, otherwise English, falling back to the other one. `{first_name}` (first word of the contact's name) and `{shop_name}` (the workspace name) are filled in; unknown values become empty. The result goes into the composer to edit. Sending stays the normal Send, so there is one send path.
- **Composer:** Ctrl+Enter (⌘+Enter) sends; Enter is a new line. "Send & take over" while the AI answers. Drafts are kept per conversation in memory (a Zustand store), not in browser storage (customer data, SEC-14 spirit), so a reload starts empty.
- **Typing:** `POST /api/v1/conversations/{id}/typing` (rate limit `messages`), sent at most every 3 seconds while writing. The hub sends `typing` `{ conversationId, userId }` to the workspace group; nothing is stored and no text is sent. The dashboard shows "Name is typing…" for other members for 5 seconds after the last signal.

## Consequences
- Saved replies are plain text (SEC-02); no formatting or attachments yet.
- A draft is lost on page reload or when the tab closes.
- The `typing` event goes to every dashboard of the workspace; each one shows it only on that conversation.
- Tenant-isolation tests for `inbox.saved_replies` are API tests (`tests/Api/Inbox/SavedRepliesTests.cs`, RLS included). Adding them to `backend/tests/Critical` waits for the owner's approval, like ADR 0021.
