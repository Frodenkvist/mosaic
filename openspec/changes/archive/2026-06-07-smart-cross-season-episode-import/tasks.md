## 1. Normalization helper (pure, testable)

- [x] 1.1 Add a pure helper that takes a collection of episode entries keyed by a stable id (each carrying a season number and a raw episode number) and returns the corrected within-season episode number per id, implementing the continuity-anchored algorithm from `design.md` (walk seasons ascending; renumber a season only when its min raw number equals the previous season's running absolute max + 1). Place it in `MediaNameParser` or a small sibling (e.g. `EpisodeNumbering`).
- [x] 1.2 Skip entries with a null season number or null episode number (leave them unchanged); make the helper idempotent (already-restarting seasons yield no changes).
- [x] 1.3 Add unit tests covering: absolute 3-season case (`1..8 / 9..16 / 17..25` → each season restarts at 1), pure within-season (unchanged), already-normalized re-run (idempotent), a within-season gap in an earlier season (continuation still detected via prev-season max), and the no-prior-season case (later season left as-is).

## 2. Apply correction at add time (source of truth)

- [x] 2.1 In `MediaLibrary.AddConfirmedAsync`, load the created-or-reused series with its existing episodes tracked (e.g. `.Include(m => m.Episodes)`) so previously-stored episode numbers can be mutated and persisted.
- [x] 2.2 After attaching the newly confirmed episodes, run the helper over the series' full episode set and write back each corrected `EpisodeNumber` before `SaveChangesAsync`.
- [x] 2.3 When an episode's number changes, regenerate its title only if it still matches the auto-generated `SxxExx` placeholder; preserve any metadata/user-supplied title.
- [x] 2.4 Confirm the existing `await db.SaveChangesAsync()` persists both new and modified existing rows (single tracked context) and that artwork fetch still runs only for non-episode items.

## 3. Reflect correction in the confirmation preview

- [x] 3.1 In `MediaLibrary.ScanFoldersAsync`, after building episode candidates, apply the helper using the scanned candidates plus the existing DB `(series, season, episode)` pairs for those series, so previewed `SeasonNumber`/`EpisodeNumber` (and the `S{season}E{episode}` candidate title) reflect the inferred within-season numbers.
- [x] 3.2 Keep this best-effort: scan-time preview must not change add-time being authoritative; ensure no exception path here aborts the scan.

## 4. Tests (behavioral)

- [x] 4.1 `MediaLibraryTests`: import Death-Note-style files (`Season 1\1..8`, `Season 2\09..`) in one batch → assert season 2's `09` is stored as season 2, episode 1; season 1 unchanged.
- [x] 4.2 `MediaLibraryTests`: import season 1, then import season 2 in a separate `AddConfirmedAsync` pass → assert the later season is renumbered using the already-stored season 1.
- [x] 4.3 `MediaLibraryTests`: import the later season first (stored as absolute), then the earlier season → assert the previously-stored later-season rows are corrected to restart at 1.
- [x] 4.4 `MediaLibraryTests`: a normal per-season-restarting series is imported and re-imported → assert numbers are unchanged (no false correction, idempotent).
- [x] 4.5 `MediaLibraryTests`: assert a renumbered episode's placeholder title is regenerated (`S02E01`) while an episode whose title was pre-set to a real name keeps it.

## 5. Verify

- [x] 5.1 `dotnet test` green (new + existing media tests).
- [x] 5.2 `dotnet build Mosaic.sln` clean.
- [ ] 5.3 Manual check in the running app with a TMDB key: an absolute-numbered series' later-season episodes now receive their real TMDB titles/stills (the previously failing match now succeeds).

## 6. Follow-up: prefer a leading episode number (parse fix)

- [x] 6.1 In `MediaNameParser.ParseEpisodeNumber`, prefer a leading `<episode>` number (file name starts with a 1-3 digit number + separator) over the bare trailing-number fallback, so titles containing numbers (e.g. `36 1.28 (January 28).mkv`) don't mis-parse to a number inside the title.
- [x] 6.2 `MediaNameParserTests`: leading number wins over a number in the title (→ 36); leading number with a simple title (`1 Rebirth` → 1). Full suite green.

## 7. Per-episode edit/remove UI (media-ui)

- [x] 7.1 `EpisodeRowViewModel`: inline-edit state (`IsEditing`, `EditTitle`, `EditSeason`, `EditEpisode`) + `BeginEdit`/`CancelEdit`.
- [x] 7.2 `MediaDetailViewModel`: `BeginEditEpisode`/`CancelEditEpisode`/`SaveEpisode` (validates whole, ≥0 numbers, persists via `UpdateMediaItemAsync`, reloads) and `RemoveEpisode` (confirm → `RemoveAsync` → reload).
- [x] 7.3 `MediaDetailWindow.xaml`: per-episode ✎ Edit and 🗑 Remove buttons + inline editor (Season/Episode/Title + Save/Cancel).
- [x] 7.4 `MediaDetailViewModelTests`: SaveEpisode persists corrected season/episode/title; invalid number shows a message and does not persist; RemoveEpisode removes only the confirmed episode; declining the confirm keeps it.
