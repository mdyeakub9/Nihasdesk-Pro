# ADR 0029: Knowledge sources foundation — storage, URL fetching, and what waits

Date: 2026-11-05 · Status: Accepted

## Context
Issue #51 starts document and web page import (PRD 4.7, ADR 0026 §1–2). This records the choices of its first PR that ADR 0026 left open.

## Decision
- **`IFileStore`** (BuildingBlocks): random key per file under the workspace's folder, never the client's file name. Local folder for now (`Storage:LocalPath`, shared by API and Workers); S3-compatible object storage replaces it for production behind the same interface.
- **`SafeHttpClient`** (BuildingBlocks) is the only way to fetch a user-given URL (SEC-05). Each hop is checked before the request and the address is checked again at connect time (DNS rebinding). A name with any blocked address is refused. Adding a page checks the address at once, so a private URL fails immediately; fetching comes with the reading job.
- **`embedding vector(1536)` and `search_vector tsvector` are not mapped in EF.** Only search SQL uses them, so the `Pgvector` packages and a change to the shared Npgsql setup aren't needed (ADR 0026 planned the package).
- **Duplicates:** same content hash, or same page URL, in a workspace is refused by code and by unique indexes. The same file in another workspace is fine.

## Accepted for now: no malware scan (differs from SEC-04)
SEC-04 asks for a malware scan (ClamAV) before documents are processed. The owner decided to add it before launch, not now. Why this is acceptable until then: files are only read as text by in-process parsers, they are never served or executed, and only signed-in Owners and Admins can upload. **Must be done before real customers upload documents.**

## Consequences
- Sources stay in status `reading` until the Workers host reads them: it must be running.
- Production needs persistent storage for files before launch: object storage, or a volume shared by API and Workers.
- The migration runs `CREATE EXTENSION vector`, so the migration user must be allowed to create it.

## Reading (Issue #51, PR 2)
- `SourceReadProcessor` runs in the **Workers host** and finds work through the narrow `ai.due_sources` function. A source is claimed atomically; network and provider errors are tried 3 times with a growing wait; a file that can't be read fails at once with a code (`scanned`, `encrypted`, `too_many_pages`, `unreadable`, `no_text`, `too_complex`, `duplicate`, `not_a_page`, `blocked_url`, `invalid_url`, `page_too_large`, `page_unavailable`, `embedding_failed`, `read_failed`).
- Web pages: the `<main>` or `<article>` the page declares is read as it is. Otherwise SmartReader finds the content, and tables it dropped are added back at the end of the source, because SmartReader removes short tables, which often hold the facts.
- A table, list or FAQ pair is always its own section.
- Tokens are estimated (characters / 4): no tokenizer dependency for a size target.
- PDF layout, from the first real PDF: paragraphs by the document's usual line spacing, running headers and footers removed (same text in the top or bottom 12% of at least half the pages, 3 pages or more), a sentence or a question and answer cut by a page break stays together, and a section's page is where it starts.
- PDF tables have no structure, so they are found by wide gaps between columns (rows with ` | `). Cells that wrap over several lines come out mixed up. Reading them properly waits for the Docling upgrade path (ADR 0026).
- Without an API key sources are still read and found by keywords, with the warning `no_embeddings`.
- **Limit of SEC-04:** parsing has a time limit, but parsers run in process and can't be cancelled, so a stuck one stays on its thread until the Workers host restarts, and there is no per-file memory limit. Input size is limited (20 MB, 5 MB per page, 300 pages). Separate parser processes are for before launch, with the malware scan.
