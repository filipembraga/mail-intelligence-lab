# Timeline

The complete dated history of this project, Phase 0 engineering through public release. This is deliberately more than a commit log — each entry includes why the milestone mattered or what it changed, not just what shipped. For the few moments worth a deeper story (bugs, pivots, a decision pulled forward under real pressure), see [How This Was Actually Built](README.md#how-this-was-actually-built) in the main README.

---

### 2026-07-25 — Scaffold, Azure AD registration, first authentication

Three commits in one day: a bare .NET console project, an Azure AD app registration (public client, device code flow, no redirect URI), and the first successful authenticated call to `/me` via Microsoft Graph. The riskiest unknown in the whole project — can this actually authenticate against a real personal mailbox at all — was resolved before a single line of mailbox-reading logic existed.

### 2026-07-26 — Paginated read of inbox metadata

First real read against the full mailbox: 64k+ messages via `PageIterator`, no delta query (not needed yet — every run reads the full inbox at this stage; see [ADR-002](README.md#adr-002--authenticating-against-microsoft-graph-sdk-device-code-and-a-persisted-cache) for why the SDK choice already anticipates delta for later). This is where the ~30-minute read time first became a real, felt constraint rather than an assumption.

### 2026-07-27 — Sender aggregation, size and age buckets

Metadata became a report: message count, body-length proxy, age buckets per sender. Also where the body-length-as-size-proxy decision was made — a placeholder chosen deliberately, with exact attachment measurement always intended as a later stage, not a gap that was missed.

### 2026-07-28 — CSV export

The report became a file that could actually be opened and explored, not just printed to console. First version of the raw/sanitized split for `docs/reports/`.

### 2026-07-31 — Persisted authentication cache

Six days after the first login, four commits deep, re-entering a device code on every single run had become real, repeated friction — not hypothetical. This is the one milestone pulled forward from wherever it might have eventually landed, specifically because the cost was already being paid. Shipping it surfaced two separate, non-obvious bugs in the same feature — see How This Was Actually Built for what they were.

### 2026-08-01 — Real attachment sizing, bounded parallelism

Body-length proxy was joined by exact attachment size, fetched under a `SemaphoreSlim(4)` respecting Graph's documented per-mailbox concurrency limit. Same day, a diagnostic-only counter — added purely as a sanity check, not in response to a known bug — revealed thousands of messages that `hasAttachments` was silently missing.

### 2026-08-01–02 — `cid:` heuristic validated at full scale

The diagnostic finding above was tested cheaply first, against a 1,000-message sample (11/11 hit rate), before committing to a full ~30-minute mailbox rerun to confirm it. At full scale: 3,265 of 3,317 candidates (98.4%) confirmed real, recovering 733.8 MB — 27.6% of all attachment weight measured — that the official flag alone had missed entirely. This closes Phase 0 engineering.

### 2026-08-04 — Repository made public, documentation complete

Phase 0 engineering closed Aug 1–2. After went into the README, the ADRs, and this timeline — written before the repo opened up, not after.
