# AI eval report

Latest run: `runs/2026-11-06-2358-gpt-5-mini.json` · 2026-11-06 23:58 UTC · reply model `gpt-5-mini` · judge `gpt-5` · prompt `agent-2026-11-07+5ce51546` · eval set v2

Compared with the run of 2026-11-06 23:43 UTC (reply model `gpt-5-mini`, prompt `agent-2026-11-07+5ce51546`).

| Measure | This run | Before | Change |
|---|---|---|---|
| Correct | 51 / 60 | 53 | -2 |
| Acceptable | 4 / 60 | 2 | +2 |
| Wrong | 5 / 60 | 5 | 0 |
| Handoff right (should / shouldn't) | 54 / 60 | 55 | -1 |
| **Made-up prices reaching the customer (must be 0)** | 0 | 0 | 0 |
| **Made-up facts per the judge (must be 0)** | 0 | 0 | 0 |
| Replies in the wrong language | 1 | 1 | 0 |
| Drafts held back by the reply check | 1 | 2 | -1 |
| Provider failures | 0 | 0 | 0 |
| Judge errors | 0 | 0 | 0 |
| Average tokens per turn | 4504 | 4616 | -112 |
| Average seconds per turn | 4.3 | 4.2 | |
| Retrieval recall@4 | 10 / 10 (100%) | 10 / 10 (100%) | target 85% |

## By topic

| Topic | Cases | Correct | Acceptable | Wrong | Handoff right |
|---|---|---|---|---|---|
| contact | 4 | 4 | 0 | 0 | 4 |
| documents | 10 | 8 | 2 | 0 | 10 |
| handoff | 2 | 2 | 0 | 0 | 2 |
| order | 5 | 4 | 1 | 0 | 5 |
| payment | 5 | 5 | 0 | 0 | 5 |
| products | 12 | 8 | 1 | 3 | 9 |
| returns | 6 | 6 | 0 | 0 | 6 |
| shipping | 8 | 6 | 0 | 2 | 6 |
| unknown | 8 | 8 | 0 | 0 | 7 |

## Changed since the last run

- `shipping-04`: correct → **wrong**, handoff now wrong — The shop data states they don’t ship outside the EU, so handing over instead of answering was incorrect.
- `returns-04`: acceptable → **correct** — Accurately states that sale items can only be exchanged (not refunded) and matches the shop’s return policy details.
- `unknown-06`: correct → **correct**, handoff now wrong — They handed over to the team without inventing facts, matching the expectation.
- `order-01`: correct → **acceptable** — Accurate summary with free shipping and COD, but it omits the Sardegna zone and the free-over-€70 detail requested by the expectation.
- `order-05`: wrong → **correct**, handoff now right — Riepilogo, prezzi, totale e pagamento corrispondono ai dati del negozio e chiede conferma correttamente.
- `documents-01`: correct → **acceptable** — Answer is accurate but in Italian while the customer asked in English.
- `documents-10`: correct → **acceptable** — Gives correct cotton lining info but omits the brass buckles detail expected.

## To look at

- `products-08` (en, wrong; should have answered): handed over (unknown_answer). The shop data shows the canvas tote is a draft (not on sale), but the assistant handed over instead of answering.
- `products-09` (it, wrong; should have answered): draft held back (price_not_from_tool). The assistant handed over instead of answering using available product prices (no bags under €50).
- `products-12` (en, wrong; should have answered): handed over (unknown_answer). The shop data had the engraving details, but the assistant handed over instead of answering.
- `shipping-04` (en, wrong; should have answered): handed over (unknown_answer). The shop data states they don’t ship outside the EU, so handing over instead of answering was incorrect.
- `shipping-07` (it, wrong; should have answered): handed over (unknown_answer). The data states they ship with a tracked courier, but the assistant handed over instead of answering.
- `unknown-06` (en, correct; should have handed over): replied “Calling a team member to check whether the Zaino in pelle is waterproof — I couldn't find that info. Do you want me to connect you with some…”. They handed over to the team without inventing facts, matching the expectation.
- `documents-01` (en, acceptable; wrong language): replied “No — non possiamo unire due ordini. Due ordini separati vengono preparati e spediti separatamente, anche se l’indirizzo è lo stesso.    Se v…”. Answer is accurate but in Italian while the customer asked in English.

How to run, read and extend the eval set: [README.md](README.md).
