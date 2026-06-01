## Context

Adding a game (manual or scan) inserts the `Game` row and then fires a best-effort artwork fetch as fire-and-forget: `GameLibrary.AddGameAsync` calls `_ = FetchArtworkSafelyAsync(game.Id)`, which delegates to `ArtworkService.FetchArtworkAsync`. That fetch can download cover/hero/logo art and, on a first fetch of an auto-derived name, replace the game's name with the matched SteamGridDB title.

Today the only signal back to the UI is `IArtworkService.ArtworkUpdated` (game id), raised **only when something actually changed and was saved**. `GameCollectionViewModel` subscribes and calls a full `RefreshAsync`, rebuilding every `GameTileViewModel`. There is no signal for "fetch started" or "fetch failed":
- A no-API-key configuration is a silent early return.
- A no-match / no-asset / network error leaves the tile on a placeholder with no event raised.
- `FetchArtworkSafelyAsync` swallows all exceptions.

So a user cannot distinguish "still fetching" from "failed and gave up." This change adds in-session feedback. The established precedent is the live running badge: `IPlayTracker.SessionStarted/SessionEnded` → `App.RunOnUiAsync` → a targeted per-tile update (`OnSessionStartedAsync`) that sets an `[ObservableProperty]` on the matching `GameTileViewModel`, with `ApplyRunningState` re-applying it whenever tiles are rebuilt. We mirror that precedent.

## Goals / Non-Goals

**Goals:**
- Show, per game tile, that an artwork/name fetch is **in progress**.
- Show, per game tile, that a fetch **failed** (no match or error), with a way to **retry**.
- Keep success implicit — the art/name updating is the success signal (no extra "done" badge).
- Reuse the existing event → `RunOnUiAsync` → targeted-tile-update pattern; no new threading model.
- No database schema change / no EF migration.

**Non-Goals:**
- Persisting fetch status across app restarts (status is in-session only).
- Surfacing per-game feedback when no SteamGridDB API key is configured (no fetch is attempted; placeholder behaviour is unchanged).
- A global progress UI, queue view, or notification center for batch fetches.
- Changing match resolution, throttling, caching, or name-adoption logic.

## Decisions

### 1. Transient in-memory status via events, not a persisted column
Add lifecycle events to `IArtworkService` and hold the resulting status in memory; do **not** add a `Game`/`Artwork` status column.
- Rationale: fetch status is a runtime concern, not domain data; avoids an EF migration; matches the existing event-driven, derived-state style (play stats are derived, running state is in-memory). The durable remediation for a missing-art game already exists ("refetch missing artwork").
- Alternative considered: a persisted `FetchState` enum on `Game`. Rejected — adds a migration and a denormalized field that would need careful reconciliation on startup, for state that is only meaningful live.

### 2. New events: `ArtworkFetchStarted` and `ArtworkFetchFailed`; success stays `ArtworkUpdated`
Extend `IArtworkService` with `event EventHandler<int>? ArtworkFetchStarted` and `event EventHandler<int>? ArtworkFetchFailed` (game id payload, consistent with the existing events). `ArtworkUpdated` remains the success signal.
- In `ArtworkService.FetchArtworkAsync`: after the API-key check and after computing the needed artwork kinds, if a network fetch will actually be attempted, raise `ArtworkFetchStarted`. Wrap the resolve/download body in try/catch. On exception, or when the attempt completes with **no change** (no match resolved and no asset/name update), raise `ArtworkFetchFailed`. On a real change, save and raise `ArtworkUpdated` as today.
- "Started" is raised before the `FetchGate` semaphore is entered, so games queued behind a batch show "fetching" immediately (they are genuinely queued to fetch).
- No-API-key and "nothing needed (already complete)" paths raise **neither** started nor failed — there is no attempt to report.
- Rationale: keeps success/failure mutually exclusive and lets the UI map directly to None/Fetching/Failed. `FetchArtworkSafelyAsync` stays as the outer guard but the failure decision lives in the service where the outcome is known.

### 3. Authoritative status map in `GameCollectionViewModel`, re-applied on rebuild
The collection view model keeps a `Dictionary<int, ArtworkFetchStatus>` as the source of truth. Subscribe to all three events via `App.RunOnUiAsync`:
- `ArtworkFetchStarted(id)` → set map[id]=Fetching, targeted tile update.
- `ArtworkFetchFailed(id)` → set map[id]=Failed, targeted tile update.
- `ArtworkUpdated(id)` → clear map[id] (back to None) and `RefreshAsync` (rebuilds the tile so the new cover/name show).
Add `ApplyFetchStatus(tile)` alongside `ApplyRunningState(tile)` so that whenever tiles are rebuilt the status is re-applied from the map.
- Rationale: `ArtworkUpdated` triggers a full `RefreshAsync` that rebuilds **all** tiles. During a batch scan add, one game succeeding would otherwise wipe the "fetching" badges off every still-in-progress tile. The map survives the rebuild; `ApplyFetchStatus` restores each tile's badge. This is the exact role `ApplyRunningState` plays for the running badge.
- Alternative considered: make `ArtworkUpdated` do a targeted single-tile update instead of full refresh. Rejected for this change — the current full-refresh-on-update behaviour is relied upon elsewhere and changing it is out of scope; the map approach is additive and safer.

### 4. `ArtworkFetchStatus` enum + observable property on `GameTileViewModel`
Add `enum ArtworkFetchStatus { None, Fetching, Failed }` and an `[ObservableProperty] ArtworkFetchStatus _fetchStatus` on `GameTileViewModel`. The tile template binds badge visibility to it (via a value converter, like the existing bool→visibility usage). Fetching and Failed are distinct visuals.

### 5. Retry from a failed tile
Expose a retry command (on the collection VM, parameterised by game id, or on the tile) that calls `IArtworkService.FetchArtworkAsync(gameId, refetch: false)` again and optimistically sets status to Fetching. The subsequent `ArtworkFetchStarted`/`Failed`/`ArtworkUpdated` events drive the final state.
- Rationale: the plumbing already exists; retry turns a transient network failure into a one-click recovery instead of forcing the global "refetch missing artwork".

### 6. Visual placement reuses the running-badge overlay
Add the status indicator to the shared tile template (`LibraryView.xaml` / `RecentlyPlayedView.xaml`). Place it so it does not collide with the running badge (which sits top-right): e.g. a small top-left pill — a subtle spinner/"Fetching art…" for Fetching, and a warning pill that acts as the retry button for Failed. It must not block double-click/launch interaction. Exact styling settled during implementation + visual verification.

## Risks / Trade-offs

- **Full `RefreshAsync` on `ArtworkUpdated` drops in-progress badges during batch adds** → the authoritative status map + `ApplyFetchStatus` re-apply (Decision 3) preserves them across rebuilds.
- **Defining failure as "attempted but nothing changed" could mislabel a legitimate no-op single-game refetch as Failed** → this feature targets the add / first-fetch flow; a single-game refetch that genuinely finds nothing new showing Failed is acceptable (it did not find anything). Documented so it is a known, accepted edge.
- **Batch scan adds show many simultaneous "fetching" badges while the `FetchGate` serializes requests** → acceptable and honest (those games are queued to fetch); badges resolve in processing order.
- **Status lost on restart means a previously-failed game shows only a placeholder next launch** → accepted (Non-Goal). Per-tile retry covers the session; "refetch missing artwork" is the durable remediation.
- **Background-thread event races against a disposed/rebuilt VM** → no new exposure: reuse the same `App.RunOnUiAsync` marshalling and targeted-lookup-by-id guarding already used for `SessionStarted`.
- **An HTTP timeout must not be mistaken for cancellation** (found during apply): `HttpClient.Timeout` (30s) surfaces as `TaskCanceledException : OperationCanceledException`, so a blanket `catch (OperationCanceledException) { throw; }` would swallow a timeout without a terminal signal and leave the badge stuck on "fetching". Mitigation: only treat *our* token's cancellation as a silent abort (`when (cancellationToken.IsCancellationRequested)`); every other failure raises `ArtworkFetchFailed`. Additionally, each artwork kind is fetched under its own guard so a slow hero/logo can't discard an already-downloaded cover or abort the whole fetch.
- **The fetch must not block the UI thread** (found during apply): the fetch was invoked from UI-thread commands without offloading, so its network/file/DB I/O ran on the UI thread and could freeze it for up to the 30s HTTP timeout. Mitigation: `FetchArtworkAsync` now offloads its body via `Task.Run` (after a cheap no-key guard), so the work runs on the thread pool for every caller and the lifecycle events arrive on the UI thread through the existing `App.RunOnUiAsync` marshalling — the same background-thread→UI pattern used for play-session events. Consequence: non-cancellation failures are reported via the event and *not* rethrown, so a single slow game cannot abort the `FetchMissingForAllAsync` batch loop.

## Migration Plan

Purely additive code; no data migration. Rollback is a straight revert — removing the events/property/badge leaves the existing fetch behaviour intact.

## Open Questions

- Exact visual treatment of the Fetching/Failed indicators (spinner vs pill, icon, placement) — to be finalised during implementation and confirmed by running the app.
- Whether to debounce/suppress the Fetching badge for fetches that resolve near-instantly (cached/fast) to avoid a flicker — decide during verification; default is to show it regardless.
