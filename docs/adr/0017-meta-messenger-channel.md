# ADR 0017: Messenger channel — connection, webhooks and delivery

Date: 2026-10-28 · Status: Accepted

## Context
Messenger is the first Meta channel (Instagram and WhatsApp follow). docs/architecture.md plans RabbitMQ + MassTransit for webhook processing. Local development and the beta run one API instance; RabbitMQ and Redis are not configured yet. SignalR has no backplane, so only the API process can push to dashboards.

## Decision
- **Connect** with Facebook Login: an Owner or Admin starts the login from the dashboard. The callback exchanges the code for a long-lived user token and stores each granted Page's token (`ChannelConnection`). It also subscribes the app to the Page's `messages` webhook.
  - The OAuth `state` is a time-limited (10 min), Data-Protection-encrypted value bound to the workspace and user. The callback is the one state-changing GET; `state` plays the role of the CSRF token.
  - Tokens are encrypted with Data Protection (SEC-28), never returned by any API, and deleted on disconnect.
  - A Page can be connected to one workspace only.
- **Webhooks:** `POST /api/v1/webhooks/meta` checks `X-Hub-Signature-256` on the raw body with the app secret in constant time (SEC-17). It stores the payload encrypted in `channels.webhook_events` and answers 200 at once. A background processor:
  - finds the workspace through the Page ID with a narrow `SECURITY DEFINER` lookup;
  - hands text messages to `Inbox.Contracts.IInboxIngest`; the channel message ID de-duplicates retries;
  - skips echoes of the Page's own messages;
  - deletes processed events after 7 days.
- **Delivery:** a team reply on a connected channel is stored as `pending`. A background sender sends it through the Send API (`messaging_type = RESPONSE`) and marks it `sent` with Meta's message ID. Temporary errors are retried with backoff; permanent ones are marked `failed`.
- **24-hour window (SEC-43):** replies are refused with `conversation.outside_window` when the customer's last message is older than 24 hours. The sender checks again before sending. Message tags and templates come later.
- **Where it runs:** both processors are hosted services **in the API**, using the database as the queue (`FOR UPDATE SKIP LOCKED`, safe with several instances), the same pattern as the email outbox (ADR 0013). This way new messages reach dashboards through SignalR immediately. They move to the Workers host together with a SignalR Redis backplane. RabbitMQ comes when AI jobs need it.

## Consequences
- No new infrastructure for the beta.
- Webhook traffic and the Send API share the API process. That is fine at beta volumes; revisit with the backplane.
- Messages a shop sends from Facebook's own Page inbox don't appear in Nihasdesk yet (echoes are skipped).
- Images and voice notes arrive as a placeholder text until media storage exists.
