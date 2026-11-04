# AI eval report

Latest run: `runs/2026-11-04-0136-gpt-5-mini.json` · 2026-11-04 01:36 UTC · reply model `gpt-5-mini` · judge `gpt-5` · prompt `agent-2026-11-04+5ce51546` · eval set v1

No earlier run to compare with: this is the baseline.

| Measure | This run | Before | Change |
|---|---|---|---|
| Correct | 32 / 50 | – |  |
| Acceptable | 3 / 50 | – |  |
| Wrong | 15 / 50 | – |  |
| Handoff right (should / shouldn't) | 37 / 50 | – |  |
| **Made-up prices reaching the customer (must be 0)** | 0 | – |  |
| **Made-up facts per the judge (must be 0)** | 0 | – |  |
| Drafts held back by the reply check | 1 | – |  |
| Provider failures | 0 | – |  |
| Judge errors | 0 | – |  |
| Average tokens per turn | 2926 | – |  |
| Average seconds per turn | 5.2 | – | |
| Retrieval recall@4 | n/a | – | comes with hybrid search (Issue #53) |

## By topic

| Topic | Cases | Correct | Acceptable | Wrong | Handoff right |
|---|---|---|---|---|---|
| contact | 4 | 4 | 0 | 0 | 4 |
| handoff | 2 | 2 | 0 | 0 | 2 |
| order | 5 | 1 | 0 | 4 | 1 |
| payment | 5 | 5 | 0 | 0 | 5 |
| products | 12 | 2 | 1 | 9 | 4 |
| returns | 6 | 6 | 0 | 0 | 6 |
| shipping | 8 | 4 | 2 | 2 | 7 |
| unknown | 8 | 8 | 0 | 0 | 8 |

## To look at

- `products-01` (it, wrong; should have answered): handed over (unknown_answer). Handover despite product availability and price being present in the shop data.
- `products-02` (en, wrong; should have answered): handed over (unknown_answer). The shop data has the backpack price and details, but the assistant handed over instead of answering.
- `products-03` (it, wrong; should have answered): handed over (unknown_answer). Handover was unnecessary—the shop data clearly shows brown is sold out and other colors with prices available.
- `products-04` (en, wrong; should have answered): handed over (unknown_answer). It handed over despite the product colors and availability being clearly in the shop data.
- `products-05` (it, wrong; should have answered): handed over (unknown_answer). The data clearly shows size 110 is out of stock and sizes 90/100 available at €35, but the assistant handed over instead of answering.
- `products-06` (en, wrong): replied “Al momento non abbiamo «silk scarf» nel catalogo. Vuoi che cerchi per altre parole (es. “silk shawl”, “silk scarf” in italiano, o sempliceme…”. Incorrectly says no silk scarves despite an active silk scarf in red or blue at €35.00 in the data.
- `products-09` (it, wrong; should have answered): draft held back (price_not_from_tool). Handover instead of answering even though product prices were in the shop data.
- `products-10` (en, wrong; should have answered): handed over (unknown_answer). Handover despite product prices in data clearly answering that cuoio is €95 and black €89.
- `products-12` (en, wrong; should have answered): handed over (unknown_answer). Should have answered using shop data about free initials (max 3 letters) and +2 days; instead handed over.
- `shipping-04` (en, wrong): replied “Al momento spediamo solo nelle zone elencate: Sardegna, resto d'Italia e Unione Europea (Francia, Germania, Spagna, Austria, Belgio, Paesi B…”. Suggests arranging extra‑EU shipping, which contradicts the policy that they do not ship outside the EU.
- `shipping-06` (en, wrong; should have answered): handed over (unknown_answer). The shop data clearly shows free shipping to Italy over €99 and the backpack is €129, so it should have answered yes instead of handing over.
- `order-01` (it, wrong; should have answered): handed over (unknown_answer). Handover was unnecessary since the shop data had all details to summarize the order and ask for confirmation.
- `order-02` (en, wrong; should have answered): handed over (unknown_answer). It needlessly handed over despite the shop data providing all info to summarize the order and request confirmation.
- `order-04` (en, wrong; should have answered): handed over (unknown_answer). Should inform brown is sold out and suggest black €89.00 or cuoio €95.00, but instead handed over despite data available.
- `order-05` (it, wrong; should have answered): handed over (unknown_answer). Handover instead of providing available product, shipping, and payment details and asking for confirmation.

How to run, read and extend the eval set: [README.md](README.md).
