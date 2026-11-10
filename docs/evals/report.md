# AI eval report

Latest run: `runs/2026-11-10-1031-gpt-5-mini.json` · 2026-11-10 10:31 UTC · reply model `gpt-5-mini` · judge `gpt-5` · prompt `agent-2026-11-10.2+5ce51546` · eval set v2

Compared with the run of 2026-11-10 00:00 UTC (reply model `gpt-5-mini`, prompt `agent-2026-11-10+5ce51546`).

| Measure | This run | Before | Change |
|---|---|---|---|
| Correct | 46 / 60 | 47 | -1 |
| Acceptable | 9 / 60 | 8 | +1 |
| Wrong | 5 / 60 | 5 | 0 |
| Handoff right (should / shouldn't) | 54 / 60 | 54 | 0 |
| **Made-up prices reaching the customer (must be 0)** | 0 | 0 | 0 |
| **Made-up facts per the judge (must be 0)** | 0 | 0 | 0 |
| Replies in the wrong language | 0 | 0 | 0 |
| Drafts held back by the reply check | 1 | 1 | 0 |
| Provider failures | 0 | 0 | 0 |
| Judge errors | 0 | 0 | 0 |
| Average tokens per turn | 6065 | 5834 | +231 |
| Average seconds per turn | 7.1 | 7.2 | |
| Retrieval recall@4 | 10 / 10 (100%) | 10 / 10 (100%) | target 85% |

## By topic

| Topic | Cases | Correct | Acceptable | Wrong | Handoff right |
|---|---|---|---|---|---|
| contact | 4 | 4 | 0 | 0 | 4 |
| documents | 10 | 8 | 1 | 1 | 9 |
| handoff | 2 | 2 | 0 | 0 | 2 |
| order | 5 | 2 | 2 | 1 | 5 |
| payment | 5 | 5 | 0 | 0 | 5 |
| products | 12 | 6 | 5 | 1 | 10 |
| returns | 6 | 5 | 0 | 1 | 5 |
| shipping | 8 | 7 | 0 | 1 | 7 |
| unknown | 8 | 7 | 1 | 0 | 7 |

## Changed since the last run

- `products-06`: wrong → **correct**, handoff now right — Mentions the silk scarf with correct size, colors, in-stock status, and shows the correct €35.00 price per shop data.
- `products-12`: wrong → **acceptable**, handoff now right — Accurate about free engraving up to 3 letters and +2 days, but doesn’t mention keyrings as in the full policy.
- `shipping-06`: wrong → **correct** — Accurately states free shipping threshold for Italy and the backpack price, matching the shop data.
- `shipping-07`: correct → **wrong**, handoff now wrong — The shop data states they ship with a tracked courier, but the assistant handed over instead of answering.
- `returns-06`: correct → **wrong**, handoff now wrong — They handed over instead of answering with the clear return policy provided in the shop data.
- `unknown-06`: correct → **acceptable** — Did not provide false info but failed to directly hand over as required; asked for permission instead of handing off.
- `order-04`: acceptable → **wrong** — It should have said the brown variant is sold out and offered black (€89.00) or cuoio (€95.00); instead it handed over and claimed it couldn’t find the item.

## To look at

- `products-08` (en, acceptable; should have answered): handed over (unknown_answer). They handed over instead of stating the tote is not on sale, which is allowed but doesn’t answer the question.
- `products-09` (it, wrong; should have answered): draft held back (price_not_from_tool). The assistant handed over despite the shop data clearly answering that all bags are over €50.
- `shipping-07` (it, wrong; should have answered): handed over (unknown_answer). The shop data states they ship with a tracked courier, but the assistant handed over instead of answering.
- `returns-06` (en, wrong; should have answered): handed over (customer_asked_for_human). They handed over instead of answering with the clear return policy provided in the shop data.
- `unknown-06` (en, acceptable; should have handed over): replied “I’m not sure — the product info doesn't say whether the "Zaino in pelle" is waterproof. I'll ask a team member to confirm. Should I pass thi…”. Did not provide false info but failed to directly hand over as required; asked for permission instead of handing off.
- `order-04` (en, wrong): replied “Sorry — I couldn't find a brown shoulder bag in the catalog. I’ll connect you with a team member who can help locate it or offer alternative…”. It should have said the brown variant is sold out and offered black (€89.00) or cuoio (€95.00); instead it handed over and claimed it couldn’t find the item.
- `documents-05` (en, wrong; should have answered): handed over (unknown_answer). The shop document clearly provides the leather care instructions, but the assistant handed over instead of answering.

How to run, read and extend the eval set: [README.md](README.md).
