## Why

Achievement-unlock toasts currently appear only inside the Mosaic window. While the user is playing, Mosaic is in the background, so the player never actually sees the toast that celebrates the unlock they just earned. The celebration should reach the player where they are — on top of the running game — and be reinforced with a sound, the way console and Steam/Xbox overlays do.

## What Changes

- Add a **transparent, click-through, topmost in-game overlay** that appears over a game **launched through Mosaic** for the lifetime of that play session and is removed when the session ends.
- The overlay is **otherwise invisible**: when there is no achievement to show it renders nothing and does not intercept input, so the player is unaware it exists.
- **Achievement-unlock toasts are presented on the overlay** (same content as today's in-app toast: icon, achievement name, game name), auto-dismissing after a few seconds, so the player sees them without leaving the game.
- The achievement toast **plays a short "achievement unlocked" sound** when it appears.
- Add Settings toggles to enable/disable the **in-game overlay** and the **achievement sound** independently (both default on).
- The existing in-app Mosaic toast is **kept unchanged** — the overlay is purely additive (and serves as the visible surface for the common windowed/borderless case while the in-app toast remains the surface when the user is looking at Mosaic).
- **Known limitation (documented, not a regression):** the overlay can draw over windowed and borderless-windowed games but **not exclusive-fullscreen** ones, which bypass the desktop compositor (drawing on those would require DirectX/Vulkan render-hooking — out of scope and anti-cheat-risky). In the exclusive-fullscreen case the **sound still plays** and the in-app toast is still recorded, so the unlock is never silent.

## Capabilities

### New Capabilities

- `game-overlay`: An in-game presentation surface for games launched through Mosaic — a transparent, click-through, topmost, non-activating window whose lifetime is tied to the play session, which is invisible when idle, renders achievement-unlock toasts over the running game, plays an audio cue on unlock, and is user-configurable.

### Modified Capabilities

<!-- None. The overlay is additive: the existing library-ui "Live achievement-unlock notification" requirement (the in-app toast) is unchanged, and achievement unlocks are still detected and persisted exactly as today. -->

## Impact

- **New code**
  - `Views\AchievementOverlayWindow.xaml(.cs)` — the borderless transparent overlay window; applies extended window styles (`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`) via P/Invoke on `SourceInitialized`, hosts the toast control, animates it in/out, and positions itself in a screen corner. P/Invoke is isolated to this view (mirroring the handle/P-Invoke isolation discipline of `JobObjectTracker`).
  - `Services\AchievementOverlayService.cs` (+ interface) — singleton that subscribes to `IPlayTracker.SessionStarted`/`SessionEnded` (create/teardown the overlay) and `IAchievementService.AchievementUnlocked` (show toast + play sound), marshalling to the UI thread via `App.RunOnUiAsync`. Instantiated at startup like `SystemMediaWatchObserver`.
  - A short bundled WAV (e.g. `Assets\achievement.wav`) added as a `<Resource>` in `Mosaic.csproj` and played via `System.Media.SoundPlayer` from the resource stream.
- **Modified code**
  - `App.xaml.cs` — register `AchievementOverlayService` as a singleton and resolve it during `OnStartup` so its event subscriptions are wired.
  - `Services\AppSettings.cs` — add `GameOverlayEnabled` (default `true`) and `AchievementSoundEnabled` (default `true`); both back-compatible defaults for older `settings.json`.
  - `ViewModels\SettingsViewModel.cs` + `Views\SettingsView.xaml` — two toggles, mirroring the existing `AutomaticUpdatesEnabled` toggle pattern.
  - `Mosaic.csproj` — add the `<Resource>` entry for the sound asset.
- **No data/schema impact.** No EF migration, no model changes. Achievement detection, persistence, and the `AchievementUnlocked` event contract are untouched.
- **Platform:** Win32 interop for the overlay window styling and corner placement (Windows-only, consistent with the app's existing Job Object / WPF dependence).