## MODIFIED Requirements

### Requirement: Track play time across the full process tree
The system SHALL measure a play session's duration based on the lifetime of the entire process tree launched, not only the initially started process, so that games launched via a launcher that spawns the real game are measured correctly. To make this reliable even when a launcher spawns the game and exits within milliseconds, the system SHALL assign the launched process to the tracking Job Object **before the process begins executing** — that is, the process SHALL be started in a suspended state, assigned to the job, and only then resumed — so that every descendant is captured by the job from its first instruction and the assignment cannot lose a race against an early-exiting launcher.

#### Scenario: Launcher spawns the real game and exits
- **WHEN** the launched executable spawns one or more child processes and the initial process exits while a descendant is still running
- **THEN** the system SHALL continue the play session until the last process in the tree exits

#### Scenario: Launcher exits immediately after spawning the game
- **WHEN** the launched process spawns a descendant and exits within milliseconds — before any post-launch assignment step could complete
- **THEN** the descendant SHALL nonetheless be a member of the tracking job, and the system SHALL continue the session until the descendant (and the rest of the tree) exits

#### Scenario: Suspended launch is transparent to the game
- **WHEN** a game is launched
- **THEN** the system SHALL start the process suspended, assign it to the tracking job, and resume it, and the game SHALL run with its configured launch arguments and working directory exactly as if it had been started normally

#### Scenario: Session duration reflects whole-tree lifetime
- **WHEN** the entire launched process tree has exited
- **THEN** the system ends the session and records a duration spanning from launch until the last process exited
