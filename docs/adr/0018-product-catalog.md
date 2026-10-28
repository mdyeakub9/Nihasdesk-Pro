# ADR 0018: Product catalog follows the Shopify product model

Date: 2026-10-28 · Status: Accepted

## Context
PRD 4.6 needs a native catalog: shops add products by hand or import a CSV, and the AI takes prices and stock only from it (CLAUDE.md rule 5). Many beta shops come from Shopify or WooCommerce, and later we sync with both.

## Decision
- **Model = Shopify's**, so exports import as they are and a later sync maps one to one:
  - **Product:** title, handle (unique per workspace), plain-text description, status `draft` / `active` / `archived`, product type, category, vendor, tags, up to **3 option names**.
  - **Variant:** at least one per product, at most 100. Fields: SKU (unique per workspace when set), barcode (GTIN/EAN/UPC), option values, **price / compare-at price / cost in minor units** (integer cents) with an ISO currency, inventory tracking, stock, inventory policy `deny` / `continue`, weight in grams, requires shipping, position.
- **Money is never a decimal:** integer minor units plus currency (EUR default until the workspace currency setting exists). Totals are always computed on the server (SEC-41).
- **CSV import uses the Shopify product CSV columns.** Our template is a subset; unknown columns are ignored.
  - Rows are grouped by `Handle`; the first row carries the product fields. Rows without variant data (Shopify image rows) are skipped.
  - Products match by handle and variants by SKU, else by option values. Variants missing from the file are kept, never deleted.
  - Two steps: **preview** (validate, nothing saved, errors per row and column), then **apply** (same file, all or nothing, in one transaction).
- **SEC-04 for CSV:** at most 2 MB and 5,000 rows. The file must be valid UTF-8 text without NUL bytes, otherwise it is rejected.
  - It is parsed in memory within the request and never stored, so there is no object storage or malware scan for CSV.
  - Cells are data only. HTML in `Body (HTML)` becomes plain text (SEC-02).
  - The template download escapes formula-like cells (SEC-23).
- **Permissions:** every member can see the catalog (the inbox needs it). Only Owner and Admin create, edit and import (`catalog.manage`).
- **For the AI:** `Commerce.Contracts.ICatalog` returns **active** products only, without cost.
- **Images** come later with object storage (SEC-04).

## Consequences
- Shopify exports work without editing. WooCommerce's CSV needs its own column mapping later.
- A workspace currency setting and multi-currency are not part of this; every price stores its currency, so either can come later.
