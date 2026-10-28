# Integrations

_Status: draft._

- Adapter pattern: one interface per capability (`IChannelAdapter`, `IShippingProvider`, `ICommerceProvider`), one adapter per vendor.
- Channels: Meta Messenger, Instagram, WhatsApp **official Cloud API** only.
- Shipping: start with a tracking aggregator (AfterShip / 17TRACK) instead of carrier-by-carrier.
- Every integration: encrypted credentials, webhook signature verification, raw payload stored, idempotent processing, retries + circuit breaker (Polly).

## Meta (Messenger now; Instagram and WhatsApp next) — ADR 0017
- Connect with Facebook Login (Owner/Admin). Page tokens are stored encrypted (SEC-28), never returned by an API, and wiped on disconnect. A Page belongs to one workspace.
- Webhook `/api/v1/webhooks/meta`: signature (`X-Hub-Signature-256`) checked on the raw body before anything is stored; payload stored encrypted, processed in the background, purged after 7 days.
- The workspace of a webhook comes only from the connected Page ID (`channels.find_connection`), never from other payload fields.
- Replies go out through the Send API with `messaging_type = RESPONSE` inside the 24-hour window (SEC-43); temporary errors are retried, permanent ones mark the reply `failed`.
- Graph calls send tokens in the `Authorization` header or form bodies, with `appsecret_proof`; never in URLs (SEC-19).
