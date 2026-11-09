# ADR 0030: Knowledge from a whole website, given only its domain

Date: 2026-11-09 · Status: Accepted (Issue #80)

## Context
The owner could add web pages only one at a time. The PRD listed whole-website crawling as "Later". The owner decided to build it now: a shop owner gives `example.com` and doesn't have to look for the right pages.

## Decision
- **Finding pages is a background job in Workers.** It reads `robots.txt`, then the sitemap. Without a usable sitemap it reads the links on the home page and on up to 10 of its most promising pages. It reads addresses, not content.
- **Choosing is done by code, not by a model.** Words in the link and address, in English and Italian, mark a main page (about, FAQ, shipping, returns, payment, contact, terms of sale). The home page is always chosen, and at most 15 are. It's cheap, works the same every time, and sends nothing to the AI provider.
- **Pages left out:** products and collections (they come from the catalog), cart, checkout, login, account, search, blog posts, privacy and cookie pages, other sites and subdomains, files, addresses with a query, and anything `robots.txt` forbids.
- **The owner sees the list with the main pages ticked and confirms it.** Without looking, an old offer page could become something the AI tells customers. It costs one click. The owner also confirms the site is theirs or that they may use it.
- **Each page added becomes an ordinary web page source,** with the same reading, review, conflict check and "Use for AI". "Use all for AI" uses every page that was read, and deleting a site deletes its pages.
- **Safety (SEC-05):** every request goes through `SafeHttpClient`. The job waits 1 second between requests and makes at most 60 for one site. The API only adds addresses the job found, so a request can't name what gets fetched. Sitemap XML is read with DTDs off. `robots.txt` patterns run with no backtracking, because the site writes them.

## Consequences
- The 100 source limit counts these pages, and a workspace has at most 10 sites.
- `robots.txt` is checked when finding pages, not again when reading them or later.
- A site that answers `robots.txt` with an error counts as having none.
- Pages that only show their text after JavaScript runs won't be read well; the reader gets the HTML the server sends.
- Duplicate language versions (`/en/faq` and `/it/faq`) are both chosen, which can use up the 15.
- Reading pages again when they change stays with #55.
