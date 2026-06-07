## Context

Mosaic parses episodes heuristically in `MediaNameParser.TryParseEpisode` and stores each as a `MediaItem` with `SeasonNumber` + `EpisodeNumber`, where `EpisodeNumber` is documented as **within-season** (restarts at 1 each season). `MediaArtworkService.FillEpisodesAsync` enriches episodes by grouping them by season and matching each against TMDB's per-season episode list **by exact within-season episode number** (`tmdbEpisodes.TryGetValue(n, …)`).

The parser, however, reads whatever number the filename carries. For absolute-numbered libraries (common for anime, and for users who split a long series into `Season N` folders while keeping a single running count), the number in a later season is the *series-wide* index, not the within-season index. So `Death Note\Season 2\09 Encounter.mkv` parses to season 2, episode 9, but TMDB's season 2 episode 1 is what it should match. The lookup misses, the episode keeps its placeholder title and gets no still, and it sorts after the season-1 episodes incorrectly.

Constraints from the codebase (see `CLAUDE.md`):
- Short-lived `DbContext` per operation; `AddConfirmedAsync` already uses a **tracked** context and attaches new episodes to a tracked `series`.
- Import is confirmation-gated: `ScanFoldersAsync` → `ScanResultsWindow` (user confirms) → `AddConfirmedAsync`.
- `MediaItem.EpisodeNumber` already means within-season; no schema change is needed. There is no stored absolute-number field, and we do not want to add one (YAGNI — the absolute number is recoverable from the filename).

## Goals / Non-Goals

**Goals:**
- Infer absolute cross-season numbering at import and store corrected **within-season** episode numbers, so TMDB matching, ordering, and display are all correct.
- Work fully offline (no dependency on a TMDB key or network) using only folder/season structure already parsed.
- Correct across **separate import passes** and **out-of-order** imports (season 2 imported before season 1).
- Be conservative: never renumber a series that already restarts per season; never overwrite a real (metadata/user) episode title.

**Non-Goals:**
- No automatic backfill/migration of the whole existing library. A series is re-evaluated only when an import pass touches it.
- No change to `MediaArtworkService` / TMDB matching logic. We feed it correct numbers rather than teach it to reinterpret absolute numbers.
- No new persisted "absolute episode number" field on `MediaItem`.
- No attempt to reconstruct numbering when a season is partially imported with internal gaps *and* the boundary signal is broken (see Risks). Conservative miss is preferred over wrong correction.

## Decisions

### Decision 1: Normalize at add-time over the full series, as the source of truth

The correction runs in `AddConfirmedAsync`, after episodes are grouped under their series, operating over the **union of existing (already-stored) and newly confirmed** episodes of that series. This is the only point that sees the authoritative, merged set and can persist corrections to both new rows and previously-stored rows.

Implementation shape:
- Load the reused series with its episodes tracked (`.Include(m => m.Episodes)`), or load existing episodes into the tracked context, so mutating `EpisodeNumber` is persisted by the existing `SaveChangesAsync`.
- After attaching the new episodes, run the normalization helper over `series.Episodes`, then save.

*Alternative considered — normalize at parse time (`TryParseEpisode`):* rejected. The parser is per-file and I/O-free; it cannot see sibling seasons or already-stored episodes, which is exactly the context cross-season inference needs.

*Alternative considered — TMDB-informed correction in `FillEpisodesAsync`:* using TMDB's authoritative per-season episode counts, an episode whose number exceeds the season's count could be reinterpreted as absolute and mapped down. More robust to gaps, but it requires a key/network, only fixes metadata (not the stored number or offline ordering), and couples a data-correctness concern to the artwork service. Rejected as the primary mechanism; could be layered later as a fallback (Open Questions).

### Decision 2: Continuity-anchored detection (previous season's max), not a fixed threshold

A later season is treated as absolute-numbered only when its **minimum** episode number continues directly from the running absolute maximum of the prior seasons. Algorithm, walking a series' seasons in ascending `SeasonNumber` (ignoring episodes with a null season or episode number):

```
prevAbsoluteMax = 0                      // largest absolute episode index seen in earlier seasons
for each season in ascending order:
    minRaw = min(EpisodeNumber in season)
    maxRaw = max(EpisodeNumber in season)
    if prevAbsoluteMax > 0 and minRaw == prevAbsoluteMax + 1:
        offset = prevAbsoluteMax         // absolute-numbered: this season continues the count
        prevAbsoluteMax = maxRaw         // its raw numbers ARE absolute indices
    else:
        offset = 0                       // already within-season (restarts), or the base season
        prevAbsoluteMax = prevAbsoluteMax + maxRaw
    for each episode in season:
        newEpisodeNumber = EpisodeNumber - offset
```

Worked examples:
- Absolute `S1:1..8, S2:9..16, S3:17..25` → `S2 -=8 → 1..8`, `S3 -=16 → 1..9`. ✓ (the user's Death Note case: `S2/09 → S2E1`).
- Within-season `S1:1..10, S2:1..10` → `S2 min=1, prevMax=10, 1≠11` → offset 0, unchanged. ✓
- Already-normalized re-pass `S1:1..8, S2:1..8` → unchanged (idempotent). ✓

Why min-continues-prev-max rather than "min > 1" or a cumulative **count**: equality with the prior absolute boundary is a strong, low-false-positive signal, and anchoring on the prior season's *max* (not its episode *count*) tolerates within-season gaps in earlier seasons. A bare "min > 1" would wrongly renumber a legitimately-split season (e.g. a "part 2" that genuinely starts at 13). Idempotency falls out for free: once corrected, seasons restart at 1, so `minRaw == 1 ≠ prevAbsoluteMax + 1` and no further change occurs.

### Decision 3: Single pure helper, applied at both add-time and preview-time

The algorithm lives in one pure, deterministic function (in `MediaNameParser`, or a small sibling like `EpisodeNumbering`) that takes a collection of `(season, episode)` entries keyed by a stable id and returns the corrected episode numbers. This keeps it unit-testable in isolation (like the existing parser tests) and lets two call sites share identical logic:

1. **`AddConfirmedAsync`** — authoritative correction over existing + confirmed episodes (Decision 1).
2. **`ScanFoldersAsync`** — apply over the scanned episode candidates merged with the existing DB `(series, season, episode)` pairs, so `ScanResultsWindow` previews the corrected `S{season}E{episode}` the user is about to confirm. The preview is best-effort UX; add-time remains the source of truth if the user confirms only a subset.

### Decision 4: Regenerate only the auto-generated placeholder title

Episodes are created with `Title = "S{season:D2}E{episode:D2}"`. When normalization changes an episode's number, regenerate that placeholder so a not-yet-matched episode displays consistently (`S02E09` → `S02E01`). A title that is **not** the `SxxExx` placeholder (a real TMDB or user-edited title — possible when re-evaluating an already-stored series on a later pass) MUST be left untouched.

## Risks / Trade-offs

- **Incomplete earlier season breaks the boundary signal** → If season 1's highest *imported* episode isn't its true last episode (the user imported a strict subset that stops below the season-2 start − 1), `minRaw == prevAbsoluteMax + 1` won't hold and season 2 is left as absolute. Mitigation: conservative by design — we accept a missed correction (the prior, already-shipped behavior) rather than risk a wrong one; the next pass that fills the gap, or a manual edit, corrects it.
- **False positive on a genuinely continuous within-season show** → Vanishingly unlikely: it would require a real season to start at exactly `prevAbsoluteMax + 1` while every other season restarts at 1. The equality anchor plus per-season scoping makes this effectively impossible for real libraries. Mitigation: only renumber when the continuation equality holds; otherwise leave data exactly as parsed.
- **Overwriting a stored number is lossy** → We replace `EpisodeNumber 9` with `1`. Mitigation: the original is trivially recoverable from the unchanged filename, and the existing "Edit a media item" flow lets the user override; we never touch the video file.
- **Preview vs. stored mismatch when confirming a subset** → If the scan preview infers numbering from the full batch but the user confirms only some episodes, add-time may compute a slightly different offset. Mitigation: add-time merges with already-stored episodes and is authoritative; the preview is advisory only.

## Migration Plan

No DB migration. Ships as behavior in the import path. Existing libraries are corrected lazily: the first import pass that adds an episode to an affected series re-evaluates and persists corrected numbers for that series. No rollback concern beyond reverting the code; corrected numbers are valid within-season values regardless.

## Open Questions

- Should a TMDB-informed fallback be added later in `FillEpisodesAsync` (reinterpret an episode number exceeding the matched season's TMDB episode count as absolute) to catch the incomplete-earlier-season case? Out of scope here; revisit if users report misses where prior seasons are only partially imported.
- Should re-evaluating an already-stored series also offer to backfill series the user never re-imports (a one-time "re-detect numbering" action)? Deferred; not requested.
