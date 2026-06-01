## 1. Suspended-launch interop

- [x] 1.1 Add P/Invoke for `CreateProcessW`, `STARTUPINFO`, `PROCESS_INFORMATION`, and `ResumeThread` (with `CREATE_SUSPENDED`), isolated in the Job Object interop layer (`Services/JobObjectTracker.cs` or a small sibling helper), using `SafeHandle`s consistent with the existing class
- [x] 1.2 Build `lpCommandLine` correctly: quote the executable path, pass it also as `lpApplicationName`, append the game's launch arguments, and use a mutable buffer (`CreateProcessW` may modify it)
- [x] 1.3 Resolve the working directory exactly as today (`game.WorkingDirectory` or the executable's directory) for `lpCurrentDirectory`; inherit the parent environment
- [x] 1.4 Expose a managed entry point that starts the process suspended and returns a process handle/`Process` plus the primary-thread handle (raw interop not visible to `PlayTracker`)

## 2. Wire the assign-before-resume sequence into the launch path

- [x] 2.1 In `PlayTracker.LaunchAsync`, replace `StartProcess` (`Process.Start`) with the suspended launch; on `CreateProcessW` failure, return `false` and record nothing (covers a non-startable executable)
- [x] 2.2 Create the `JobObjectTracker` and `Assign` the suspended process **before** resuming; move tracker creation/assignment out of `TrackToCompletionAsync` and into the synchronous launch path
- [x] 2.3 `ResumeThread` on the primary thread, then close the thread handle immediately; keep the process handle for the wait
- [x] 2.4 Persist the open session, set `_runningSince`, and fire `SessionStarted` after resume (preserve current ordering and crash-safety)
- [x] 2.5 Hand the already-created tracker + process to `TrackToCompletionAsync`; have the background task only wait (`WaitForTreeExitAsync` / real-exe poller), close the session, and dispose the tracker in its `finally`

## 3. Preserve existing fallbacks and error paths

- [x] 3.1 Keep the `TryAssign` fallback so a failed `AssignProcessToJobObject` does not abort the session (still bounded by the real-exe poller / process lifetime)
- [x] 3.2 Keep the per-game real-executable override path working unchanged
- [x] 3.3 Confirm the missing-executable check still returns `false` and records no session

## 4. Tests

- [x] 4.1 Add a test that a descendant is tracked even when the parent process exits within milliseconds of launch (the race the change closes) — assert the session spans the descendant's lifetime, not the parent's
- [x] 4.2 Verify the existing stub-launcher integration test (`JobObjectTrackerTests` / `PlayTrackerTests`) still passes through the new suspended-launch path
- [x] 4.3 Add/adjust a test asserting launch arguments and working directory are honored through `CreateProcessW`
- [x] 4.4 Confirm graceful behavior when the executable cannot be started (`LaunchAsync` returns `false`, no session row)

## 5. Validation

- [x] 5.1 `dotnet build Mosaic.sln` and `dotnet test` are green
- [ ] 5.2 Manual smoke against a launcher-style game (launcher spawns game and exits) confirms full play time is recorded without a real-executable override
- [x] 5.3 `openspec validate harden-play-tracking-launch --strict` passes
