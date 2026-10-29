# Shipping module

See docs/architecture.md. Layout: Domain/, Features/<UseCase>/, Infrastructure/, Contracts/, ShippingModule.cs

Phase 1: delivery zones with costs (ADR 0020), read by other modules through `Contracts/IShippingRates`.
Shipping integrations and tracking come in Phase 3.
