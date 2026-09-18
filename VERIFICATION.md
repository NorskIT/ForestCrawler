# ForestCrawler verification

## 0.2.8 screen cues, retreat and parallel arms

Release compilation targets the installed Valheim 1.0.14 assemblies. The core suite passes 94 checks, including warning boundaries at 105/120 seconds, retreat thresholds at +50/+75m, invalid distances and phase/type exclusions.

Unity arm validation passes 27 combinations of distance (3/12/30m), height (0/6/15m) and lateral offset (-5/0/5m). It checks forearm/hand side order and constant width, baked skin reach and exact restoration. The rendered arm preview was inspected; the two extended arms remain parallel. Existing visual stance/gaze/music checks also pass.

Actual Valheim full-world run `artifacts/runtime-20260918-124849` passes 116 assertions. It exercises camera rendering, rune ordering/deduplication, effect opt-out and cleanup, exact MusicMan suppression/restoration, tease, alternating lure, server warning, automatic reveal, retreat warning/early chase, normal discovery/relocation, moving/jumping target capture and dry teleport. The player flees 58.16m and jumps 15 times. The warning/deadline clocks and retreat baseline are explicitly injected test inputs; the production server tick, shared target position, routed cues and phase transitions run normally. This is not a measurement of a human running the extra 75m.

The hidden test window does not render automatic camera frames. Screen tests explicitly render the actual game camera with its installed effects, then inspect saved 1280x720 and 2560x1080 images. They do not establish smooth frame pacing or human comfort. Visual inspection caught automatic atlas rescaling and a font-alpha issue that state-only assertions missed; both were fixed before the successful full-world run. The negative transition was subsequently adjusted to retain more scene contrast and the left-to-right rune reveal gained a soft fade.

Final-binary and final-bundle run `artifacts/runtime-20260918-130016` passes 41 assertions, including the camera/text regression, native rock comparison, real 30-second arm delay, spatial I-see-you playback, side order and shoulder-width preservation during the rendered pose, collision-checked pull, capture, dry teleport and physics restoration. It also tolerates a test-only 0.9-second gap in shared player position publication. Final negative/static and rune captures were inspected; the static contribution was reduced to preserve the dark scene and the negative curve now preserves more shadow detail. The actual-game arm image shows separate parallel arms. Deployment hashes are checked against this run.

All game tests use one actual client and isolated saves. Two-client visibility/isolation, dedicated-server runtime, the complete Development mod combination, automatic foreground frame pacing and human audio/comfort evaluation remain unverified. No user character or world was used.

Native-fixture runs `125235` and `125634` did not complete the arm assertions: native circling lost line of sight in one, and an ordinary capture ended the other before an arm attempt. The final fixture holds the test player on a 12m solid roof and moves its test position to an exposed edge when required. This isolates extension/pulling from incidental slips, reachable terrain and occlusion; it is not a human rooftop gameplay test. Production visibility and movement logic are unchanged.

Earlier screen runs are development diagnostics, not final visual acceptance: `124105` failed the automatic-render assertion; `124311` rendered the world without the previous GUI overlay; `124644` exposed incorrect atlas crops in its saved image despite passing state assertions.


## 0.2.7 native pursuit and extended-arm capture

Release builds against the installed Valheim 1.0.12 assemblies without warnings or errors. The package build runs 81 core checks, including the 30-second grab boundary, 30-metre range, duplicate-phase rejection, retry delay, 60-second deadline and invalid numeric input. Old custom-route/recovery tests were removed with those implementations.

Actual Valheim full-world run `artifacts/runtime-20260917-233330` passed 100 assertions: independently inspected, actively playing MusicMan source suppression/restoration, preview after previous capture cleanup, tease, alternating lure, timeout-to-charge, real camera gaze, stare, relocation, native pursuit, capture, dry teleport and cleanup. The target ran 32.16m and jumped 13 times before capture. The two-minute lure deadline was advanced by the fixture; denied landing and capture cancellation are injected transaction tests.

Actual Valheim run `artifacts/runtime-20260917-234558` passed 29 assertions. A vanilla Greydwarf and the detached native driver pursued a player on the same generated rock; the driver inherits the actual navigation agent/body radius and executes both native AI and Character motor updates. An elevated solid-roof fixture then exercised the real 30-second unsuccessful-pursuit delay, 30m range rejection, solid sight obstruction, arm extension, torso attachment, collision-checked pulling, ordinary capture and dry landing. A test-only 0.9-second gap in player ZSyncTransform position publication verified the pull envelope and repeated contact reporting. Repeated pull cancellation restored the previous physics flags, constraints and position. The camera was directed away during lure to isolate the arm scenario from first-discovery relocation.

Unity editor checks passed for actual skinned-mesh reach at 3/12/30m and restoration of the authored pose, alongside gaze, music and visual foot IK. Rendered arm poses were inspected in editor and actual-game captures. Maximum measured stance drift was 0.001877132m in the visual fixtures; steep editor surfaces are not evidence that native Greydwarfs can climb vertical walls.

Final-binary run `artifacts/runtime-20260917-234935` passed five main-menu asset/audio checks, including scene destruction before repeated audio cleanup. No world was loaded in that final focused run. The last changes after the full-world tests removed unused custom-route helpers and made destroyed-audio cleanup idempotent; the focused check verifies that cleanup change. Final package/deployed file hashes are compared with this run.

Failed development runs exposed sensing-range loss, stale preview phase state, rooftop pull clearance and a fixture accidentally permitting gaze relocation. They are not counted as successful runs. HuntPlayer uses Valheim's built-in pursuit mode; no custom creature route planner or transform motor remains.

Limits: all game tests used isolated saves and one actual client. Two-client visibility/isolation cancellation, dedicated-server execution, the complete Development mod combination and human audio evaluation remain unverified. Registry absence checks and injected delayed position publication are not substitutes for those multiplayer tests. No original user world or character was used.

## 0.2.6 exact music source and elevated rock targets

The original bug is reproduced in actual Valheim: the hierarchy lookup returns `ocean_ambient_loop`, while the private `MusicMan.m_musicSource` field points to `music`. Focused run `artifacts/runtime-20260917-222738` passes 38 assertions with the corrected DLL. It plays a real game track at nonzero source volume, independently reads the exact source, inserts a decoy source, verifies mute/restoration across injected tease/full presentation phases and verifies saved MusicVolume is untouched. In the same isolated world, the production chase motor routes around a steep-front rock fixture, climbs its accessible back and reaches the elevated player. The fixture has no saved/network entity and is removed afterwards. This is not a human listening test or proof that every natural rock is traversable.

Full actual-Valheim run `artifacts/runtime-20260917-222935` passes all 72 checks, now inspecting the exact MusicMan source. It completes tease, discovery, relocation, recovery and capture/teleport; the target runs 48m and jumps 12 times before capture. Both cancellation and completion restore music. Tests of route failure and capture fallback retain the previously documented injected fixtures.

Unity tests validate the same production surface search against a rock with an inaccessible front and accessible back, with every resulting edge rechecked; a blocking wall remains impassable. The successful editor search returned 31 waypoints in approximately 31ms on this machine. Existing slope, foot-contact and mute ownership tests also pass. Release compilation and the 85 core checks are run by the package build. Dedicated/two-client behavior and subjective audio are not newly verified.

## 0.2.5 encounter music suppression

Correction discovered in 0.2.6: the source-specific assertions below used the same hierarchy lookup as production. On the installed game it selected `ocean_ambient_loop`, so those assertions did not establish that the actual music source was muted. They are preserved as historical test results, not valid evidence of music suppression. The 0.2.6 regression inspects `MusicMan.m_musicSource` independently and requires an actively playing track with nonzero source volume.

Unity tests of the production MusicSilence class pass active suppression, inactive preview behavior, repeated acquisition/cleanup, preservation of changing volume and prior mute state, source replacement/destruction and late initialization. Existing terrain/animation editor checks also pass.

Actual isolated Valheim run `artifacts/runtime-20260917-221205` passes all 72 assertions. The real MusicMan AudioSource remains unmuted for previews, is muted during tease/full lure, relocation, chase recovery and capture, and regains its previous mute state after tease completion, forced route cancellation and full completion. The full event still catches a running/jumping player and completes the scare, dry teleport and cleanup. No human listening or two-client multiplayer test is claimed.

Earlier run `artifacts/runtime-20260917-220908` passed preview, tease suppression/restoration and full-lure checks but stopped at the existing retained-route movement assertion; it is retained as an unsuccessful full run. No pursuit implementation changed in this release. The final DLL and unchanged bundle are checked against the successful fixture before local deployment.

## 0.2.4 repository and package artwork

Release compilation succeeds without warnings or errors and all 85 core checks pass. This revision adds the supplied artwork as a 256x256 package icon, a matching package manifest, concise README and developer-command reference. Detailed documentation is preserved in DEVELOPMENT.md. The asset bundle and encounter implementation are unchanged from 0.2.3; no new in-game test is claimed for this packaging revision.

The asset build script now regenerates the production Traversal editor dependency alongside the other shared sources, so ignored generated files are not required in a new checkout. Package validation checks the ZIP layout, icon dimensions, matching manifest/assembly versions and inclusion of DLL, bundle and attribution.

## 0.2.3 jump-safe pursuit and five-second recovery

Release compilation against the installed game succeeds with zero warnings/errors. All 85 core checks pass, including exact five-second boundaries, consecutive failures preserving the original deadline, recovery reset and a fresh deadline for a later obstruction.

Unity editor tests exercise the production Traversal, ApproachPath and FootSolver implementations. They pass direct routes and body clearance at 30/45/60/85 degrees, connected transitions from flat to 85 degrees, airborne target projection and changed landing positions, wall rejection, obstacle removal and unsupported cliff rejection. Foot contact is tested at 8/12/18m/s on 0/15/30/45/60/85-degree surfaces; maximum measured stance drift is 0.001877132m. These steep-slope results are editor physics/animation tests, not a claim of human-verified 85-degree movement in Valheim.

Actual isolated Valheim run `artifacts/runtime-20260917-215423` passes all 63 assertions. The target runs 27.81m and jumps eight times using normal game movement/Jump, with 3,039 observed airborne frames. The production creature replans, catches the target, shows the animated face and non-spatial scream, teleports to validated dry ground and restores controls. Existing lure alternation, timeout-to-chase, tease completion, gaze, stare and 40-70m relocation also pass.

Test-only injected path failures verify following the retained route for 1.5 seconds, recovery before five seconds, initial charge route failure waiting for two seconds, persistent obstruction surviving four seconds then cancelling after five, and chase audio retention/cleanup. Physical obstacle rejection and removal are separately tested in editor physics. These injections are never shipped; they are not a two-player network test. Denied-landing and clear-during-capture regressions also pass.

Earlier run `artifacts/runtime-20260917-215047` passed the recovery assertions but failed its catch assertion after a real-terrain movement blockage. Additional collision diagnostics were added before the successful run. Permanent physical obstacles still legitimately cancel after five seconds; the successful fixture does not establish that every possible terrain route is reachable. Two real clients, dedicated-server changes and subjective movement/audio evaluation remain unverified in this revision.

The Development package must match the DLL and unchanged asset bundle from the successful runtime fixture by SHA-256. No asset rebuild is required; production surface-relative IK operates on the existing authored animations.

## 0.2.2 continuous lure and undiscovered timeout

Release compilation against installed Valheim assemblies succeeds with no warnings or errors. All 78 core checks pass, including the exact 120-second boundary, late ticks, full-only gating and cancellation of the deadline after discovery.

Actual isolated Valheim run `artifacts/runtime-20260917-210246` verified alternating spatial clips without deliberate silence or overlapping sources, tease completion after one clip, timeout reveal replacing lure audio, and transition to real terrain chase. The fixture advanced the authoritative Lure phase timestamp to exercise the two-minute transition; it did not wait 120 wall-clock seconds. Normal direct gaze, stare, relocation at 40-70m and I-see-you progression also passed. The later moving-target catch assertion failed because the fleeing route crossed 32-37 degree terrain, exceeding the existing 30-degree ground limit; the production motor cancelled after bounded retries. This remains a terrain limitation, not a successful capture test.

Repeat actual Valheim run `artifacts/runtime-20260917-210605` passed all 54 assertions, including the new continuous-lure/advanced-clock timeout checks and the complete normal discovery, relocation, moving-target catch, animated closeup, dry teleport, denied-landing fallback and cancellation cleanup. The shipped DLL is identical to this tested DLL. The first run and its terrain limitation remain recorded above.

Eligibility and overall timeout retain priority over the new lure deadline. Natural encounters can therefore end with the default midnight window before reaching 120 seconds. No new two-client multiplayer or subjective audio listening test was performed.

## 0.2.1 moving-target correction

The Development log recorded repeated `Charge route became blocked` cancellations. The old charge discarded its current route whenever a moving-target navmesh query failed. It also required navigating all the way to the player's centre.

The updated motor retains an existing route while retrying for at most 1.25 seconds, with fresh collision checks before each movement. It first attempts a densely sampled direct ground route (bounded to 90m) ending inside catch range. Otherwise, navigation queries target reachable catch positions around the player and repair small corridor deviations with validated side steps. Terrain samples are 0.35m apart, the route budget is 512 points, and movement reaches corners within 0.01m instead of cutting them at 0.15m. A blocked movement step pauses and retries before cancellation. Near-target updates run at 0.1-second intervals after 0.35m of target displacement. Real obstructions still prevent contact; the code does not teleport the creature through obstacles.

Release compilation and 72 core tests passed. New Unity physics regressions passed for a moving target without a navmesh, wall rejection, traversable slope, unreachable raised target, bounded range, and no partial route after failure. Actual-game moving-target regression passed 47 assertions in `artifacts/runtime-20260917-204923`. The target moved 62.21m using normal player input; the production motor replanned, recovered from two local blocked movement steps, reached capture, completed the face/teleport sequence, and cleaned up. An earlier fixture failed to move its target because normal input overwrote the scripted controls; test-only input interception fixed the fixture. Subsequent runs reproduced route and movement obstructions while fleeing, prompting the corridor/step changes described above. The 0.2.1 Release package targets Development only, with file-hash verification during deployment.

## 0.2.0 verification

Updated 2026-09-17. Historical 0.1.0 records remain in `artifacts/verification-0.1.0.md`. Old second-discovery/reminder/audio-tail tests do not describe the new sequence.

## Executed checks

- Release compilation against installed Valheim 1.0.12, Unity 6000.0.75f1 and BepInEx 5.4.23.5: zero warnings/errors.
- 72 pure logic/storage checks: server-wide 20% probability at one player over 120 eligible seconds, decreasing total rate for larger populations, no catch-up, first-tease selection, cooldown gating without reroll, per-world/player progression, heartbeat endpoints/cap/interpolation, adaptive chase speed, gaze phase gating and swept distance.
- Blender closeup export creates three facial morphs with 2,889 / 2,792 / 3,343 affected vertices. The world model retains its existing 50k/20k/5k LODs and original three clips. Existing authored-model validation contains 144 checks.
- Unity builds and reloads the bundle, samples animation deformations and all three closeup morphs. Production foot IK and gaze tests passed again. IK ran at 8, 12 and 18m/s on 0, 15 and 30 degree slopes; maximum stance drift between simulated 60Hz frames was 0.001867m. These are editor tests, not observations across all game terrain.
- Processed audio validation: mono PCM heartbeat is 0.240s with zero-valued endpoints; close loop is 18.672s with a normalized boundary difference of 0.000763. Both peak at approximately 0.85. First-thump extraction preserves pitch. Source downloads remain unchanged.
- Actual installed Valheim, isolated disposable character/world: `artifacts/runtime-20260917-200928` and `artifacts/runtime-20260917-201331` each passed 45 assertions. This covered preview/animations/terrain movement/clear, invisible tease and audio completion, actual continuous camera-ray discovery, half-second stare, 40-70m relocation, spatial voice, automatic reveal/chase, authoritative catch, 2D screams, animated facial morphs, temporary movement lock, 200-500m teleport onto loaded dry terrain, and cleanup.
- Focused actual-game transaction fixtures in that run also verified direct damage suppression without god mode, gameplay input suppression, a five-second no-permit fallback retaining the origin, and immediate `crawler_clear` cleanup with original Rigidbody constraints restored. Those fallback/cancellation cases inject a transaction; they do not simulate a second networked player.
- Actual-game face capture is saved as `capture-face.png` within each successful capture run. Framing/materials were adjusted after inspecting those images.

- Final audio refinement extracts the first physical thump without resampling. `artifacts/runtime-20260917-201709` passed four focused actual-Valheim asset/sample checks on the final bundle. Its Release DLL is SHA256-identical to the last full 45-assertion run; this focused run loaded no world.

## Failures retained and addressed

- The first generated heartbeat used a transient AudioClip asset whose sample data did not survive bundling. Production runtime validation rejected it. Derived audio is now written as real PCM WAV files and imported normally.
- One capture found no validated landing by the deadline and safely retained the origin. Destination terrain/objects now prewarm during chase, loaded safe candidates are preferred, and a small local grid searches around the candidate before requesting a server permit.
- One chase encountered an unsafe ground/obstruction segment and cancelled, as designed, rather than crossing it. Dynamic game terrain can still abort encounters. A later complete run reached capture and a validated landing.

## Remaining manual coverage

- Two real clients, including a dedicated server: target-only visibility/audio and isolation cancellation during every phase, especially a pending landing permit.
- Listening assessment of the original spatial clips, heartbeat distortion, close loop and headphone scream levels. Automated sample checks do not establish subjective listening quality.
- Human assessment of the face animation and scare timing, routing around houses/doors and feet across more game terrain.
- In-game direct/proximity detection against varied obstructions and dense Mistlands fog. Editor ray tests and actual camera discovery passed; broad fog coverage remains manual.
- Compatibility playthrough with the other Development mods enabled. The runtime fixture contains only ForestCrawler and its test plugin.

## Runtime isolation

`scripts/Test-Runtime.ps1` starts the actual installed Valheim executable using a separate BepInEx runtime. The test plugin overrides the game's save-path API and disables cloud access before creating a disposable world/character. It never loads the user's world. Test plugins, game assemblies and Unity/Blender executables are excluded from release packages.

## Local acceptance

Launch Gale **Development**, enter a world and press F5. `crawler_spawn` previews the model. `crawler_encounter tease` tests the invisible call. `crawler_encounter` forces the full sequence after 30 seconds of isolation without changing natural progression. `crawler_start` uses natural progression and advances shared time after successful preflight. `crawler_clear` cancels local presentation; `crawler_status` reports type, phase, heartbeat, destination readiness, progression and cooldown state.
