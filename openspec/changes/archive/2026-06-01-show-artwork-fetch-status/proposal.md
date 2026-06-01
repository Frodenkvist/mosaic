## Why

When a game is added (via folder scan or manually), Mosaic fires off a best-effort SteamGridDB fetch that may both download cover art and replace the auto-derived name — but the library gives no sign this is happening. A successful fetch is obvious (the tile's art and name update), yet while it is in flight the tile looks "finished," and when it fails (no match, network error) the tile silently stays on a placeholder with no explanation. Users can't tell "still working" apart from "gave up."

## What Changes

- Surface the artwork-fetch **lifecycle** for each game as it is added: a *fetching* state while SteamGridDB is being queried/downloaded, and a *failed* state when no match is found or the fetch errors. Success needs no badge — the art/name update already signals it.
- Add fetch lifecycle signals from the artwork service: today only a "something changed" event is raised on success. Add explicit *fetch-started* and *fetch-failed* signals so the UI can show progress and failure, not just completion.
- Show a per-tile status indicator in the library grid (and recently-played grid), reusing the existing running-badge overlay pattern, with a subtle in-progress indicator and a distinct failed indicator.
- Let a failed tile be retried (the underlying `FetchArtworkAsync` already supports re-resolving), so a user isn't stuck with a placeholder after a transient failure.
- Status is **transient/in-session** (not persisted): it reflects fetch attempts during the current run; restarting the app clears it. When no SteamGridDB API key is configured no fetch is attempted, so no fetching/failed indicator is shown (the existing placeholder behaviour is unchanged).

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `artwork`: add a requirement that the artwork fetch exposes its lifecycle (started / succeeded / failed) so callers and the UI can reflect in-progress and failed states, rather than only signalling on a successful change.
- `library-ui`: add a requirement that the library shows per-game artwork-fetch feedback — an in-progress indicator while fetching and a failed indicator (with retry) when it fails — complementing the existing live running feedback.

## Impact

- **Services**: `IArtworkService` / `ArtworkService` — new lifecycle events (`ArtworkFetchStarted`, `ArtworkFetchFailed`) raised around `FetchArtworkAsync`; the "no match / no asset" outcome must be distinguishable from a successful change. `GameLibrary.FetchArtworkSafelyAsync` (the fire-and-forget caller) participates in signalling start/failure.
- **ViewModels**: `GameTileViewModel` gains an observable fetch-status property; `GameCollectionViewModel` subscribes to the new events and applies targeted per-tile updates (mirroring `SessionStarted` handling) plus a retry command.
- **Views**: `LibraryView.xaml` / `RecentlyPlayedView.xaml` (or the shared tile template) gain a status overlay/badge.
- **No schema change**: status is in-memory; no new `Game`/`Artwork` columns, no EF migration.
- **Tests**: `Mosaic.Tests` — cover the started/failed/success signalling from the artwork service.
