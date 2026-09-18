# ForestCrawler development

A private, non-damaging Valheim horror encounter using the supplied Smile model. The server chooses one isolated player; only that player's client creates the creature and its spatial audio.

The complete client package contains the Release plugin and Unity AssetBundle with the supplied model, three original animations, eight audio assets and three closeup facial morphs and a model-only horror shader. See `VERIFICATION.md` for executed checks and remaining manual acceptance tests.

The latest animation pass adds held neck tilts and irregular head twitches to idle, a compressed anticipation and sharp asymmetrical scream, and head recoils/uneven arm movement during the charge. See `REVIEW.md` for the associated code review and fixes.

Local deployment defaults to Gale **Development**. Building a package does not install it. Prod-v1 and remote servers are not changed by the default deployment workflow.

## Build

The reference installation is Valheim 1.0.12, Unity 6000.0.75f1 and BepInEx 5.4.23.5 on Windows. `Environment.props` resolves the installed game and Gale Prod-v1 BepInEx. Override these through `Environment.local.props` or MSBuild properties when necessary.

1. Install the .NET SDK, Valheim, BepInEx and a licensed Unity Editor 6000.0.75f1.
2. For a normal source checkout, run `scripts/Build-Assets.ps1 -SkipBlender -UnityPath 'PATH/Editor/Unity.exe'` to build from the checked-in Unity assets.
3. To regenerate the models from original downloads instead, run `scripts/Import-Assets.ps1` and install Blender 4.5.3 LTS. Original downloads and build tools are not included in Git.
4. Run `scripts/Build-Assets.ps1 -BlenderPath 'PATH/blender.exe' -UnityPath 'PATH/Editor/Unity.exe'` when regenerating geometry, animation and facial morphs.
5. Run `scripts/Build-Package.ps1`, then `scripts/Deploy-Local.ps1 -Profile Development`. Deployment to another profile requires an explicit profile selection.

Run `scripts/Test-Editor.ps1` for visual/IK checks. `scripts/Test-Runtime.ps1` uses an isolated actual-Valheim world; `-NativeOnly` compares native pursuit on generated rock and tests rooftop capture, `-MusicOnly` isolates music suppression, and `-AssetsOnly` loads the final assets and checks teardown without loading a world. These scripts refuse to run alongside an existing Valheim process.

Run pure logic tests separately with `dotnet run --project tests/ForestCrawler.Tests -c Release`.

Client packages require both `ForestCrawler.dll` and `forestcrawler.assets` in `BepInEx/plugins/ForestCrawler`. Dedicated servers use the same DLL without loading the bundle; `Build-Package.ps1 -ServerOnly` produces that package. Matching mod versions on the server and every connected client provide the heartbeat coverage needed for isolation. Missing/stale clients disable encounters conservatively. Local previews remain possible when the server lacks the mod; no client takes over natural scheduling.

## Commands

Launch **Gale Development**, enter a world, press **F5**, and run **`crawler_spawn`** after the complete package has been installed. The plugin uses the installed game's `Console.SetConsoleEnabledForThisSession` API, preserving all Gale/Steam launch arguments and without enabling devcommands.

| Command | Behaviour |
| --- | --- |
| `crawler_spawn` | Idle local preview on safe ground 10-15m ahead, retained until cleared. |
| `crawler_anim idle` | Return preview to idle. |
| `crawler_anim scream` | Reveal animation, then idle. |
| `crawler_anim charge` | Actual terrain-following approach with a safe stop. |
| `crawler_encounter` | Full test for caller; bypass biome/night/cooldown throughout; retain 30s isolation, cancellation, death/disconnect and timeout. |
| `crawler_encounter tease` | Test the invisible spatial-audio tease without changing natural progression. |
| `crawler_start` | Natural selection/progression rules, replacing only the trigger roll; preflight placement, then advance shared time to the next midnight window. |
| `crawler_clear` | Remove caller's previews/encounter, sound and temporary state; retain persistent cooldown. |
| `crawler_status` | Asset readiness, authority, phase, eligibility failure, online count, rate/cooldown, native path state, AI/motor update counts, unsuccessful-pursuit duration and grab readiness. |

With a ForestCrawler server present, state-changing debug commands require the host or a server administrator. Status and cleanup are available to the invoking player. `crawler_start` affects every player's world time, never rewinds it, never freezes the clock and does not shorten the real-time cooldown. It reports unmet criteria rather than waiting them out. No remote server deployment is performed by the local deployment script.

## Encounter and probability

During both full and tease encounters, only the selected client's actual `MusicMan.m_musicSource` is temporarily muted; hierarchy-based audio-source lookup is not used. Suppression begins when the lure/tease activates, persists through relocation, recovery and capture, and ends on completion, cancellation, clearing or world exit. Previews do not mute music. Playback scheduling and volume preferences remain untouched; cleanup restores the source's previous mute flag, including when it was already muted. A replaced music source is acquired and the old one released. Monster audio and environmental sound effects retain their existing behavior.

Allowed biomes are Black Forest, Swamp and Mistlands. Require 30 seconds alone, with no other living player within 150m of either the player or the creature. Isolation uses server-side character ZDOs, including players with map sharing disabled, plus client heartbeats. It is not based on public map markers.

The default midnight window is raw network-day fraction `[0.95, 1) union [0, 0.05)`: 120 real seconds with the installed 1,200-second day. This is independent of EnvMan's remapped/smoothed lighting fraction. A creature can therefore be cancelled by the window ending before the 180-second encounter maximum.

Once per real second, the server evaluates `p = 1 - exp(-rate * elapsedHours / onlineCount)`. The default rate is 6.6943065 per eligible hour: approximately 20% over a continuously eligible two-minute window for one player, 10.56% for two, 5.43% for four, and 2.75% for eight. This is the **whole server's** chance; it is not independently rolled for each candidate. After a trigger, choose one eligible player uniformly. Missed ticks and periods without eligible players create no accumulated opportunities. At most one encounter, including a test or audio tail, owns the server slot.

Natural progression is per player and world. The first encounter is always an invisible tease: one woo/whisper clip from safe ground at 60-90m, followed by cleanup. Only successful audio completion unlocks full encounters. Later draws are 50/50 tease/full; a blocked full draw is skipped, never rerolled. A shared 15-real-minute quiet interval starts at every natural activation. Full encounters additionally start the minimum 45-minute cooldown. Cancellation retains those intervals. Persistence lives under `BepInEx/config/ForestCrawler/`: `tease-WORLD.txt`, `quiet-WORLD.txt` and `cooldowns-WORLD.txt`. Debug encounters and previews never modify progression.

Full encounters lure at 60-90m. A random first woo/whisper clip is followed by alternating woo and whisper, with one spatial source and no deliberate gaps or overlap, until discovery. If the initial lure remains undiscovered for 120 real seconds from activation, the server starts scream/reveal and chase from the current location, without relocation or I-see-you. Eligibility cancellation and the overall encounter timeout still take priority: the default two-minute natural midnight window can end before this fallback, while `crawler_encounter` bypasses the time requirement. Tease still plays only one clip. Discovery requires continuous direct camera aim (35m/0.4s) or unobstructed proximity (5m). The creature faces the target for 0.5 seconds, relocates once to 40-70m outside view in a different direction, and plays I-see-you from that location. The voice automatically starts a scream/reveal and fast charge. There is no second discovery or reminder loop. Pursuit uses the installed Greydwarf's MonsterAI, BaseAI and Humanoid/Character motor. The built-in HuntPlayer mode keeps the scripted target in pursuit from the 40-90m encounter distances; ordinary Greydwarf sensing alone can lose a fleeing player before getting into view range. Native route caching, circling, target search, obstacle response and Rigidbody walking remain in charge of locomotion. The closest-player fallback is restricted to the encounter owner. The horror chase changes run speed to `min(18, max(12, playerHorizontalSpeed + 3))`; preview uses the configured speed. Ranged throws, structure attacks, damage, fire fear, drops and unrelated visual/audio effects are excluded. It does not gain wall climbing or a custom surface planner.

During chase, a non-spatial heartbeat interpolates from 100 BPM at 100m to 230 BPM at 20m, clamped at both ends. Volume rises from .14 to .58 and distortion from .02 to .68. A 240ms first-thump extraction preserves the original pitch and fits the maximum beat cadence. Two DSP-scheduled sources discard missed beats instead of catching up. The close-chase loop fades in below 25m, reaches full volume at 20m, and exits above 30m. Output gains remain bounded.

At a validated 2.5m catch, hide the world creature, stop pressure audio, and show the supplied model's face and shoulders on an isolated rendering camera. Three authored facial morphs and neck movements animate it for 4 seconds, extending only to a 5-second landing deadline. A random supplied caught scream plays in 2D, followed by the existing scream. Menus and console remain accessible. Movement/combat input and incoming damage are suppressed only for the captured local player; cleanup restores previous Rigidbody constraints without changing god mode.

During chase, select a candidate 200-500m from the player using world-generation height/slope data. During capture, stream its terrain and objects while retaining the origin. Actual loaded physics must confirm dry terrain, slope, capsule clearance, no overhead structure, no liquid trigger, and no lava. The server grants one phase-bound coordinate permit after checking range, world height and isolation. Commit during the face overlay only after fresh validation. Any biome is allowed for this terminal landing. If loading/validation/authorization misses the deadline, retain the original position. Cancellation stops sound and restores control immediately; it never forces an unvalidated landing.

Camera discovery runs after normal camera/animation updates, uses a direct viewport-centre ray against body/head sensors, and excludes only the local player's own colliders from obstruction queries. World obstructions and mist still block gaze. `crawler_status` includes gaze accumulation, range, target distance and the current blocking reason.

After 30 seconds without reachable pursuit progress, an unobstructed torso ray and a three-dimensional distance of at most 30m permit arm extension. Native wandering continues while the target is unreachable. Extension takes one second and checks visibility/range again before contact. The authorized pull moves at 10m/s, lifts elevated targets 1.5m, crosses above the creature to clear the roof edge, then descends toward capture range, and sweeps the player's capsule through every physics step. A blocked pull releases to the last safe position and retries no sooner than three seconds later. Contact enters the existing capture transaction. Unsuccessful pursuit ends after 60 seconds; circling or a successful query alone does not reset this deadline. A current path ending within capture range plus actual movement toward the target resets it. Eligibility and the overall 180-second encounter deadline always take precedence. Configurable defaults are under the Capture section; crawler_status reports native AI/motor ticks, path state and grab readiness.

## Authority, movement and cleanup

The server owns eligibility, rate, cooldown, approved positions, phase and cancellation. The target supplies camera discovery and local loaded-scene terrain proposals. Every message has a protocol, encounter identifier and phase sequence; reports are bounded by phase, owner and distance. The target client's native Character Rigidbody motor is the sole creature movement authority. The visible supplied model follows that driver; root motion and foot IK never move the driver.

The authored 0.7-second running cycle covers 5.6m at 8m/s, with foot-contact metadata encoded in the rig report. Runtime animation follows distance actually travelled. Two-bone IK preserves world-space stance locks, releases swing feet, aligns contacts to ground and adjusts pelvis height. The navigation agent and physical body are inherited from the installed Greydwarf prefab, rather than approximated with custom capsule clearance or route repair.

The server checks eligibility at 10Hz; the client checks local hazards every frame and stops if its authority lease expires after one second. Network cancellation is bounded by observation/transit time, not literally instantaneous across computers. The hidden local driver contains the actual MonsterAI, Humanoid, Rigidbody and ZNetView required by native movement. Its private ZDO is initialized without ZDOMan creation or sector registration, never added to ZNetScene, and never sends a routed RPC. Scoped hooks keep state revisions local and dispatch native animation/alert callbacks locally. Cleanup releases detached extra data and native update-list membership. Combat/HUD hooks and per-collider character collision exclusions apply only to marked drivers; normal mobs are unaffected. The supplied visual and arms remain local rendering objects. New GrabWindup/Pulling phases and protocol 3 require matching 0.2.7 clients/server. World changes clear client entities; only cooldown metadata persists, never encounter entities.

Mist checks combine the installed ParticleMist API with sampled visibility and an eight-metre conservative cap in uncleared mist. This remains subject to actual Mistlands runtime testing.

## Assets and reproducibility

The opaque model-only horror material darkens the lower body, adds animated screen-space surface grain and offsets red/green/blue contour passes. Grain changes brightness, never transparency; the entire body writes depth and blocks the background. It does not alter the player's camera or other objects. In `BepInEx/config/norskit.ForestCrawler.cfg`, `[Visuals]` exposes `HorrorStrength` (default 0.9, zero restores the ordinary material), `ChromaticPixels` (1.8) and `GrainPixels` (1.2). Restart after editing these settings. The effect's visibility varies with scene lighting and distance; grain runs at six updates per second without full-screen flashes.

`scripts/Prepare-Model.py`, `Animate-Model.py` `Validate-Model.py` and `Closeup-Model.py` produce the Blender source, FBX, rig metadata, previews and measured deformation/contact report. Three LODs contain 50,000 / 20,000 / 5,000 triangles. `unity/Assets/Editor/CrawlerBuild.cs` produces the prefab, materials, controller and platform bundle, rejecting missing clips/rig/meshes/audio. Original source hashes are recorded when importing. See `ATTRIBUTION.md` for model and supplied-audio provenance.

Deployment touches ForestCrawler's folder and the explicitly migrated baseline rate in its own Development config, backs up an existing installation under `artifacts/backups`, and refuses to replace plugins while Valheim runs. `Rollback-Local.ps1` restores the two runtime files from a selected backup. The unrelated profile errors already present in Prod-v1 are not changed by this mod.

## 0.2.8 screen cues and retreat

Protocol 4 adds encounter-scoped cue IDs (Start, Warning, Run, Close, Escape). Both host and clients require 0.2.8. The server deduplicates cues and derives retreat from shared player XZ position relative to the first accepted spawn point and its initial distance. Only Full/Lure observes +50m and +75m thresholds; walking and sprinting behave identically. No cumulative travel or post-relocation reset is used. Eligibility cancellation still wins.

The undiscovered chase deadline is 120 real seconds after activation. Warning occurs at 105; reveal begins one animation duration before 120. Discovery or +75m retreat can start the usual reveal earlier. First Charge emits Run; distance <=50m emits Close, including an initial charge already inside that radius. Presentation deduplicates by encounter, Run replaces stale cues, and Close queues behind Run. Natural encounter eligibility can still end an event before its automatic deadline.

`EncounterScreen` owns a camera image-effect component and per-encounter glyph textures. It follows the current game camera, never changes saved graphics preferences, excludes previews/dedicated servers, and releases everything on clear/cancel/capture. Local `[Visuals] ScreenEffects`, `ScreenStrength` (0..1) and `RuneText` affect presentation only. Tease uses half strength. Negative introduction lasts 1.5s; CCTV pulses last 0.6..1.2s with 4..8s spacing. The regular image is approximately 15% darker plus a mild vignette. Rune text is composited after the screen shader; HUD and capture overlay render afterwards. Font and glyph resources are per encounter and disposed with the camera pass.

A-Z rectangles in the supplied atlas explicitly exclude the printed labels. Runtime alpha derives from red intensity and source alpha, leaving the original file intact. Rune reveal lasts 1s; translation starts at 2.5s; text clears at 5.25s. Text sits at 65% screen height, constrained to 82% of screen width, and reads the live GuiScaler HUD scale. The shader and readable atlas are bundled and validated at load.

Extended arms use signed shoulder offsets and a horizontal lateral axis perpendicular to reach. Forearms and hands keep the same width at full extension, without swapping anatomical sides. Wrist orientation bends inward. The first GrabWindup plays the existing spatial voice clip once per encounter; retries do not replay it.

The runtime fixture supports `-Rendered` (omit Valheim's batchmode flag), but a hidden window still requires explicit `Camera.Render` for deterministic screen captures. `-NativeOnly` now includes the screen regression and an elevated-target fixture before validating arm capture. The target position is held on a solid 12m platform and moved to a visible edge in test code only; the shipped plugin contains no target-holding helper. Screen capture files include the game's existing image effects.
