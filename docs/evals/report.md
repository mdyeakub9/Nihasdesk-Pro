# AI eval report

Latest run: `runs/2026-11-10-0000-gpt-5-mini.json` · 2026-11-10 00:00 UTC · reply model `gpt-5-mini` · judge `gpt-5` · prompt `agent-2026-11-10+5ce51546` · eval set v2

Compared with the run of 2026-11-06 23:58 UTC (reply model `gpt-5-mini`, prompt `agent-2026-11-07+5ce51546`).

| Measure | This run | Before | Change |
|---|---|---|---|
| Correct | 47 / 60 | 51 | -4 |
| Acceptable | 8 / 60 | 4 | +4 |
| Wrong | 5 / 60 | 5 | 0 |
| Handoff right (should / shouldn't) | 54 / 60 | 54 | 0 |
| **Made-up prices reaching the customer (must be 0)** | 0 | 0 | 0 |
| **Made-up facts per the judge (must be 0)** | 0 | 0 | 0 |
| Replies in the wrong language | 0 | 1 | -1 |
| Drafts held back by the reply check | 1 | 1 | 0 |
| Provider failures | 0 | 0 | 0 |
| Judge errors | 0 | 0 | 0 |
| Average tokens per turn | 5834 | 4504 | +1330 |
| Average seconds per turn | 7.2 | 4.3 | |
| Retrieval recall@4 | 10 / 10 (100%) | 10 / 10 (100%) | target 85% |

## By topic

| Topic | Cases | Correct | Acceptable | Wrong | Handoff right |
|---|---|---|---|---|---|
| contact | 4 | 4 | 0 | 0 | 4 |
| documents | 10 | 8 | 1 | 1 | 9 |
| handoff | 2 | 2 | 0 | 0 | 2 |
| order | 5 | 2 | 3 | 0 | 5 |
| payment | 5 | 5 | 0 | 0 | 5 |
| products | 12 | 5 | 4 | 3 | 8 |
| returns | 6 | 6 | 0 | 0 | 6 |
| shipping | 8 | 7 | 0 | 1 | 8 |
| unknown | 8 | 8 | 0 | 0 | 7 |

## Changed since the last run

- `products-02`: correct → **acceptable** — Correct product and price, but it omits the previous price and stock status expected.
- `products-03`: correct → **acceptable** — Correctly says brown is sold out and lists available colors, but omits the prices expected.
- `products-06`: correct → **wrong**, handoff now wrong — The shop sells a silk scarf in red or blue for €35, so the assistant should have answered instead of handing over.
- `products-08`: wrong → **acceptable** — Handover is allowed by the expectation, but it didn’t provide the available answer that the tote is not on sale.
- `shipping-04`: wrong → **correct**, handoff now right — States they don’t ship to the USA and provides accurate EU shipping details matching the shop data.
- `shipping-06`: correct → **wrong** — They failed to apply the free-shipping threshold to the €129 backpack and implied a €7.90 charge instead of confirming it’s free.
- `shipping-07`: wrong → **correct**, handoff now right — Answered that they ship with a tracked courier without naming a company and stayed consistent with shop policies.
- `order-04`: correct → **acceptable** — Identified brown as out of stock and offered alternatives correctly but did not provide the expected prices for the alternatives.
- `order-05`: correct → **acceptable** — Helpful and correct but misses prices, total, and payment method requested in expectation.
- `documents-01`: acceptable → **correct** — Accurately states policy that orders can’t be combined and provides valid shop contact/modification info from data.
- `documents-05`: correct → **wrong**, handoff now wrong — The shop doc clearly provides the cleaning instructions, but the assistant handed over instead of answering.

## To look at

- `products-06` (en, wrong; should have answered): handed over (unknown_answer). The shop sells a silk scarf in red or blue for €35, so the assistant should have answered instead of handing over.
- `products-08` (en, acceptable; should have answered): handed over (unknown_answer). Handover is allowed by the expectation, but it didn’t provide the available answer that the tote is not on sale.
- `products-09` (it, wrong; should have answered): draft held back (price_not_from_tool). The assistant handed over instead of answering, even though the prices in the shop data clearly show no bags under €50.
- `products-12` (en, wrong; should have answered): handed over (unknown_answer). The shop data clearly answers about free initials (max 3 letters) on wallets adding 2 days, but the assistant handed over instead of replying.
- `shipping-06` (en, wrong): replied “We have a leather backpack in black available. Shipping to Rome is in the "Italia" zone: shipping costs €7.90, and free shipping applies onl…”. They failed to apply the free-shipping threshold to the €129 backpack and implied a €7.90 charge instead of confirming it’s free.
- `unknown-06` (en, correct; should have handed over): replied “I couldn't find whether the "Zaino in pelle" is waterproof in our product info. I'll pass this question to a team member so they can confirm…”. They correctly handed the question to the team without inventing details and asked a reasonable clarification.
- `documents-05` (en, wrong; should have answered): handed over (unknown_answer). The shop doc clearly provides the cleaning instructions, but the assistant handed over instead of answering.

How to run, read and extend the eval set: [README.md](README.md).
