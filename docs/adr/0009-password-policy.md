# ADR 0009: Password policy

Date: 2026-10-27 · Status: Accepted

## Context
SEC-08 asks for a minimum length of 12, a breached-password check and no composition rules, but leaves the details open.

## Decision
- **Length 12–128 characters.** The upper bound only protects the hasher; any character is allowed, including spaces and Unicode.
- **No composition rules** (no forced digits, symbols or case) and no periodic forced changes.
- **Breached-password check with Have I Been Pwned (k-anonymity):** only the first 5 characters of the SHA-1 hash leave the server; a match rejects the password with a clear message. The check runs at signup, password change and reset.
- **Fail-open:** if the HIBP service is down or slow (short timeout), the password is accepted and a warning is logged without any part of the password or hash (SEC-19). Outbound calls go through the shared HTTP client with timeout and retry limits.
- Hashing stays as in SEC-08 (ASP.NET Core Identity hasher or Argon2id).

## Consequences
Users can pick long passphrases; weak-but-common passwords are blocked by the breach list rather than by rules. An HIBP outage weakens the check temporarily instead of blocking signups; the logged warning and SEC-48 alerting make such periods visible.
