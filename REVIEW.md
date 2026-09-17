# Encounter music suppression - 0.2.5

Inspection of the installed MusicMan and MusicVolume implementations shows that ambient, location, combat and event track selection feeds MusicMan's music AudioSource. Target-client presentation now owns a scoped mute of that source from Lure/Tease activation until Clear. Existing music selection, playback, mixer state and user volume settings continue normally underneath the mute. Repeated acquisition cannot overwrite the original mute state; source replacement and late initialization are supported. Previews never acquire the mute. No shared player state or network protocol changes are required.

# Jump-safe pursuit and unified recovery - 0.2.3

The prior route builder required a grounded endpoint to be within catch distance of the airborne player. Navigation now uses a separately projected ground target; capture still uses the actual player position and unobstructed three-dimensional distance. Projection begins at the feet, so overhead roofs are not selected as destinations.

A single five-second recovery clock replaces immediate charge-route failure, 1.25-second route/movement failures and 0.25-second stall cancellation. Failed queries retain the old route. Fresh movement checks stop at obstructions, without erasing the last reachable destination. Query success cannot reset recovery until current-route movement or grounded arrival is validated. One distance budget consumes multiple retained waypoints per frame without terrain shortcuts. Frame hitches pause movement and schedule validation.

The installed game's HumanoidBigNoSwim agent uses an 85-degree slope and 0.3m climb setting. Chase queries now use separate surface support and swept body checks, while spawn placement and capture landing remain conservative. The virtual body and visible model align with the support normal; foot rays and pelvis offsets use the same axis. No shared game navigation settings are changed. Runtime/editor tests call the same Traversal implementation.

# Continuous full-event lure - 0.2.2

Full-event lure playback now alternates woo and whisper as each clip finishes on the same spatial source. Discovery stops the lure through the existing stare transition. The server owns a 120-second Lure deadline and enters the existing Reveal/Charge sequence when it expires. This is gated by encounter kind and phase; tease, relocation, eligibility cancellation and total timeout retain their existing behavior. No assets changed.

# Moving-target chase correction ? 0.2.1

The old motor cancelled on the first failed replan against a moving player. This version retains a validated route during bounded retries, checks direct ground routes before navigation, routes toward catch positions instead of the player centre, and repairs small navigation/collider corridor discrepancies with checked side steps. Dense ground samples and tighter corner arrival prevent shortcuts into rocks/trees. Blocked movement pauses before retrying; permanent obstruction still cancels.

The smoke fixture now applies ordinary forward/run input through a test-only Player.SetControls interception, since writing controls in Update alone was overwritten by the game's own input. It asserts that the target really moved and that the production motor replanned before capture. That test patch is never shipped.

Capture cleanup releases constraints, temporary loading state, audio and rendering resources in a finally block, including when world teardown interrupts rollback.

# ForestCrawler 0.2.0 review

The new sequence separates invisible tease presentation from full encounters. Protocol/version matching was bumped to prevent older clients from interpreting new states. Server timing constants are generated from the actual imported audio rather than trusting client-reported lengths.

Natural progression is persisted independently of the existing full cooldown. Starting a tease does not mark it complete; cancellation cannot unlock full encounters. Scheduling makes one server-wide draw before candidate selection, and a cooldown-blocked full draw is skipped.

Catching requires an unobstructed approach; nearby shelter cannot trigger a catch through a wall. The target-client ground motor remains the sole creature movement authority and keeps stride phase tied to actual displacement at the new speed range.

Capture uses a separate camera and an opaque full-screen rendering of the supplied model. No world camera postprocessing or global god-mode setting is changed. Input/damage guards apply only to the captured local player. Origin objects/terrain are retained while destination loading proceeds, and the landing requires one phase-bound server permit plus fresh loaded physics. Before commit, natural time/biome requirements still apply. A player already teleporting is rejected. Timeout/cancellation restores controls and retained-origin state.

Prewarming and capture retention are bounded by the encounter lifecycle and cleared on every presentation cleanup. Persistent data contains only cooldown/progression metadata, never the creature, audio or closeup camera.

See `VERIFICATION.md` for executed tests and unverified multiplayer/listening coverage.

---

## Earlier code and animation review — 2026-09-17

This pass focused on presentation correctness, traversal boundaries, allocation hotspots and the authored horror performance. It is not a multiplayer certification.

## Correctness fixes

- Paused IK: a zero-delta frame could apply the previous pelvis offset again to an Animator pose that had not advanced. The foot solver now leaves paused poses untouched.
- Disappearance: the body sensor was disabled, but the newer head sensor stayed enabled during the audio tail. A shared hiding method disables every sensor and renderer for both local disappearance and the authoritative tail transition.
- Route exhaustion: reaching the last navigation waypoint previously called normal completion even if the player remained outside the stopping radius. That case now stops the preview or gracefully cancels the encounter.
- Terrain discontinuities: two individually valid ground samples could lie on opposite sides of a steep ledge. Segment validation now rejects large vertical jumps while retaining ordinary slopes and small steps.
- Cooldown storage failure: an I/O error during activation previously escaped into generic RPC error handling and could leave an inactive encounter occupying the slot. It now explicitly cancels and records the storage error.

## Allocation improvements

- Cache instance renderers instead of traversing the hierarchy on every movement update.
- Use a reusable raycast buffer for ordinary gaze obstruction checks. A saturated buffer falls back to a complete query so dense player equipment cannot hide a wall from validation. No frame-rate improvement is claimed without profiling.

## Animation changes

- Idle: asymmetric neck tilts, short double/triple head twitches, offset shoulder movements, and long held poses. Breathing remains restrained.
- Reveal: brief compression, a sharp opening movement, irregular neck shaking, uneven arm positions, and a crouched finish that leads into the charge.
- Charge: forward lean with asymmetric shoulder/arm motion and authored head recoils around the stride. Foot trajectories, movement authority, stride length and terrain IK remain coordinated.
- Motion is authored and reproducible, not random per-frame limb rotation. Loop endpoints, head-motion bounds, deformed mesh quality and planted-foot trajectories are checked.

## Verification

See `VERIFICATION.md` and the recorded artifacts. Focused editor regressions cover zero-delta IK and saturated gaze obstruction buffers. Core tests cover ordinary slopes, small steps, cliff ascent/descent and non-finite terrain deltas. Actual-game smoke testing exercises camera discovery, stare, relocation, reminder audio, charge and complete disappearance cleanup.

Remaining manual coverage includes two real clients, dense Mistlands visibility, varied terrain and compatibility with all unrelated Development mods. The exact cause of the earlier user-specific gaze miss cannot be proven from the old logs; current gaze diagnostics report the blocker and range.
