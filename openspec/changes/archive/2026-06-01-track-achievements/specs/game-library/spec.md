## MODIFIED Requirements

### Requirement: Edit a game
The system SHALL allow the user to edit a game's editable metadata, including its achievement linkage — the Steam appid used for achievement definitions (or an explicit "no Steam achievements"), the achievement source mode, and whether achievement tracking is enabled.

#### Scenario: Edit game metadata
- **WHEN** the user edits a game's name, executable path, launch arguments, working directory, or real-executable override
- **THEN** the system persists the updated values and reflects them in the library

#### Scenario: Edit achievement linkage
- **WHEN** the user sets a game's Steam appid (or declares it has no Steam achievements), changes its achievement source mode, or enables/disables achievement tracking
- **THEN** the system SHALL persist the achievement linkage and configuration with the game
