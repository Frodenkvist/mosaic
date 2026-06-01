## Context

`game-library-mvp` delivered the Job Object based tracker (`Services/JobObjectTracker.cs`, `Services/PlayTracker.cs`). It is solid for the common cases, but it assigns the launched process to the Job Object *after* `Process.Start` returns. The current sequence is:

1. `PlayTracker.LaunchAsync` calls `StartProcess` → `Process.Start(psi)` (the process is already running when this returns).
2. It persists an open session, fires `SessionStarted`, then spins up `TrackToCompletionAsync` on a background `Task.Run`.
3. Only inside that background task is the `JobObjectTracker` created and `TryAssign(tracker, process)` called.

Between step 1 and the assignment in step 3 the process is live — and there is a DB write *and* a thread hop in between. If the launched executable is a thin launcher that spawns the real game and exits in that window, the real game is never adopted into the job, so `WaitForTreeExitAsync` fires as soon as the launcher exits and the session is measured as a few seconds instead of the real play time. This is the one known correctness gap explicitly deferred from the MVP (recorded under "Resolved After MVP" in the MVP design).

The per-game **real-executable override** (name-based polling) already mitigates many of these titles, but it requires manual configuration per game and is a fallback, not a fix for the default path.

## Goals / Non-Goals

**Goals:**
- Close the assign-after-start race: every descendant of the launched process is in the job before the process executes a single instruction.
- Keep the default launch path (no override configured) reliable for launcher-spawns-game titles without any per-game configuration.
- No user-visible behavioral change for games that already tracked correctly; identical launch arguments / working directory semantics.
- Preserve the existing real-executable override and the graceful failure paths (assignment failure, missing executable).

**Non-Goals:**
- Changing the wait/completion mechanism (`JOBOBJECT_MSG_ACTIVE_PROCESS_ZERO` via the completion port stays exactly as is).
- Tracking games launched entirely outside Mosaic (a full background watcher) — still out of scope.
- Any schema, settings, or UI change.
- Cross-platform anything (Windows-only by construction).

## Decisions

### 1. Start the process suspended, assign to the job, then resume
Replace `Process.Start` in the tracked-launch path with a Win32 `CreateProcessW` call that passes `CREATE_SUSPENDED`. The OS creates the process and its initial thread but does not run it. We then create the `JobObjectTracker`, `AssignProcessToJobObject`, and finally `ResumeThread` on the primary thread. Because assignment happens while the process is suspended, the job is guaranteed to contain the process — and therefore every descendant it later spawns — from the first instruction. The race is structurally eliminated, not merely narrowed.

- **Why `CreateProcessW` and not `ProcessStartInfo`/`Process.Start`**: .NET's managed `Process.Start` has no "create suspended" option; only the raw Win32 API exposes `CREATE_SUSPENDED` and a handle to the primary thread to resume.
- **Access rights**: `CreateProcessW` returns full-access handles in `PROCESS_INFORMATION` (`hProcess`, `hThread`), which satisfy the `PROCESS_SET_QUOTA | PROCESS_TERMINATE` rights `AssignProcessToJobObject` requires and the `THREAD_SUSPEND_RESUME` right `ResumeThread` requires. No extra `OpenProcess`/`OpenThread` is needed.
- **Alternatives considered**:
  - *Keep `Process.Start`, just assign sooner (synchronously, before the `Task.Run`)*: shrinks the window but does not close it — the process is still running between `Process.Start` returning and the assign call. Rejected; the whole point is to remove the race, not make it smaller.
  - *`Process.Start` with `CreateNoWindow`/shell tricks*: irrelevant to the race.
  - *Detect the spawned child by name and re-assign*: that is exactly the real-executable override, which we keep as the fallback, not the primary mechanism.

### 2. Sequence the launch so assignment precedes resume, synchronously, before tracking moves to the background
The `JobObjectTracker` must exist and own the process *before* `ResumeThread`. So the create→assign→resume sequence happens synchronously in the launch path; only `WaitForTreeExitAsync` (the blocking wait) moves to the background as today. New ordering in `LaunchAsync`:

1. Load the game; if the executable no longer exists, return `false` and record nothing (unchanged).
2. `CreateProcessW(..., CREATE_SUSPENDED, ...)`. On failure, return `false` and record nothing (this now also covers the "executable cannot be started" case).
3. Create the `JobObjectTracker`; `Assign` the suspended process. If assignment fails, fall back as today (the session is still tracked via the real-exe poller and/or the process's own lifetime) rather than aborting.
4. `ResumeThread` on the primary thread. Close the thread handle immediately afterward (no longer needed); keep the process handle for the wait.
5. Persist the open session, set `_runningSince`, fire `SessionStarted` (unchanged).
6. Hand the already-created tracker + process to `TrackToCompletionAsync` on `Task.Run`, which awaits `WaitForTreeExitAsync` / runs the real-exe poller and closes the session (unchanged), and is responsible for disposing the tracker in its `finally`.

This moves `JobObjectTracker` creation and assignment out of `TrackToCompletionAsync` and into the synchronous launch path; the background task no longer creates or assigns, only waits and closes.

### 3. Isolate the new interop in `JobObjectTracker` (or a small sibling), behind a managed surface
All `CreateProcessW` / `STARTUPINFO` / `PROCESS_INFORMATION` / `ResumeThread` P/Invoke, command-line construction, and handle lifetime management live in one place (the existing interop class is the natural home, matching the MVP decision to isolate all Job Object P/Invoke in `JobObjectTracker` with `SafeHandle`s). The managed entry point exposes something like "launch suspended → return a handle/`Process`, assign, resume" so `PlayTracker` stays free of raw interop. The primary-thread handle is closed right after resume; the process handle is wrapped (e.g. via `Process.GetProcessById` or a `SafeProcessHandle`) so the existing wait/`Process` usage in `TrackToCompletionAsync` is unchanged.

- **Command line**: build `lpCommandLine` as a single mutable buffer (`CreateProcessW` may write to it). Quote the executable path (`"\"<exe>\" <args>"`); pass the executable also as `lpApplicationName` so a quoted path is unambiguous. Use the same working-directory resolution as today (`game.WorkingDirectory` or the exe's directory) for `lpCurrentDirectory`. Inherit the parent environment (pass `NULL`), matching current `UseShellExecute = false` behavior.
- **Handle hygiene**: `hThread` closed after `ResumeThread`; `hProcess` owned for the lifetime of the wait and released on session close — no leaks across the launch path, consistent with the `SafeHandle` discipline already in `JobObjectTracker`.

## Risks / Trade-offs

- **More involved P/Invoke (quoting, working dir, environment, handle lifetimes)** → a quoting or marshalling bug could fail to launch a game or launch it wrong. *Mitigation*: isolate in one class behind a managed surface; cover with the existing stub-launcher integration test plus a new test asserting a descendant is still tracked when the parent exits within milliseconds; manual smoke against a couple of real launcher-style games.
- **Loss of `Process.Start` conveniences** (shell-verb launching, `.lnk`/non-exe targets) → games are real executables started with `UseShellExecute = false` already, so no shell features are in use; acceptable.
- **`AssignProcessToJobObject` could still fail** (e.g. process already in an incompatible job under some environments) → keep the existing `TryAssign` fallback so the session is never aborted; the real-exe poller / process lifetime still bounds it.
- **Resuming before vs. after persisting the open session** → resume happens before the DB write so the game starts promptly, but the session row is written immediately after (and reconciliation already discards anything left open by a crash), so a crash in the sub-millisecond gap is handled by the existing conservative reconcile policy.
- **No data/schema/settings impact** → nothing to migrate; risk is confined to the launch code path.

## Migration Plan

No data or schema migration. This is an internal change to how the tracked process is started. No settings change; existing per-game real-executable overrides keep working unchanged. The change is shipped behind the existing `IPlayTracker` interface, so callers (view models) are untouched.

## Open Questions

- Whether to expose the suspended-launch helper as a method on `JobObjectTracker` or a small dedicated `SuspendedProcessLauncher` type. *Leaning*: a method/factory on the interop class to keep all process/job P/Invoke together; final boundary decided in implementation.
- Whether to wrap the raw process handle in a `SafeProcessHandle` + `Process.GetProcessById` vs. attaching to the existing `Process` abstraction for the wait. *Leaning*: reuse a `Process` instance so `TrackToCompletionAsync` is unchanged; confirm `WaitForTreeExitAsync` (job-based) is unaffected by how the handle is obtained.
