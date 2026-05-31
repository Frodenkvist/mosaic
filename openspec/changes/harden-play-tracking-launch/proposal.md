## Why

Play-time tracking assigns the launched process to a Windows Job Object *after* `Process.Start` returns. There is a brief race: if a launcher spawns the real game and exits before the assignment completes, those early descendants may not be captured by the job, so the session can end too early or be mismeasured. This is the one known correctness gap in the otherwise-solid tracker delivered by `game-library-mvp` (it was explicitly deferred there).

## What Changes

- Start the game **suspended** via the Win32 `CreateProcess` API with `CREATE_SUSPENDED`, **assign the process to the Job Object while it is still suspended**, then resume the main thread. This guarantees every descendant is in the job from the very first instruction — closing the race.
- Keep the existing per-game **"real executable" override** (name-based polling) as the fallback for games launched through a separate, already-running store/launcher process that Mosaic does not own.
- Preserve current behavior for the common case (no user-visible change), but make whole-tree tracking reliable for launcher-spawns-game titles.

## Capabilities

### New Capabilities
<!-- None — this refines existing play-tracking behavior. -->

### Modified Capabilities
- `play-tracking`: The requirement that play time spans the full process tree is strengthened — tracking MUST be reliable even when a launcher spawns the game and exits immediately, by assigning the suspended process to the job before it runs.

## Impact

- **Code**: `Services/JobObjectTracker.cs` / `Services/PlayTracker.cs` — replace `Process.Start` + post-hoc `AssignProcessToJobObject` with suspended `CreateProcess` interop (`CreateProcessW`, `STARTUPINFO`, `PROCESS_INFORMATION`, `ResumeThread`), assigning to the job between create and resume.
- **Risk**: more involved P/Invoke (process/thread handle lifetimes, argument/quoting, working directory, environment). Mitigate by isolating it behind the existing `IPlayTracker` surface and covering the launcher-exits-early case with the existing stub-launcher integration test plus a new test that asserts the descendant is tracked even when the parent exits within milliseconds.
- **No data/schema changes.** Windows-only (already a constraint).
