# AI eval report

Latest run: `runs/2026-11-06-1820-gpt-5-mini.json` · 2026-11-06 18:20 UTC · reply model `gpt-5-mini` · judge `gpt-5` · prompt `agent-2026-11-06+5ce51546` · eval set v2

Compared with the run of 2026-11-06 18:00 UTC (reply model `gpt-5-mini`, prompt `agent-2026-11-06+5ce51546`).

| Measure | This run | Before | Change |
|---|---|---|---|
| Correct | 47 / 60 | 42 | +5 |
| Acceptable | 10 / 60 | 10 | 0 |
| Wrong | 3 / 60 | 8 | -5 |
| Handoff right (should / shouldn't) | 57 / 60 | 52 | +5 |
| **Made-up prices reaching the customer (must be 0)** | 0 | 0 | 0 |
| **Made-up facts per the judge (must be 0)** | 0 | 0 | 0 |
| Replies in the wrong language | 6 | 5 | +1 |
| Drafts held back by the reply check | 1 | 1 | 0 |
| Provider failures | 0 | 0 | 0 |
| Judge errors | 0 | 0 | 0 |
| Average tokens per turn | 3966 | 4092 | -126 |
| Average seconds per turn | 5.7 | 5.6 | |
| Retrieval recall@4 | 10 / 10 (100%) | 3 / 10 (30%) | target 85% |

## By topic

| Topic | Cases | Correct | Acceptable | Wrong | Handoff right |
|---|---|---|---|---|---|
| contact | 4 | 4 | 0 | 0 | 4 |
| documents | 10 | 7 | 2 | 1 | 9 |
| handoff | 2 | 2 | 0 | 0 | 2 |
| order | 5 | 3 | 2 | 0 | 5 |
| payment | 5 | 5 | 0 | 0 | 5 |
| products | 12 | 6 | 4 | 2 | 10 |
| returns | 6 | 4 | 2 | 0 | 6 |
| shipping | 8 | 8 | 0 | 0 | 8 |
| unknown | 8 | 8 | 0 | 0 | 8 |

## Changed since the last run

- `products-02`: acceptable → **correct** — All details (price, sale price, color, stock) exactly match the shop data and the language matches the customer.
- `products-06`: correct → **acceptable** — Accurate details but replied in Italian while the customer asked in English.
- `products-08`: correct → **acceptable** — Helpful and accurate, but ends with a sentence in Italian instead of fully replying in English.
- `products-11`: correct → **acceptable** — They asked for clarification instead of giving the available direct answer that the leather is genuine Italian vegetable-tanned, worked in Sassari.
- `shipping-04`: acceptable → **correct** — Accurately states they don’t ship outside the EU and lists shipping zones, prices, and times exactly as in the shop data.
- `shipping-08`: acceptable → **correct** — Gave correct Sardinia shipping cost (€5), free threshold (€70), and delivery time (1–2 days) per shop data.
- `returns-01`: acceptable → **correct** — Matches return policy details exactly and in Italian, with no extra invented facts.
- `returns-04`: correct → **acceptable** — Content matches policy but the reply is in Italian while the customer asked in English.
- `contact-02`: acceptable → **correct** — Gave the exact phone number from the shop data in the customer’s language without adding unsupported facts.
- `order-01`: correct → **acceptable** — No incorrect facts, but it missed price, shipping, total, and payment method that the expectation required.
- `order-02`: correct → **acceptable** — Accurate order summary and request to confirm, but the reply is in Italian while the customer used English.
- `order-04`: acceptable → **correct** — States brown is sold out and correctly offers black (€89.00) and cuoio (€95.00) with no extra claims.
- `documents-01`: wrong → **acceptable**, handoff now right — Answer is correct per shop data but it's in Italian while the customer asked in English.
- `documents-03`: wrong → **correct**, handoff now right — Accurately states the 24-month warranty for manufacturing defects and exclusions for normal wear and misuse.
- `documents-06`: acceptable → **correct** — Matches the shop guide: neutral cream in small amount every six months, with correct care tips.
- `documents-07`: wrong → **correct**, handoff now right — Accurately states packaging is a recycled cardboard box with a cotton bag, matching the shop document and language.
- `documents-08`: wrong → **correct**, handoff now right — Matches the shop guide exactly about two attempts, 5 days at depot, then return, in Italian.
- `documents-09`: acceptable → **correct** — Confirms free handwritten note and mentions adding the message in the order notes, matching shop info.
- `documents-10`: wrong → **acceptable**, handoff now right — Gives correct lining material but omits the brass buckles detail from the shop data.

## To look at

- `products-06` (en, acceptable; wrong language): replied “Sì — abbiamo una “Sciarpa di seta” (pura seta, 180 × 70 cm). È disponibile nei colori Rosso e Blu, prezzo €35.00 ciascuna; in stock 8 pezzi …”. Accurate details but replied in Italian while the customer asked in English.
- `products-08` (en, acceptable; wrong language): replied “Sorry — we don’t have a canvas tote in the catalog. The available bags I can see are leather: a Borsa a tracolla in pelle (variants: Nero €8…”. Helpful and accurate, but ends with a sentence in Italian instead of fully replying in English.
- `products-09` (it, wrong; should have answered): draft held back (price_not_from_tool). It handed over instead of answering even though the catalog clearly shows all bag prices (none under €50).
- `products-12` (en, wrong; should have answered): handed over (unknown_answer). The shop data clearly answers engraving details, but the assistant handed over instead of providing them.
- `returns-02` (en, acceptable; wrong language): replied “You have 14 days from delivery to return an item for free, as long as it’s unused and still has the tag. Refunds are issued within 7 days af…”. Accurate info but mixes Italian with English, not fully in the customer’s language.
- `returns-04` (en, acceptable; wrong language): replied “Sì — ma con una limitazione: i capi comprati in saldo non vengono rimborsati, possono solo essere cambiati. Il cambio è gratuito entro 14 gi…”. Content matches policy but the reply is in Italian while the customer asked in English.
- `order-02` (en, acceptable; wrong language): replied “Perfetto — riepilogo dell’ordine usando i tuoi dati:  Prodotto: Portafoglio uomo in pelle (Blu)   Quantità: 2   Prezzo unitario: €39.00   To…”. Accurate order summary and request to confirm, but the reply is in Italian while the customer used English.
- `documents-01` (en, acceptable; wrong language): replied “No — non si possono unire due ordini. Due ordini separati vengono preparati e spediti separatamente anche se vanno allo stesso indirizzo. Sp…”. Answer is correct per shop data but it's in Italian while the customer asked in English.
- `documents-05` (en, wrong; should have answered): handed over (unknown_answer). The shop guide contains the cleaning instructions, so handing over instead of answering is incorrect.

How to run, read and extend the eval set: [README.md](README.md).
