# ForestCrawler verification

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
