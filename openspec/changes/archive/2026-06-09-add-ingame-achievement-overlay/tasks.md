## 1. Settings and sound asset

- [x] 1.1 Add `bool GameOverlayEnabled { get; set; } = true;` and `bool AchievementSoundEnabled { get; set; } = true;` to `Services\AppSettings.cs` (default-true so older `settings.json` deserializes to enabled, mirroring `AutomaticUpdatesEnabled`).
- [x] 1.2 Add a short royalty-free WAV at `Assets\achievement.wav` (≈1s "achievement unlocked" chime) and register it as a `<Resource>` in `Mosaic.csproj`. (Shipped as a synthesized C-major arpeggio placeholder — see design Open Questions; can be swapped for a curated clip without code changes.)

## 2. Overlay window (view + interop)

- [x] 2.1 Create `Views\AchievementOverlayWindow.xaml` deriving from a plain WPF `Window`: `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `ShowInTaskbar=False`, `ShowActivated=False`, `Topmost=True`, `ResizeMode=NoResize`, sized to the toast only.
- [x] 2.2 Lay out the toast content reusing the existing in-app toast visual (icon + "🏆 Achievement unlocked" + achievement name + "Unlocked in {game}"), collapsed/empty when idle so nothing is visible.
- [x] 2.3 In `AchievementOverlayWindow.xaml.cs`, on `SourceInitialized`, set extended window styles on the HWND via P/Invoke: `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` (isolate all P/Invoke here, mirroring `JobObjectTracker`).
- [x] 2.4 Add a `ShowToast(title, subtitle, iconPath)` method that fills and reveals the toast, animates it in, and starts/restarts a short auto-dismiss timer that hides the content (so a newer unlock supersedes and restarts the interval).
- [x] 2.5 Position the window in a corner (default top-center) of the monitor hosting the foreground window at show time (`GetForegroundWindow` → `MonitorFromWindow` → `GetMonitorInfo`, DPI-aware); fall back to the primary monitor. Re-assert `HWND_TOPMOST` via `SetWindowPos(SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE)` on each `ShowToast`.

## 3. Overlay service

- [x] 3.1 Create `Services\AchievementOverlayService.cs` (+ minimal interface if useful) as a singleton; constructor-inject `IPlayTracker`, `IAchievementService`, and `ISettingsService`.
- [x] 3.2 Subscribe to `IPlayTracker.SessionStarted`/`SessionEnded`; maintain an active-session ref count; create/show the overlay (on the UI thread via `App.RunOnUiAsync`) when the count goes 0→1 and `GameOverlayEnabled` is set, and close + null it when it returns to 0. Guard so overlay-creation failure never throws into the tracker.
- [x] 3.3 Subscribe to `IAchievementService.AchievementUnlocked`; on the UI thread, play the sound via `System.Media.SoundPlayer` (loaded from the bundled resource stream) when `AchievementSoundEnabled`, and call `ShowToast(...)` on the active overlay when one exists.
- [x] 3.4 Read settings live (`GameOverlayEnabled` at session start, `AchievementSoundEnabled` per unlock) so changes apply without a restart; ensure the sound plays even when no visual overlay exists (exclusive-fullscreen path).

## 4. Composition / wiring

- [x] 4.1 Register `AchievementOverlayService` as a singleton in `App.ConfigureServices`.
- [x] 4.2 Resolve it in `App.OnStartup` (as `SystemMediaWatchObserver` is) so its event subscriptions are wired at startup.

## 5. Settings UI

- [x] 5.1 Add `GameOverlayEnabled` and `AchievementSoundEnabled` observable properties to `ViewModels\SettingsViewModel.cs`, loaded from and saved to settings, mirroring the `AutomaticUpdatesEnabled` toggle.
- [x] 5.2 Add two toggles to `Views\SettingsView.xaml`: "Show in-game achievement overlay" and "Play achievement sound", bound to the new properties.

## 6. Tests

- [x] 6.1 Add `AchievementOverlayService` tests: overlay created on `SessionStarted` (when enabled) and torn down on `SessionEnded`; ref-count keeps it alive across concurrent sessions; not created when `GameOverlayEnabled` is false.
- [x] 6.2 Test that an `AchievementUnlocked` event triggers a toast on the active overlay and that the sound is gated on `AchievementSoundEnabled` (use a seam/abstraction over the visual window and the sound player so the service is testable headlessly, given xUnit cannot show WPF windows).
- [x] 6.3 Test the "never silent" path: with no overlay window available, an unlock still triggers the sound (when enabled).

## 7. Validation

- [x] 7.1 `dotnet build Mosaic.sln` and `dotnet test` pass. (Build: 0 warnings/0 errors; tests: 146 passed.)
- [x] 7.2 Manual verification: launch a windowed/borderless game through Mosaic, confirm the overlay is invisible when idle, does not steal focus or block input, shows the toast in-game with sound on unlock, and disappears on exit; toggle both settings off and confirm each is suppressed. (Confirmed working by the user.)
- [x] 7.3 Run `openspec validate add-ingame-achievement-overlay --strict` and resolve any issues. (Valid.)
