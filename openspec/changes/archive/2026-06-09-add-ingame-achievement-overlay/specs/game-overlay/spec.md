## ADDED Requirements

### Requirement: Overlay tied to a Mosaic-launched play session

The system SHALL display an in-game overlay over a game that was launched through Mosaic for the duration of that game's play session, and SHALL remove the overlay when the session ends. The overlay SHALL NOT be created for a game that Mosaic did not launch, and a session whose overlay is enabled SHALL still launch, track, and record normally if the overlay window fails to be created.

#### Scenario: Overlay appears when a launched game's session starts

- **WHEN** a play session starts for a game launched through Mosaic and the in-game overlay is enabled
- **THEN** the system SHALL create the overlay associated with that session

#### Scenario: Overlay removed when the session ends

- **WHEN** the play session for which an overlay was created ends
- **THEN** the system SHALL remove the overlay and release its resources

#### Scenario: Overlay creation failure does not break tracking

- **WHEN** the overlay window cannot be created for an otherwise-trackable session
- **THEN** the system SHALL continue to track and record the play session and SHALL NOT surface the failure to the player as an error

### Requirement: Overlay is invisible and non-interactive when idle

The overlay SHALL be transparent and SHALL render nothing when there is no notification to show, so that the player is unaware of its presence. It SHALL be click-through (passing all mouse and keyboard input to the game beneath it), SHALL NOT take or steal focus from the game, and SHALL NOT appear in the taskbar or the Alt-Tab task switcher.

#### Scenario: Nothing is shown when idle

- **WHEN** the overlay is active but no achievement notification is being displayed
- **THEN** the overlay SHALL display no visible content

#### Scenario: Input passes through to the game

- **WHEN** the player clicks, types, or moves the pointer over the region the overlay occupies
- **THEN** the input SHALL reach the game beneath the overlay and the overlay SHALL NOT intercept it

#### Scenario: Overlay never steals focus or appears in the task switcher

- **WHEN** the overlay is created or shows a notification
- **THEN** it SHALL NOT activate or take focus away from the running game and SHALL NOT appear in the taskbar or Alt-Tab switcher

### Requirement: Present achievement unlocks on the overlay

When an achievement is detected newly unlocked during an active play session whose overlay is present, the system SHALL present a toast on the overlay that identifies the unlocked achievement and the game it belongs to, SHALL display the achievement's icon when one is available, and SHALL automatically dismiss the toast after a short interval. The toast SHALL be drawn above the running game so the player can see it.

#### Scenario: Toast shown on the overlay on unlock

- **WHEN** an achievement is detected newly unlocked during an active session whose overlay is present
- **THEN** the system SHALL show a toast on the overlay identifying the achievement and its game

#### Scenario: Toast auto-dismisses

- **WHEN** an overlay toast has been shown
- **THEN** the system SHALL automatically dismiss it after a short interval without requiring player input

#### Scenario: A newer unlock supersedes the current toast

- **WHEN** a second achievement unlocks while a toast is still showing
- **THEN** the system SHALL present the newer unlock and restart the dismissal interval rather than losing it

### Requirement: Play an audio cue on achievement unlock

The system SHALL play a short "achievement unlocked" sound when an achievement is detected newly unlocked during an active play session, provided the achievement sound is enabled. The sound SHALL play once per unlock and SHALL NOT block the UI or the game.

#### Scenario: Sound plays on unlock

- **WHEN** an achievement is detected newly unlocked during an active play session and the achievement sound is enabled
- **THEN** the system SHALL play the achievement sound once

#### Scenario: Sound suppressed when disabled

- **WHEN** an achievement is detected newly unlocked and the achievement sound is disabled
- **THEN** the system SHALL NOT play the sound

### Requirement: Overlay and sound are independently configurable

The system SHALL allow the player to enable or disable the in-game overlay and the achievement sound independently, SHALL persist each setting, and SHALL default both to enabled (including for an existing configuration that predates these settings). Disabling the overlay SHALL suppress the visual overlay; disabling the sound SHALL suppress the audio cue.

#### Scenario: Disable the overlay

- **WHEN** the player disables the in-game overlay
- **THEN** the system SHALL NOT create the overlay for subsequent sessions and SHALL persist the disabled state

#### Scenario: Disable the sound

- **WHEN** the player disables the achievement sound
- **THEN** the system SHALL NOT play the audio cue on unlock and SHALL persist the disabled state

#### Scenario: Defaults when unconfigured

- **WHEN** the application loads a configuration that does not specify these settings
- **THEN** the system SHALL treat both the overlay and the achievement sound as enabled

### Requirement: Unlock is never silent when the overlay cannot draw

Because the overlay cannot composite over a game running in exclusive-fullscreen mode, the system SHALL ensure an unlock is never lost in that case: it SHALL still play the audio cue (when enabled) and SHALL still surface the existing in-application notification, so the unlock is conveyed even when the visual overlay is not visible over the game.

#### Scenario: Exclusive-fullscreen unlock still audible and recorded

- **WHEN** an achievement unlocks while the game is running in a mode the overlay cannot draw over (e.g. exclusive fullscreen)
- **THEN** the system SHALL still play the audio cue (when enabled) and SHALL still present the in-application notification
