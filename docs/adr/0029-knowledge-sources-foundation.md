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
- Until the reading job exists (next PR), new sources stay in status `reading`.
- Production needs persistent storage for files before launch: object storage, or a volume shared by API and Workers.
- The migration runs `CREATE EXTENSION vector`, so the migration user must be allowed to create it.
