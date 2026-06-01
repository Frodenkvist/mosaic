## 1. Artwork service lifecycle signals

- [x] 1.1 Add `event EventHandler<int>? ArtworkFetchStarted` and `event EventHandler<int>? ArtworkFetchFailed` to `IArtworkService` (alongside `ArtworkUpdated`).
- [x] 1.2 In `ArtworkService`, declare and raise the two new events; keep `ArtworkUpdated` as the success signal.
- [x] 1.3 In `FetchArtworkAsync`: after the API-key check and after computing the needed artwork kinds, raise `ArtworkFetchStarted` (before entering `FetchGate`) only when a network fetch will actually be attempted (key present AND at least one needed kind). Do not signal when no key is configured or nothing needs fetching.
- [x] 1.4 In `FetchArtworkAsync`: wrap the resolve/download body so that on an exception, or when the attempt completes with no change (no match resolved and no asset/name update), `ArtworkFetchFailed` is raised; on a real change, save and raise `ArtworkUpdated` as today. Ensure started/success/failure are mutually consistent (a successful attempt never also raises failed).
- [x] 1.5 Verify `GameLibrary.FetchArtworkSafelyAsync` still guards the fire-and-forget call (outer catch) but no longer hides the failure (the service now signals it).

## 2. Tile view model status

- [x] 2.1 Add `enum ArtworkFetchStatus { None, Fetching, Failed }` (in `ViewModels`).
- [x] 2.2 Add `[ObservableProperty] ArtworkFetchStatus _fetchStatus` to `GameTileViewModel`.

## 3. Collection view model wiring

- [x] 3.1 In `GameCollectionViewModel`, add an authoritative `Dictionary<int, ArtworkFetchStatus>` keyed by game id (source of truth that survives tile rebuilds).
- [x] 3.2 Subscribe to `Artwork.ArtworkFetchStarted` and `Artwork.ArtworkFetchFailed` via `App.RunOnUiAsync`, doing targeted per-tile updates by game id (mirror `OnSessionStartedAsync`): set the map entry to Fetching / Failed and update the matching tile's `FetchStatus`.
- [x] 3.3 Update the existing `ArtworkUpdated` handler to clear the map entry for that id (back to None) before/with `RefreshAsync`, so the rebuilt tile shows the new cover/name with no badge.
- [x] 3.4 Add `ApplyFetchStatus(tile)` (analogous to `ApplyRunningState`) and call it when tiles are (re)built in `RefreshAsync`, so in-progress/failed badges are preserved across the full refresh triggered by another game's success during batch adds.
- [x] 3.5 Add a retry command parameterised by game id that optimistically sets the tile/map to Fetching and calls `IArtworkService.FetchArtworkAsync(gameId, refetch: false)`.

## 4. Tile UI (views)

- [x] 4.1 Add an `ArtworkFetchStatus`→`Visibility` (and/or value) converter, or reuse an existing converter pattern, registered in the relevant resource dictionary. *(Reused the existing `BoolToVisibility` via `IsFetchingArtwork` / `ArtworkFetchFailed` projections on the tile VM; added a `StatusPillButton` style for the retry control.)*
- [x] 4.2 In the shared game-tile template (`LibraryView.xaml` / `RecentlyPlayedView.xaml`), add an in-progress indicator (subtle spinner / "Fetching art…" pill) bound to `FetchStatus == Fetching`, placed so it does not collide with the top-right running badge and does not block launch/double-click.
- [x] 4.3 Add a failed indicator bound to `FetchStatus == Failed` that acts as the retry control (invokes the retry command with the game id).

## 5. Tests

- [x] 5.1 Test that `FetchArtworkAsync` raises `ArtworkFetchStarted` then `ArtworkUpdated` on a successful fetch, and never `ArtworkFetchFailed`.
- [x] 5.2 Test that a no-match / no-asset outcome raises `ArtworkFetchStarted` then `ArtworkFetchFailed` and leaves the game on a placeholder.
- [x] 5.3 Test that with no API key configured, neither `ArtworkFetchStarted` nor `ArtworkFetchFailed` is raised.
- [x] 5.4 Test that a fetch with nothing needed (artwork already complete) raises no started/failed signal.

## 6. Verification

- [x] 6.1 Build (`dotnet build Mosaic.sln`) and run the test suite (`dotnet test`). *(Build succeeded, 0 errors; 31/31 tests pass incl. the 4 new lifecycle tests.)*
- [x] 6.2 Run the app and confirm by observation. *(Launched the built app and captured the Library — it renders cleanly with no XAML/resource/binding-load errors, confirming the new `StatusPillButton` style and tile-template changes parse and that the indicators stay hidden when games already have art. The transient Fetching/Failed badge states are covered by the 4 new service unit tests (5.1–5.4) and standard binding-pattern reuse; they were **not** force-triggered live because doing so means adding a game / refetching against the user's real `%LOCALAPPDATA%\Mosaic` library — left to the user to exercise on demand.)*

## 7. Fix: badge stuck on "Fetching" when a request times out (reported)

- [x] 7.1 Root cause: `HttpClient.Timeout` is 30s; a timeout throws `TaskCanceledException` (an `OperationCanceledException`), and `FetchArtworkAsync`'s `catch (OperationCanceledException) { throw; }` treated it as a silent abort — raising neither `ArtworkUpdated` nor `ArtworkFetchFailed`, so the badge stuck on "Fetching" and any downloaded art was discarded.
- [x] 7.2 Only treat *our* cancellation as a silent abort: `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }`; any other failure (incl. a timeout) raises `ArtworkFetchFailed`, guaranteeing a terminal signal after `ArtworkFetchStarted`.
- [x] 7.3 Guard each artwork kind independently in the download loop so a slow hero/logo timeout neither discards an already-downloaded cover nor aborts the whole fetch — partial art is saved and the fetch reports success.
- [x] 7.4 Make `OnArtworkUpdatedAsync` clear the tile's status directly (`SetFetchStatus(..., None)`) before `RefreshAsync`, so the badge clears even if the refresh faults.
- [x] 7.5 Added internal `AppPaths(string root)` ctor so tests can use a temp artwork dir without clobbering real cached art.
- [x] 7.6 Regression tests: a timeout during resolve raises `Started`+`Failed` (not stuck); a timeout on one kind still saves the cover and raises `Started`+`Updated`. (33/33 tests pass.)

## 8. Move the fetch off the UI thread (reported: UI sluggish during adds)

- [x] 8.1 Root cause: `FetchArtworkAsync` was invoked from UI-thread commands without offloading, so its network/file/DB I/O ran on the UI thread (blocking it up to the 30s HTTP timeout).
- [x] 8.2 Split `FetchArtworkAsync` into a thin public wrapper (cheap no-key guard, then `Task.Run(FetchArtworkCoreAsync)`) and the core logic, so every caller (add, retry, refetch-all) runs the fetch on the thread pool. Lifecycle events now fire from a background thread and are marshalled to the UI via `App.RunOnUiAsync` — matching the documented threading model for artwork/session events.
- [x] 8.3 With the fetch now genuinely parallel, stop rethrowing non-cancellation failures (incl. timeouts): the core reports them via `ArtworkFetchFailed` and swallows, so one slow game can no longer abort `FetchMissingForAllAsync`'s loop. Only genuine (our-token) cancellation rethrows. Updated the timeout regression test accordingly. (33/33 tests pass.)
