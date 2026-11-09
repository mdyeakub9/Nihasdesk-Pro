# ADR 0031: Customer media — storing photos, voice notes and files, transcription and photo understanding

Date: 2026-11-09 · Status: Proposed (accepted part by part as the issues are built)

## Context
PRD v0.9 (4.4, 4.6, 4.8) asks that photos, voice notes, video and files from customers are shown in the inbox, that voice notes are transcribed and answered, that the AI recognises products in photos, and that products have images.

Today:
- `MessengerWebhookProcessor` stores the text `[attachment]` for any attachment (ADR 0017 left media for later). The file is not downloaded, so the team can't see it, and the AI hands off with reason `media`.
- Meta's webhook gives only a URL per attachment (`type`: image, audio, video, file, sticker). The URL is on Meta's CDN and is not permanent. WhatsApp (later) gives a media id that is fetched with the channel token.
- The catalog has no images; the Shopify CSV import skips image rows.
- `IFileStore` (local disk in dev, object storage later) and `SafeHttpClient` (SEC-05) exist (ADR 0029).

## Decision

### 1. Ownership and data
- **Inbox** owns attachments as part of messages. Table `inbox.message_attachments` (RLS, `TenantId`): message id, kind (`image`, `audio`, `video`, `file`, `sticker`), status (`pending`, `saved`, `failed`, `too_large`), failure code, content type (detected), size, duration (audio/video), width/height, sha256, storage keys (original, thumbnail, AI-size image), `transcript` + language + model (audio), `ai_description` (image, short), created/expires.
- **Channels** knows how to fetch media for its channel: `IChannelMediaFetcher` per channel (Messenger now; Instagram and WhatsApp later use the same interface). Parsing attachments goes into `InboundMessage` as a list of `{kind, sourceUrl or mediaId}`; the source URL is kept only until the download is done.
- **AiAgent** transcribes and looks at photos; it reads attachments only through a new Inbox contract `IMessageMedia` (get saved media of a message, set transcript, set AI description).
- **Commerce** owns product images (`commerce.product_images`: product id, optional variant id, position, storage keys, source URL for CSV, status).
- Storage keys are generated (`{tenant}/{area}/{guid}`), never from file names (SEC-04). Customer media is served to the dashboard only by an authenticated, tenant-checked endpoint with `Content-Disposition` and `X-Content-Type-Options: nosniff`.

### 2. Download and checks (Workers)
`webhook → message stored with attachment "pending" → MediaReceived event → fetcher downloads → checks → store → MediaSaved event`
- **Messenger fetcher:** only `https` URLs on Meta CDN hosts (allow-list: `*.fbcdn.net`, `*.fbsbx.com`; others → `failed`), through `SafeHttpClient` (no private IPs, redirects only to allow-listed hosts, timeouts).
- **Limits** (configurable): image 10 MB, audio 25 MB and 10 min, video and file 25 MB. The download stops when the limit is passed (streaming count), status `too_large`.
- **Type by content** (magic bytes), not by header or name; allowed: JPEG, PNG, WebP, GIF (first frame), HEIC (converted), MP4/M4A/AAC, OGG/Opus, MP3, PDF; anything else is stored as `file` and never opened by us or the AI.
- **Images are decoded and re-encoded** (JPEG/WebP): removes EXIF and GPS data, defeats polyglot files, produces a thumbnail (320 px) and an AI-size copy (max 1024 px). A pixel limit (40 MP) protects against decompression bombs. Library: **SkiaSharp** (MIT). ImageSharp was rejected for its revenue-based licence.
- **Audio:** duration read with `ffprobe`; converted with `ffmpeg` to a format the transcription API accepts when needed. `ffmpeg` is added to the Workers image only (SEC-20: pinned version, no network use).
- Retries with backoff for network errors; a link that has expired is `failed` with code `expired` (the team asks the customer to resend).
- Idempotent per attachment (sha256 + message id), so webhook retries don't download twice.

### 3. AI reply waits for media
- When the last customer message has pending media, the AI reply job waits until it is saved and, for audio, transcribed, at most 30 s; then it continues with what it has, or hands off with reason `media`.
- Debounce (ADR 0026 §7) groups "photo + 'do you have this?'" into one turn.

### 4. Voice notes
- `ITranscriber` behind the provider abstraction; default model `gpt-4o-mini-transcribe` (configuration `Ai:TranscriptionModel`), language hint from the contact's language (it/en), short prompt with shop and product names to help spelling.
- The transcript is stored on the attachment and shown to the team. It enters the conversation history as the customer's message text (`[voice note] …`).
- Hand off with reason `voice_note` when: longer than 2 minutes, empty or very short transcript with low confidence, unsupported language.
- Transcription runs even when the AI is off, so the team can read voice notes; it counts toward the workspace's AI usage.

### 5. Photos
Two steps, so code stays in control:
1. **Look:** one vision call (`Ai:VisionModel`, default the reply model, AI-size image, low detail) returns strict JSON: `kind` (`product_request`, `complaint`, `payment_or_order_screenshot`, `other`, `unclear`), a short neutral `description` (type, colour, material, visible brand or text summary, ≤ 40 words), `search_query`, `language`. The instructions forbid identifying or describing people. The description is stored on the attachment and used for later turns instead of sending the photo again.
2. **Act:** code decides:
   - `complaint` → handoff `complaint`; `payment_or_order_screenshot` → handoff `payment_screenshot`; `unclear` or `other` → the reply model may ask one question or hand off.
   - `product_request` → the reply model gets the description and the tool **`match_photo_to_products`**: code runs `search_products` with the search query, takes the top 5 with a main image, and asks the vision model to compare the customer photo with those product images; it returns up to 3 products with a match level (`likely`, `possible`) and never a price. The model then shows the product card(s) and asks "Is it this one?"; with no match it says so and may suggest similar products.
- Only the latest photo of the turn is sent (max 3 if sent together). Videos, files and stickers are never sent.
- Text inside photos is untrusted data (SEC-35). Guardrails stay: prices and stock only from tools (rule 5, SEC-38).
- The run trace stores the description, kind and matched product ids, not the image (SEC-40).

### 6. Team sends an image
- Upload in the composer (SEC-04: same checks and re-encode), stored as an outgoing attachment, sent with the Messenger Send API as an attachment upload, only inside the 24-hour window (SEC-43).

### 7. Product images
- Upload in the product editor (same checks, re-encode, thumbnail, card size, AI size).
- CSV import: `Image Src`, `Image Position`, `Variant Image` columns; a Workers job downloads them through `SafeHttpClient` (any public host, private IPs blocked, 10 MB, 20 per product); failures are listed per row.
- Product cards sent to Messenger need a public image URL: the API serves product images through **signed URLs** (`/media/p/{token}`, HMAC, 7-day expiry, product images only). Customer media is never public.

### 8. Retention, erasure, cost
- Customer media follows the workspace's conversation retention (SEC-44) and is deleted with the contact or workspace (SEC-45, Issue #75): originals, thumbnails, AI copies, transcripts and descriptions.
- Limits per conversation and workspace (SEC-39): at most 10 photo analyses and 10 transcriptions per conversation per hour; counted in usage (PRD 4.11).
- Expected cost per item is small (a voice note is a fraction of a cent per minute; a scaled-down photo is a few thousand tokens), measured by the eval runner before launch.

### 9. Evals
- The eval set gets media cases with fixture files in `docs/evals/media/`: product photos of the test shop (including variants and a product we don't sell), a damaged product, a payment screenshot, a photo with injected text ("ignore your rules…"), Italian and English voice notes (generated with TTS), a 3-minute voice note.
- Measured: right product among the first 3 (≥ 80%), handoff correctness for complaint and screenshot (100%), 0 prices from photos, transcript word accuracy on the fixtures.

### 10. As built in Issue #84 PR 1 (2026-11-09)
Changes to the plan above, the first two decided by the owner:
- **Types not on the list are refused, not kept as `file`:** status `failed`, code `type_not_allowed`. The team can't download something we don't know (SEC-04).
- **No live update from Workers:** the download job has no SignalR, so the dashboard refreshes the open conversation every 3 s while it has pending media.
- **Polling instead of `MediaReceived` / `MediaSaved` events:** MassTransit isn't used yet; the job polls `inbox.message_attachments` through `inbox.due_media` (IDs only), like the other jobs.
- **`IChannelMediaFetcher` lives in Inbox Contracts** and Channels implements it, because Channels already depends on Inbox.
- **`IMessageMedia` comes with its first caller** (#85, #87). Transcript, description and duration columns come with their issues; `ffprobe` and the 10-minute limit with #85.
- **`file_name`:** a cleaned name only for display and download. The extension comes from the detected type, and the name is never part of a storage key.
- **Extra failure code `source_not_allowed`:** a link outside Meta's CDN or to a blocked address.
- **Storage keys** are `{tenant}/{guid}` from the existing `IFileStore`.
- **SEC-04 deviation, recorded here:** SEC-04 says files are served through short-lived signed URLs. Customer media is instead served only to signed-in team members of the workspace (`GET /api/v1/media/{id}`), which is stricter. Signed URLs stay for product images (§7).
- **The file as sent is never kept for images:** only the re-encoded copy, the thumbnail and the AI copy.
- **AI handoff** looks at every customer message of the turn, not only the last one.

### 11. As built in Issue #84 PR 3 (2026-11-09)
Decided by the owner:
- **The team's photo is checked and re-encoded in the API, during the upload**, so it shows at once. It comes from a signed-in team member, not a stranger, and has the same 10 MB and 40 MP limits.
- **Retention keeps the row:** after `Inbox:Media:RetentionDays` (365, config only, no screen yet) the files go and the attachment becomes `deleted`; the chat says "Removed after the retention period". Counted from the message's time. Needs a legal check before launch.

Also:
- **Sending:** `POST /api/v1/conversations/{id}/images`. The photo goes to Meta as a file upload with the Send API message, so no public link to our storage is needed. No caption, no PDF. The AI copy isn't made for team photos.
- **Channels never sees storage keys:** the Inbox reads the photo and hands the bytes to the sender in `OutboundMessage.Image`. A photo removed before it was sent fails with `media_missing`.
- **Erasure:** `IMediaErasure` (per contact, per workspace) deletes the files and the rows. Nothing calls it until #75.
- **Retention lookup:** `inbox.expired_media(before, limit)`, `SECURITY DEFINER`, IDs only, like `due_media`. Files are deleted before the row changes, so a failure leaves the row due again instead of a forgotten file.
- **The + menu** switches "Send photo" off when the newest customer message on screen is more than 24 hours old; the server still decides (SEC-43).

## Alternatives considered
- **Image embeddings (CLIP-style) for product matching:** better at large catalogs, but needs an extra model and index. Phase 1 catalogs are small; text search + visual comparison of the top 5 is simpler. Revisit if catalogs grow past a few thousand products.
- **Sending the photo straight to the reply model with all tools:** fewer calls, but the model would decide alone whether a photo is a complaint. The two-step design lets code hand off reliably.
- **Keeping only Meta's URLs:** they expire and Meta can't be relied on for the team's history.
- **Transcribing on the device or with a self-hosted Whisper:** more privacy, but needs a GPU server; revisit with the EU-region provider question (ADR 0028).

## Consequences
- New dependencies to justify in their PRs (SEC-20): SkiaSharp, ffmpeg/ffprobe in the Workers image.
- New Critical tests: attachments and product images are tenant-isolated; the media endpoint refuses other workspaces' media.
- `ILlmProvider` gets image input and a transcription method; the eval runner supports media fixtures.
- Instagram and WhatsApp add only their fetcher and send code.
- The legal tasks of ADR 0028 now also cover photos and voice notes sent to the provider.
