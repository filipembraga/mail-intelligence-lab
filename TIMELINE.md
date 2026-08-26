# Timeline

The complete dated history of this project, from Phase 0 discovery through the first real cleanup in Phase 1. This is deliberately more than a commit log — each entry includes why the milestone mattered or what it changed, not just what shipped. For the few moments worth a deeper story (bugs, pivots, a decision pulled forward under real pressure), see [How This Was Actually Built](README.md#how-this-was-actually-built) in the main README.

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

### 2026-08-08 — `From` vs `Sender`: a verification query before Phase 1's first line

Phase 1's executor needs to resolve a sender to its messages via a Graph `$filter`. Before writing that code, a single query in Graph Explorer checked whether the filter would agree with the Phase 0 report: for one university mailing list, Graph returned 721 messages and the report said 669. The cause was that Phase 0 aggregated on `sender` (the transmitting mailbox) while the natural filter targets `from` (the message author) — identical for ordinary mail, divergent on list and delegated sends. Phase 1 keys on `from`: it's what Outlook displays and what "this sender" means to someone deciding what to delete. Caught before a line of Phase 1 code existed, not after.

### 2026-08-09 — Full re-read on the corrected key, and a locale bug that grep couldn't find

A 27-minute re-read produced a report keyed on `from`: 64,833 messages, 2,655.8 MB of attachments, `cid:` hit rate unchanged at 98.4%. The same run surfaced a fourth instance of a language leak that had already been fixed three times in Phase 0 — console output reading `2.655,8 MB` and `63,0 req/s`. The words were English; the _numbers_ were still Brazilian, because `:N1` formats with the current culture. Invisible to a grep of the source, since the format string says nothing about locale. Fixed process-wide with a single `CultureInfo.DefaultThreadCurrentCulture` assignment.

### 2026-08-10 — Action plan generator: Phase 1 opens

The first Phase 1 commit reads the newest sender report and writes an editable plan file — one row per sender, one editable `Action` column, blank meaning keep. No Graph call, no write capability. Two design constraints came from outside the code: all numeric columns are emitted as integers, because a locale-aware spreadsheet parses `01.05` as a date and silently breaks sorting; and senders whose address can't be resolved by a Graph filter are excluded at generation rather than failing later.

### 2026-08-12 — A validator written for typos finds 17 bugs in the aggregation

The plan validator exists to catch hand-editing mistakes — duplicate rows, unrecognised actions. On its first run, against a plan file nobody had edited, it reported 17 duplicate senders. They weren't typos: Graph's `eq` on an email address is case-insensitive, but the report's `GroupBy` was not, so a mailbox sending with inconsistent header casing appeared as two rows that would resolve to the same messages. Normalisation was added to the plan generator rather than the report — the report stays a faithful record of what Graph returned, and every matching rule lives in the layer whose job is matching.

### 2026-08-17 — Preview, then the first deletion in the project's history

`preview` resolves each marked row against Graph and reports real count against planned count, writing nothing. It resolved four senders at 958 of 958, and one sender independently verified against Outlook. Only then did the executor ship. The first real run deleted 131 messages from a single dormant sender in 22 seconds, zero failures — a deliberately small, recoverable, independently verified target. The run before it aborted after 10 consecutive `ErrorAccessDenied` responses: the scope arrays had been updated in one of two places, so the cached credential still carried read-only scope. The circuit breaker stopped it at 10 attempts instead of 131 — the third variant of the same scope-mismatch bug class documented in [ADR-002](README.md#adr-002--authenticating-against-microsoft-graph-sdk-device-code-and-a-persisted-cache).

### 2026-08-17 — Where deleted mail actually goes

After the first successful deletion, the 131 messages were not in Deleted Items, not in the inbox, and not returned by an all-folders search. Three folder queries answered it: Graph's `DELETE` on a consumer mailbox is a soft delete into Recoverable Items → Deletions, bypassing Deleted Items entirely — the same destination as Shift+Delete, not the same as clicking the bin. Recoverable through Outlook's "Recover items deleted from this folder", and invisible everywhere else. A tool that deletes mail and cannot say where it went is incomplete, which is why this became the `verify` verb rather than a throwaway diagnostic.

### 2026-08-21 — `permanent-delete`, and the first real cleanup

Permanent deletion was added as a plan action value rather than a config flag: the destructive choice is recorded per row in the file being approved, printed in the confirmation, and written to the execution log — no invisible state can make a run unrecoverable. It requires typing `PURGE` rather than `DELETE`. Tested first on the same 131 messages, recovered and re-purged: `verify` confirmed 0 in Deletions and 131 in Purges, proving the two actions are genuinely different on a consumer mailbox. The full round then purged 4,144 messages across 21 senders in 11:37 with zero failures, including one sender already purged in the earlier test — resolved 0, acted 0, no error, demonstrating that re-running a plan is safe. Mailbox storage moved from 96% to 90% immediately, without waiting for any retention window. A same-day re-run against an already-purged plan produced zero new rows, confirming the safety property across a whole plan rather than one sender; a freshly generated plan then added 748 more messages, closing the baseline at 5,023 messages across 40 senders, storage 96% → 88%.

### 2026-08-24–25 — Commit 5, three more rounds, and where the friction moved

`feat: derive plan from report minus prior execution logs` ships — a plan now subtracts what execution logs already show as removed, excluding a sender once its count hits zero. It held under real load: three rounds followed, size-sorted then count-sorted once size stopped paying off — 153, 167, and 391 senders, purging 5,628, 2,080, and 34,359 messages respectively, the last moving storage by only two points despite dwarfing the others in message count, confirming size and count sorting find different senders. Mailbox storage 88% → 70%; totals to date, 47,221 messages purged, 10 failed, 750 distinct senders. The bottleneck was no longer discovery time but hand-marking a plan where no row predicts the next; `inspect <address> [--all]` shipped to cut the browser round-trip for individual judgement calls, though marking itself still points toward a UI rather than a CLI fix.
