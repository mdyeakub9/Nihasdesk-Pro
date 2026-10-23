# ADR 0004: Payments — cash on delivery first, then Stripe Connect

Date: 2026-10-23 · Status: Accepted

## Context
Customers ordering through chat need a way to pay. Money must go to the shop, not to Nihasdesk, and Phase 1 should stay small.

## Decision
1. **Phase 1: cash on delivery only.** No online payment handling.
2. **First online method later: Stripe Connect**, with each shop connecting its **own** Stripe account. Charges are created on the shop's account; money goes directly to the shop.
3. Use the model where **Stripe handles pricing**: Stripe charges processing fees to the shop; the platform pays no Connect fees.
4. **No platform fee per transaction** at first (subscription is our revenue). An application fee can be reconsidered later.
5. Default flow: send a payment link **after** the owner approves the order request. Authorize-then-capture may be added later.
6. Payment methods are modelled as pluggable providers (`IPaymentProvider`), so more methods (PayPal, bank transfer, others) can be added without changing the order flow.

## Consequences
- Nihasdesk never holds customers' money, avoiding payment-institution obligations; refunds and disputes live in the shop's Stripe account.
- Phase 1 orders rely on COD, which limits some shops; acceptable for the MVP.
