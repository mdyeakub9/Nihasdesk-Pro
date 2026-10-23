# Integrations

_Status: draft._

- Adapter pattern: one interface per capability (`IChannelAdapter`, `IShippingProvider`, `ICommerceProvider`), one adapter per vendor.
- Channels: Meta Messenger, Instagram, WhatsApp **official Cloud API** only.
- Shipping: start with a tracking aggregator (AfterShip / 17TRACK) instead of carrier-by-carrier.
- Every integration: encrypted credentials, webhook signature verification, raw payload stored, idempotent processing, retries + circuit breaker (Polly).
