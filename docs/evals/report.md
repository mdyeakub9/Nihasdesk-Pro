# AI eval report

Latest run: `runs/2026-11-04-2153-gpt-5-mini.json` · 2026-11-04 21:53 UTC · reply model `gpt-5-mini` · judge `gpt-5` · prompt `agent-2026-11-05.2+5ce51546` · eval set v1

Compared with the run of 2026-11-04 21:44 UTC (reply model `gpt-5-mini`, prompt `agent-2026-11-05+5ce51546`).

| Measure | This run | Before | Change |
|---|---|---|---|
| Correct | 37 / 50 | 36 | +1 |
| Acceptable | 12 / 50 | 11 | +1 |
| Wrong | 1 / 50 | 3 | -2 |
| Handoff right (should / shouldn't) | 49 / 50 | 47 | +2 |
| **Made-up prices reaching the customer (must be 0)** | 0 | 0 | 0 |
| **Made-up facts per the judge (must be 0)** | 0 | 0 | 0 |
| Replies in the wrong language | 10 | 11 | -1 |
| Drafts held back by the reply check | 1 | 2 | -1 |
| Provider failures | 0 | 0 | 0 |
| Judge errors | 0 | 0 | 0 |
| Average tokens per turn | 4110 | 3922 | +188 |
| Average seconds per turn | 5.9 | 5.8 | |
| Retrieval recall@4 | n/a | n/a | comes with hybrid search (Issue #53) |

## By topic

| Topic | Cases | Correct | Acceptable | Wrong | Handoff right |
|---|---|---|---|---|---|
| contact | 4 | 3 | 1 | 0 | 4 |
| handoff | 2 | 2 | 0 | 0 | 2 |
| order | 5 | 4 | 1 | 0 | 5 |
| payment | 5 | 4 | 1 | 0 | 5 |
| products | 12 | 5 | 6 | 1 | 11 |
| returns | 6 | 4 | 2 | 0 | 6 |
| shipping | 8 | 7 | 1 | 0 | 8 |
| unknown | 8 | 8 | 0 | 0 | 8 |

## Changed since the last run

- `products-08`: correct → **acceptable** — Accurate offer and prices but mixes Italian with English, not fully in customer’s language.
- `products-11`: correct → **acceptable** — They asked for clarification instead of giving the known shop answer that all leather is genuine Italian vegetable-tanned, made in Sassari.
- `products-12`: wrong → **acceptable**, handoff now right — Accurate info about free 1–3 letter wallet engraving and +2 days, but replied in Italian and didn’t mention keyrings.
- `shipping-02`: acceptable → **correct** — Matches shipping zone Italia: 2–4 days, €7.90, free from €99.00.
- `shipping-04`: acceptable → **correct** — Accurately states they don’t ship to the USA and only within EU/Italy with correct shipping prices from shop data.
- `shipping-06`: correct → **acceptable** — All facts match the shop data, but the reply is in Italian while the customer asked in English.
- `shipping-08`: acceptable → **correct** — Accurately states Sardegna shipping cost, free-shipping threshold, and delivery time per shop data.
- `returns-01`: correct → **acceptable** — Accurate return conditions but omitted that the return is free, which the policy specifies.
- `payment-02`: acceptable → **correct** — Accurately states only cash on delivery is accepted and no PayPal, matching shop data.
- `contact-04`: acceptable → **correct** — Accurately states Sunday closure and correct Mon–Sat hours matching shop data in the customer’s language.
- `order-01`: wrong → **correct**, handoff now right — All details (price, shipping free in Sardegna over €70, total, payment) match the shop data and it asks for confirmation without auto-confirming.
- `order-04`: correct → **acceptable** — Gives correct stock and prices and offers alternatives, but replies in Italian instead of the customer’s English.

## To look at

- `products-02` (en, acceptable; wrong language): replied “The leather backpack (Zaino in pelle) is €129.00 (available in Nero, 4 in stock). Vuoi che te lo metta da parte o ti do le opzioni di spediz…”. Accurate price, color and stock but partly in Italian and missed the before €149.00 detail.
- `products-04` (en, acceptable; wrong language): replied “Il portafoglio uomo in pelle è disponibile in Black (Nero) e Blue (Blu). Abbiamo 12 pezzi in Nero e 6 in Blu. Vuoi che te ne tenga uno da pa…”. Accurate details but replied in Italian while the customer asked in English.
- `products-06` (en, acceptable; wrong language): replied “Sì, abbiamo la "Sciarpa di seta" (180 × 70 cm) in Rosso e Blu, disponibile a €35.00 ciascuna (8 pezzi in stock per colore). Vuoi che te ne t…”. Accurate details but reply is not in the customer’s language (mixed Italian/English).
- `products-08` (en, acceptable; wrong language): replied “Sorry, we don't have a canvas tote in the shop right now. I can offer these instead: the Borsa a tracolla in pelle — available in Nero (€89.…”. Accurate offer and prices but mixes Italian with English, not fully in customer’s language.
- `products-09` (it, wrong; should have answered): draft held back (price_not_from_tool). The shop data clearly had the prices, so the assistant should have answered instead of handing over.
- `products-12` (en, acceptable; wrong language): replied “Sì, possiamo incidere le iniziali sul portafoglio: è gratis fino a un massimo di 3 lettere. L'incisione aggiunge 2 giorni ai tempi di spediz…”. Accurate info about free 1–3 letter wallet engraving and +2 days, but replied in Italian and didn’t mention keyrings.
- `shipping-06` (en, acceptable; wrong language): replied “Sì — lo zaino in pelle costa €129.00 e per la zona "Italia" (che include Roma) la spedizione è gratuita sopra €99.00, quindi con questo acqu…”. All facts match the shop data, but the reply is in Italian while the customer asked in English.
- `returns-04` (en, acceptable; wrong language): replied “Sì — i resi sono gratuiti entro 14 giorni dalla consegna, se il prodotto non è stato usato e ha ancora l'etichetta. Il rimborso viene emesso…”. Facts match policy but the reply is in Italian while the customer asked in English.
- `payment-04` (en, acceptable; wrong language): replied “Al momento non è possibile pagare a rate. L’unica modalità disponibile è solo contrassegno: paghi in contanti al corriere alla consegna. Vuo…”. Accurate payment info but the reply is in Italian while the customer asked in English.
- `contact-02` (en, acceptable; wrong language): replied “Our phone number is +39 079 000 0000. Vuoi che ti aiuti con qualcos'altro?”. Gave the correct phone number but added a sentence in Italian instead of fully replying in English.
- `order-04` (en, acceptable; wrong language): replied “La borsa a tracolla marrone è al momento esaurita. Abbiamo però gli altri due colori disponibili: Nero a €89.00 e Cuoio a €95.00. Vuoi proce…”. Gives correct stock and prices and offers alternatives, but replies in Italian instead of the customer’s English.

How to run, read and extend the eval set: [README.md](README.md).
