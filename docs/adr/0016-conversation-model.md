# ADR 0016: Conversation model for the unified inbox

Date: 2026-10-28 · Status: Accepted

## Context
PRD 4.4 lists conversation statuses open / pending / resolved. The dashboard design (Figma, PR #11) uses Waiting / Open / Done plus a separate Unread filter. The Contacts and Inbox modules need one model before channels (website widget, Messenger, Instagram, WhatsApp) start delivering messages.

## Decision
- **Contacts module** owns people: one `Contact` per person per workspace and a `ContactIdentity` per channel address (`channel` + external ID, unique per workspace). The first inbound message creates both (PRD 4.5). Other modules use `Contracts/IContactDirectory`.
- **Inbox module** owns conversations and messages. **One conversation per contact and channel**; a new message on a `done` conversation reopens it.
- **Status** follows the dashboard: `waiting` (the customer waits for the team, e.g. after a handoff), `open`, `done`. Rules:
  - an inbound message moves `done` → `open`, and to `waiting` when a human is responding;
  - a team reply moves `waiting` → `open`;
  - team members can set any status.
- **Responder** `ai` | `human` says who answers right now. A team reply takes the conversation over (`human`); "return to AI" sets `ai`. `aiEnabled` switches the AI off for the conversation (PRD 4.8).
- **Unread is per team member**, not a status: a read marker per member and conversation, compared with the last inbound message.
- **Messages:** author `customer` | `ai` | `agent`; team messages keep the author's user ID and a name snapshot. **Internal notes** are messages with `internal = true` and are never sent to a channel or the AI. Inbound messages are de-duplicated by the channel's message ID. Text is limited to 4,000 characters; media comes with the channels.
- **Assignee** must be a member of the workspace (checked through `Identity.Contracts.ITeamDirectory`).
- **Realtime:** SignalR hub `/api/v1/hubs/inbox`. Each connection joins only its own workspace's group (tenant from the session, CLAUDE.md rule 1). Events carry **IDs only** (`conversationChanged`); the dashboard reloads the data through the API, so no message text travels on the socket. A Redis backplane is added when the API runs on more than one instance.
- Lists use **cursor pagination** ordered by last activity.

## Consequences
The API and dashboard speak the same statuses; PRD 4.4 is updated. Sending to a channel is not part of this step: team replies are stored and delivered once channels exist. Channel modules call `Inbox.Contracts.IInboxIngest` and never write Inbox tables directly.
