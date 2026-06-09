## Context

Achievement unlocks are detected by `AchievementService` while a play session is active (it watches the game's Steam-emulator files) and surfaced today as `AchievementUnlocked` events (`GameId`, `GameName`, `AchievementName`, `IconPath`). `MainViewModel` listens to that event and shows an in-app toast inside `MainWindow` (`MainWindow.xaml`), marshalling from the background thread via `App.RunOnUiAsync`. Play sessions are owned by `PlayTracker`, which raises `SessionStarted(gameId)` from `LaunchAsync` and `SessionEnded(gameId)` when the tracked process tree exits.

The problem: during play, Mosaic is in the background, so the player never sees the in-app toast. We want the unlock celebration to reach the player on top of the running game, with a sound.

Constraints that shape the design:

- **Windows-only, WPF** — consistent with the app's existing Job Object / WPF dependence; Win32 interop is acceptable and already used (`JobObjectTracker`, `SuspendedProcessLauncher`, `MosaicWindow`).
- **Foreign-process windows.** Mosaic does not own the game's render surface. A real cross-the-board overlay (drawing *inside* the game's frame, like Steam/Discord) requires injecting into the game's DirectX/OpenGL/Vulkan pipeline — large, fragile, and a frequent anti-cheat trigger. That is out of scope.
- **Short-lived `DbContext` / singleton-service** conventions; background-thread events marshalled to the UI thread via `App.RunOnUiAsync`.

## Goals / Non-Goals

**Goals:**

- Show achievement-unlock toasts *over the running game* for games launched through Mosaic, without stealing focus or input.
- Be completely invisible when there is nothing to show — the player should not know the overlay exists.
- Play a short, pleasant sound when an achievement unlocks.
- Make the overlay and the sound independently toggleable, defaulting on.
- Keep the change additive and low-risk: no schema changes, no change to detection/persistence, and the existing in-app toast stays as a fallback surface.

**Non-Goals:**

- Drawing inside **exclusive-fullscreen** games (no DirectX/Vulkan hooking). Those fall back to sound + the in-app toast.
- A general-purpose overlay framework (FPS counters, friends list, settings panel, screenshot capture). This overlay shows achievement toasts only.
- Per-game overlay configuration or repositioning UI. Global on/off only for v1.
- Tracking and exactly mirroring the game window's bounds/position frame-by-frame. The toast appears in a fixed screen corner.

## Decisions

### Decision 1: A small transparent, click-through, topmost window — not full-screen, not injection

The overlay is a borderless WPF `Window` sized only large enough for the toast, positioned in a corner of the game's monitor. Idle, it shows nothing (the toast content is collapsed). Properties: `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `ShowInTaskbar=False`, `ShowActivated=False`, `Topmost=True`, `ResizeMode=NoResize`. On `SourceInitialized`, P/Invoke sets extended styles on the HWND:

- `WS_EX_LAYERED` — per-pixel alpha (WPF's `AllowsTransparency` already implies this; set defensively).
- `WS_EX_TRANSPARENT` — click-through: pointer input falls through to the game.
- `WS_EX_NOACTIVATE` — the window never becomes foreground / never takes focus.
- `WS_EX_TOOLWINDOW` — keep it out of the Alt-Tab switcher and taskbar.

All P/Invoke (`GetWindowLong`/`SetWindowLong`, `SetWindowPos`, monitor metrics) is isolated to this window's code-behind, mirroring how `JobObjectTracker` quarantines its interop.

- **Why a small corner window over a full-screen transparent window?** A full-screen topmost layered window is more likely to perturb a game's fullscreen state (DWM composition flips, flicker) and is a larger always-composited surface. A small corner window is lower-risk and equally invisible when idle.
- **Why not DirectX/overlay injection?** Out of scope (see Non-Goals): high complexity, fragile across graphics APIs, and a common anti-cheat trigger. A passive, non-injecting, click-through window is benign by comparison.
- **Why click-through + no-activate is essential.** The overlay must never intercept a click or pull focus mid-game; without these styles a topmost window would do exactly that.

### Decision 2: A dedicated `AchievementOverlayService` singleton owns the overlay lifecycle

A new singleton subscribes to `IPlayTracker.SessionStarted`/`SessionEnded` and `IAchievementService.AchievementUnlocked`. It is instantiated at startup in `App.OnStartup` the same way `SystemMediaWatchObserver` is (resolve it so its constructor wires the subscriptions). On `SessionStarted` (when the overlay setting is on) it creates/shows the overlay window on the UI thread; on `SessionEnded` it closes it. On `AchievementUnlocked` it (a) plays the sound if enabled and (b) shows the toast on the active overlay. All handlers marshal via `App.RunOnUiAsync`, because the tracker and achievement events fire on background threads.

- **Why a service, not `MainViewModel`?** The overlay is a separate top-level window with its own lifetime keyed to sessions, not to the main window or its view model. Keeping it in a focused service mirrors the existing `SystemMediaWatchObserver` observer pattern and keeps `MainViewModel` unchanged.
- **Concurrent sessions:** keep a single overlay shared across running sessions, ref-counted by active session count (created when the count goes 0→1, closed when it returns to 0). Toasts render on the one overlay regardless of which game unlocked. Simultaneous foreground games are rare; a single corner toast is sufficient.

### Decision 3: Sound via a bundled WAV `<Resource>` played with `System.Media.SoundPlayer`

Ship a short royalty-free chime as `Assets\achievement.wav`, added as a `<Resource>` in `Mosaic.csproj`. Load it with `Application.GetResourceStream(packUri).Stream` into a `System.Media.SoundPlayer` and call `Play()` (asynchronous, fire-and-forget).

- **Why `SoundPlayer` over `MediaPlayer`?** `SoundPlayer` plays a short WAV from a `Stream` with no file-on-disk requirement and no `pack://` URI quirks; `MediaPlayer` is heavier and its URI handling for embedded resources is awkward. WAV keeps the dependency surface zero.
- **Why bundle, not a system sound?** A curated "happy achievement" chime is the explicit ask; `SystemSounds` are not celebratory.
- **The sound is played by the service, gated only on `AchievementSoundEnabled` — independent of whether the visual overlay window rendered.** This is what makes the exclusive-fullscreen case still audible (Decision 5 / the spec's "never silent" requirement).

### Decision 4: Keep the in-app toast; the overlay is additive

`MainViewModel`'s existing in-app toast in `MainWindow` is left exactly as-is. The overlay is a second, in-game surface. Because the two live on different windows (the game vs. Mosaic), the player never sees both at once, so there is no visible duplication — but the in-app toast remains as the surface when the user is looking at Mosaic and as the guaranteed-recorded fallback when the overlay can't draw (exclusive fullscreen).

- **Alternative considered:** suppress the in-app toast while an overlay is active. Rejected — it adds cross-component coupling and timing logic for no user-visible benefit, and would remove the exclusive-fullscreen fallback.

### Decision 5: Positioning and topmost maintenance

Place the toast in a fixed corner (default: top-center) of the monitor that currently hosts the foreground window at show time (`GetForegroundWindow` → `MonitorFromWindow` → `GetMonitorInfo`), accounting for DPI. If that can't be resolved, fall back to the primary monitor. Re-assert `HWND_TOPMOST` via `SetWindowPos(... SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE)` each time a toast is shown, so the overlay rises above the game (and any other topmost windows) for the toast's duration.

- **Why re-assert on show rather than poll continuously?** A toast is the only moment visibility matters; a per-second z-order poll is unnecessary churn. Re-asserting at show time is enough.

### Settings shape

Add to `AppSettings`: `bool GameOverlayEnabled = true` and `bool AchievementSoundEnabled = true` (both default-true so an older `settings.json` deserializes to enabled, matching the `AutomaticUpdatesEnabled` precedent). Surface two toggles in `SettingsView`/`SettingsViewModel` mirroring the existing automatic-updates toggle. The service reads current values from `ISettingsService` at session start (overlay) and per unlock (sound), so a settings change takes effect on the next session / next unlock without a restart.

## Risks / Trade-offs

- **Exclusive-fullscreen games show no visual overlay.** → Mitigation: the sound still plays and the in-app toast is still recorded (spec: "Unlock is never silent when the overlay cannot draw"). Documented as a known limitation, not a regression — the prior behavior (in-app toast only) is preserved.
- **Topmost contention with the game or other overlays (Steam, Discord, RTSS).** → Mitigation: re-assert `HWND_TOPMOST` on each toast show; accept that another aggressive topmost overlay could still occasionally win for a few seconds. Low impact (the toast auto-dismisses anyway).
- **Anti-cheat false positives.** → Mitigation: the overlay performs **no injection, no hooking, no reading of game memory** — it is a passive, click-through, non-activating window. This is materially safer than render-pipeline overlays. Residual risk is low but nonzero for the most aggressive kernel anti-cheat; the global off switch is the escape hatch.
- **Focus/input theft if a style is missed.** → Mitigation: `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` + `ShowActivated=False` + `Show()` (never `ShowDialog()`); covered by the click-through and no-focus scenarios.
- **Multi-monitor / DPI placement errors.** → Mitigation: resolve the foreground window's monitor and its DPI; fall back to primary monitor on failure. Worst case the toast lands on the wrong monitor — cosmetic.
- **Window/thread leaks across many sessions.** → Mitigation: the service closes and nulls the overlay on `SessionEnded`; ref-counting prevents an early close while another session is still running.
- **Sound asset licensing.** → The shipped WAV must be royalty-free / appropriately licensed and committed to the repo; this is a content task, not code.

## Migration Plan

No data migration. Steps: (1) add the two `AppSettings` fields (default true); (2) add the `Assets\achievement.wav` resource + `Mosaic.csproj` entry; (3) add `AchievementOverlayWindow` and `AchievementOverlayService`; (4) register the service and resolve it in `OnStartup`; (5) add the Settings toggles. Rollback is trivial: disabling both toggles makes the feature inert; removing the startup resolution + registration removes it entirely with no residual state beyond two ignored settings fields.

## Open Questions

- **Sound asset:** which specific royalty-free chime ships as `Assets\achievement.wav`? (A short ~1s WAV must be supplied/approved; the code path is asset-agnostic.)
- **Toast corner:** default top-center vs. top-right — minor, easily changed; assuming top-center for parity with console achievement banners unless the user prefers otherwise.
- **Volume control:** out of scope for v1 (on/off only). Revisit if requested.
