# AI eval report

Latest run: `runs/2026-11-06-2226-gpt-5-mini.json` · 2026-11-06 22:26 UTC · reply model `gpt-5-mini` · judge `gpt-5` · prompt `agent-2026-11-06.2+5ce51546` · eval set v2

Compared with the run of 2026-11-06 22:25 UTC (reply model `gpt-5-mini`, prompt `agent-2026-11-06.2+5ce51546`).

| Measure | This run | Before | Change |
|---|---|---|---|
| Correct | 48 / 60 | 42 | +6 |
| Acceptable | 9 / 60 | 12 | -3 |
| Wrong | 3 / 60 | 6 | -3 |
| Handoff right (should / shouldn't) | 57 / 60 | 56 | +1 |
| **Made-up prices reaching the customer (must be 0)** | 0 | 0 | 0 |
| **Made-up facts per the judge (must be 0)** | 0 | 2 | -2 |
| Replies in the wrong language | 5 | 9 | -4 |
| Drafts held back by the reply check | 1 | 1 | 0 |
| Provider failures | 0 | 0 | 0 |
| Judge errors | 0 | 0 | 0 |
| Average tokens per turn | 4464 | 4318 | +146 |
| Average seconds per turn | 6.5 | 6.5 | |
| Retrieval recall@4 | 10 / 10 (100%) | 10 / 10 (100%) | target 85% |

## By topic

| Topic | Cases | Correct | Acceptable | Wrong | Handoff right |
|---|---|---|---|---|---|
| contact | 4 | 3 | 1 | 0 | 4 |
| documents | 10 | 8 | 1 | 1 | 9 |
| handoff | 2 | 2 | 0 | 0 | 2 |
| order | 5 | 3 | 2 | 0 | 5 |
| payment | 5 | 4 | 1 | 0 | 5 |
| products | 12 | 8 | 2 | 2 | 10 |
| returns | 6 | 6 | 0 | 0 | 6 |
| shipping | 8 | 6 | 2 | 0 | 8 |
| unknown | 8 | 8 | 0 | 0 | 8 |

## Changed since the last run

- `products-02`: acceptable → **correct** — All details (price, compare-at price, color, stock) match the shop data and language matches the customer.
- `products-05`: acceptable → **correct** — All facts match the shop data: taglia 110 esaurita, 90 e 100 disponibili a €35, risposta in italiano.
- `products-06`: acceptable → **correct** — Lists the silk scarf with correct colors, size, price, and stock exactly matching the shop data.
- `products-08`: wrong → **correct**, handoff now right — Accurately states the tote isn’t available and correctly lists in-stock alternatives with correct prices and availability.
- `products-11`: correct → **acceptable** — Risponde in italiano e senza errori, ma non fornisce la specifica “vera pelle italiana conciata al vegetale, lavorata a Sassari” richiesta dall’aspettativa.
- `shipping-02`: acceptable → **correct** — Gives the correct Milan delivery time and shipping cost/free threshold exactly as in shop data.
- `payment-02`: correct → **acceptable** — Accurate payment info but the reply mixes languages instead of matching the customer’s English.
- `payment-04`: acceptable → **correct** — Accurately states cash-on-delivery only and no instalments, matching shop policy.
- `unknown-06`: wrong → **correct**, handoff now right — They correctly handed over to the team without adding unsupported info.
- `order-01`: wrong → **correct** — All details match shop data: price €89, Sardegna shipping free over €70, COD payment, and it asks for confirmation without confirming the order.
- `order-04`: correct → **acceptable** — The reply gives correct availability and prices but is in Italian while the customer wrote in English.
- `documents-01`: acceptable → **correct** — Accurately states that orders can’t be combined and correctly adds the two-hour modification window from the shop guide.
- `documents-05`: correct → **wrong**, handoff now wrong — The shop document contains clear leather care instructions, but the assistant handed over instead of answering.
- `documents-06`: wrong → **correct** — Matches shop guidance on leather care frequency and method, no extra unsupported facts, in Italian.

## To look at

- `products-09` (it, wrong; should have answered): draft held back (price_not_from_tool). Assistant handed over instead of answering with available bag prices from the shop data.
- `products-12` (en, wrong; should have answered): handed over (unknown_answer). The shop data clearly answers about free initials (max 3) on wallets/keyrings with +2 days, but the assistant handed over instead of replying.
- `shipping-08` (en, acceptable; wrong language): replied “Per Sassari (zona Sardegna) la spedizione costa €5.00. Lo spedizione è gratuita per ordini da €70.00 in su; tempi di consegna 1–2 giorni. Vu…”. Facts are correct for Sardinia shipping and timing, but the reply is in Italian while the customer asked in English.
- `payment-02` (en, acceptable; wrong language): replied “No, we don’t accept PayPal. Payment is only cash on delivery — you pay the courier in cash when the package arrives. Vuoi procedere con un o…”. Accurate payment info but the reply mixes languages instead of matching the customer’s English.
- `contact-02` (en, acceptable; wrong language): replied “Our phone number is +39 079 000 0000. Vuoi che ti aiuti con qualcosa via telefono o preferisci qui in chat?”. Gives the correct phone number but adds an Italian sentence to an English query.
- `order-02` (en, acceptable; wrong language): replied “Perfetto Marco — ricapitolando l’ordine prima che proceda:  Portafoglio uomo in pelle — Blu   Quantità: 2   Prezzo unitario: €39.00   Totale…”. All details match shop data, but the reply is in Italian while the customer spoke English.
- `order-04` (en, acceptable; wrong language): replied “Mi spiace, la variante Marrone della "Borsa a tracolla in pelle" è al momento esaurita. Posso offrirti le varianti disponibili: Nero a €89.0…”. The reply gives correct availability and prices but is in Italian while the customer wrote in English.
- `documents-05` (en, wrong; should have answered): handed over (unknown_answer). The shop document contains clear leather care instructions, but the assistant handed over instead of answering.

How to run, read and extend the eval set: [README.md](README.md).
