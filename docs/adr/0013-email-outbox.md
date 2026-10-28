# ADR 0013: Email outbox delivered by Workers

Date: 2026-10-28 · Status: Accepted

## Context
Until now the API sent email over SMTP inside the request (ADR 0008). That made requests slow, turned an SMTP outage into a 500 after data was already saved, and let response time reveal whether an account exists (forgot-password with a known email took ~1 s, an unknown one ~20 ms), undoing the enumeration protection of SEC-26.

## Decision
- **Transactional outbox:** modules call `IEmailOutbox.EnqueueAsync`, which writes the email to an `email_outbox` table **in the module's own schema and DbContext**, inside the same transaction as the module's data. The API never talks to SMTP.
- **Delivery in Workers:** each module registers `EmailOutboxDispatcher<TContext>` through `IModule.AddWorkerServices`. It polls every 5 s, locks a batch with `FOR UPDATE SKIP LOCKED` (several Workers instances never send twice), sends via `IEmailSender` (SMTP, ADR 0008), and **deletes the row after sending** — no email archive.
- **Encrypted at rest:** the whole message (recipient, subject, bodies with tokens) is stored Data-Protection-encrypted with the shared key ring, so database access alone doesn't yield sign-in or reset links.
- **Retries:** 8 attempts with backoff 1 min → 5 min → 15 min → 1 h → 3 h → 6 h → 12 h (about a day). After the last one the row is marked failed, its content wiped, and a warning logged (feeds SEC-48 alerting). Errors are stored as exception type names only.
- **Local development** runs the Workers host next to the API (Visual Studio "multiple startup projects" or two `dotnet run`); there is no in-API shortcut, so local behaves like production.

## Consequences
Requests answer in the same time whether or not an email is queued. Email arrives a few seconds later and only while Workers runs. Tokens in queued emails expire normally (1 h), so a long outage can deliver dead links; resend/forgot recovers. Integration tests deliver the outbox on demand instead of running a host.
