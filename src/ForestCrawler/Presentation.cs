using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ForestCrawler;

internal sealed class Presentation
{
    private readonly Plugin plugin;
    private GameObject? creature;
    private Animator? animator;
    private AudioSource? sound;
    private FootSolver? feet;
    private CapsuleCollider? detection;
    private Renderer[] renderers = Array.Empty<Renderer>();
    private readonly List<Collider> gazeSensors = new();
    private string gazeReason = "Not evaluated";
    private EncounterKind kind;
    private ChaseAudio? chaseAudio;
    private readonly MusicSilence music = new();
    private static readonly AccessTools.FieldRef<MusicMan, AudioSource> ReadMusicSource =
        AccessTools.FieldRefAccess<MusicMan, AudioSource>("m_musicSource");
    private static AudioSource? MusicSource => MusicMan.instance ? ReadMusicSource(MusicMan.instance) : null;
    private Capture? capture;
    private Vector3? landingCandidate;
    private bool captureFinished;
    private string id = "", clip = "idle";
    private int sequence;
    private Phase phase;
    private Settings settings;
    private bool preview, test, searching, relocation, discoverySent, awaitingFinish;
    private float leaseAt, phaseAt, searchAt, nextSearch, nextPose, gaze, travelled, nextRoute, speed;
    private string lureClip = "whisper";
    private Vector3 position, oldPosition, previousTarget, routedTarget;
    private readonly List<Vector3> route = new(), proposedRoute = new();
    private readonly ChaseRecovery recovery = new();
    private Vector3 groundTarget, lastReachable;
    private bool hasGroundTarget, currentRoute, previewChasing;
    private Vector3 surfaceNormal = Vector3.up;
    private int successfulReplans;
    private int corner;
    private float Now => Time.realtimeSinceStartup;
    internal string Status => $"type={kind}; music=({music.Status}); heartbeat={chaseAudio?.Bpm ?? 0:F0} BPM; chaseReplans={successfulReplans}; groundTarget={(hasGroundTarget ? groundTarget.ToString("F1") : "unavailable")}; lastReachable={lastReachable:F1}; recovery={(recovery.Active ? recovery.Remaining(Now).ToString("F2") : "none")}; routeReason={recovery.Reason}; teleport={capture?.Status ?? "none"}; presentation={(preview ? "preview/" + clip : id == "" ? "none" : phase.ToString())}; authorityLeaseAge={Now - leaseAt:F2}s; gaze={gaze:F2}/{settings.GazeSeconds:F2}s ({gazeReason}); gazeRange={settings.GazeDistance:F0}m; targetDistance={(Player.m_localPlayer && creature ? Vector3.Distance(Player.m_localPlayer.transform.position, position) : 0):F1}m";
    internal Presentation(Plugin plugin) { this.plugin = plugin; settings = plugin.Settings; }
    internal void Debug(string command)
    {
        if (!plugin.Assets.Ensure()) { plugin.Notice(plugin.Assets.Status); return; }
        if (!Player.m_localPlayer) return;
        if (command == "spawn")
        {
            Clear(); settings = plugin.Settings;
            var target = Player.m_localPlayer.transform.position;
            var direction = Vector3.ProjectOnPlane(Utils.GetMainCamera()?.transform.forward ?? Player.m_localPlayer.transform.forward, Vector3.up).normalized;
            bool found = false;
            for (int i = 0; i < 18; i++)
            {
                var candidate = target + Quaternion.Euler(0, i == 0 ? 0 : (i % 3 - 1) * 12, 0) * direction * (i == 0 ? 12 : UnityEngine.Random.Range(10, 15));
                if (World.Ground(candidate, out position)) { found = true; break; }
            }
            if (!found) { plugin.Notice("No safe preview ground 10-15m ahead. Move to open ground."); return; }
            preview = true; Create(); Play("idle"); plugin.Notice("Preview spawned. crawler_anim idle|scream|charge; crawler_clear.");
        }
        else if (command.StartsWith("anim "))
        {
            if (!preview || !creature) { plugin.Notice("Run crawler_spawn first."); return; }
            string requested = command.Substring(5);
            if (requested == "charge")
            {
                BeginChase(); previewChasing = true;
            }
            if (requested == "idle" || requested == "charge" || requested == "scream")
            {
                if (requested != "charge") previewChasing = false;
                Play(requested); phaseAt = Now;
                if (requested == "scream") Voice("attack");
                else if (requested == "idle" && sound) sound!.Stop();
            }
            else plugin.Notice("Usage: crawler_anim idle|scream|charge");
        }
    }
    internal void Prepare(string encounter, int seq, bool bypass, bool relocate, Vector3 previous, Settings config, EncounterKind encounterKind)
    {
        if (id != encounter) { Clear(); id = encounter; }
        else if (seq < sequence) return;
        kind = encounterKind; settings = config; sequence = seq; test = bypass; preview = false; relocation = relocate;
        oldPosition = previous; position = previous; phase = relocate ? Phase.Relocating : Phase.Preparing;
        searching = true; searchAt = nextSearch = leaseAt = Now; discoverySent = false;
        if (sound) sound!.Stop(); if (creature) creature!.SetActive(false);
    }
    internal void State(string encounter, int seq, Phase state, Vector3 point)
    {
        if (id != encounter || seq <= sequence) return;
        sequence = seq; phase = state; phaseAt = leaseAt = Now; gaze = 0; discoverySent = false; awaitingFinish = false;
        position = point;
        if (state == Phase.Lure || state == Phase.Tease) music.Begin(MusicSource);
        if (state == Phase.Relocating) { if (sound) sound!.Stop(); if (creature) creature!.SetActive(false); return; }
        searching = false;
        if (state == Phase.Caught)
        {
            if (Player.m_localPlayer.IsTeleporting()) { Fail("Target is already teleporting."); return; }
            HideVisual(); if (sound) sound.Stop(); chaseAudio?.Dispose(); chaseAudio = null;
            capture = new Capture(plugin, landingCandidate, p => plugin.Network.Landing(id, sequence, p)); return;
        }
        if (!creature) Create(); else { creature!.transform.position = point; plugin.Assets.UpdateOrigin(renderers, point.y); creature.SetActive(true); FaceTarget(); feet?.Release(); }
        if (state == Phase.Tease) Voice(settings.TeaseWhisper ? "whisper" : "woo");
        if (state == Phase.Lure) { Play("idle"); lureClip = UnityEngine.Random.value < .5f ? "whisper" : "woo"; Voice(lureClip); }
        if (state == Phase.Stare) { if (sound) sound!.Stop(); Play("idle"); FaceTarget(); }
        if (state == Phase.Watching) { Play("idle"); Voice("voice");  }
        if (state == Phase.Reveal) { Play("scream"); Voice("attack"); }
        if (state == Phase.Charge)
        {
            BeginChase(); Play("charge"); chaseAudio = new ChaseAudio(plugin.Assets);
            landingCandidate = Capture.FindCandidate(Player.m_localPlayer.transform.position, settings.Isolation);
        }
        if (state == Phase.Tail)
        {
            HideVisual();
        }
    }
    internal void Lease(string encounter, int seq) { if (id == encounter && seq == sequence) leaseAt = Now; }
    internal void Stop(string encounter, string reason) { if (id == encounter) { plugin.Log("Presentation stopped: " + reason); Clear(); } }
    private void Create()
    {
        if (kind == EncounterKind.Tease && !preview)
        {
            creature = new GameObject("ForestCrawler_LocalTease"); creature.transform.position = position;
            CreateSound(); return;
        }
        creature = plugin.Assets.Instantiate(position);
        renderers = creature.GetComponentsInChildren<Renderer>(true);
        FaceTarget(); animator = creature.GetComponentInChildren<Animator>(); animator!.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        feet = new FootSolver(creature);
        var sensor = new GameObject("CrawlerDetection"); sensor.layer = LayerMask.NameToLayer("Ignore Raycast"); sensor.transform.SetParent(creature.transform, false);
        detection = sensor.AddComponent<CapsuleCollider>(); detection.isTrigger = true; detection.center = Vector3.up * 1.25f; detection.height = 2.5f; detection.radius = .5f;
        gazeSensors.Add(detection);
        foreach (var bone in creature.GetComponentsInChildren<Transform>())
        {
            if (bone.name != "Head") continue;
            var headSensor = new GameObject("CrawlerHeadDetection"); headSensor.layer = sensor.layer;
            headSensor.transform.position = bone.position + Vector3.up * .06f;
            headSensor.transform.SetParent(bone, true);
            var head = headSensor.AddComponent<SphereCollider>(); head.isTrigger = true;
            head.radius = .24f / Mathf.Max(.001f, Mathf.Abs(headSensor.transform.lossyScale.x));
            gazeSensors.Add(head); break;
        }
        CreateSound();
    }
    private void CreateSound()
    {
        sound = creature!.AddComponent<AudioSource>(); sound.playOnAwake = false; sound.spatialBlend = 1; sound.dopplerLevel = 0;
        sound.minDistance = 5; sound.maxDistance = 130; sound.rolloffMode = AudioRolloffMode.Custom;
        sound.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(new Keyframe(0, 1), new Keyframe(.27f, .8f), new Keyframe(.7f, .4f), new Keyframe(1, 0)));
        sound.volume = .85f; sound.priority = 64;
    }
    private void Voice(string name)
    {
        if (!sound) return; sound!.Stop(); sound.clip = plugin.Assets.Audio[name]; sound.Play();
    }
    private void Play(string name)
    {
        clip = name; feet?.Release(); if (!animator) return;
        animator!.speed = 1; animator.CrossFadeInFixedTime(name, name == "charge" ? .12f : .2f); phaseAt = Now;
    }
    internal void Update()
    {
        music.Refresh(MusicSource);
        if (id == "" && !preview) return;
        var player = Player.m_localPlayer;
        if (!player || player.IsDead() || !ZNet.instance) { if (id != "") Fail("Target left or died."); else Clear(); return; }
        if (!preview)
        {
            if (Now - leaseAt > 1) { Fail("Authority lease expired."); return; }
            if (phase != Phase.Preparing && World.LocalIntruder(player.transform.position, position, settings.Isolation)) { Fail("Another living player entered isolation radius."); return; }
            // Global time and distant player state are authoritative; local biome/physics checks fail early.
            if (phase != Phase.Caught && !test && (!World.Allowed(player.transform.position) || (!searching && !World.Allowed(position)))) { Fail("Local biome requirement failed."); return; }
            if (Now >= nextPose) { nextPose = Now + .1f; plugin.Network.Pose(id, sequence, position); }
        }
        if (phase == Phase.Caught)
        {
            if (capture != null && !captureFinished && capture.Update())
            { captureFinished = true; plugin.Network.Finished(id, sequence); }
            return;
        }
        if (searching) { Search(); return; }
        if (!creature) return;
        if (phase == Phase.Stare) FaceTarget();
        if (!preview && (phase == Phase.Tail || phase == Phase.Tease))
        {
            if (!awaitingFinish && (!sound || !sound!.isPlaying)) { awaitingFinish = true; plugin.Network.Finished(id, sequence); }
            return;
        }
        if ((previewChasing || (!preview && phase == Phase.Charge)) && !awaitingFinish) Move();
        else if (preview && clip == "scream" && Now - phaseAt > plugin.Assets.RevealLength) Play("idle");
        if (preview) return;
        if (kind == EncounterKind.Full && phase == Phase.Lure && sound && !sound!.isPlaying)
        {
            lureClip = lureClip == "whisper" ? "woo" : "whisper";
            Voice(lureClip);
        }

    }
    private void Search()
    {
        if (Now - searchAt > 12) { Fail("No safe encounter location and complete route found: " + World.LastRouteFailure); return; }
        if (Now < nextSearch) return; nextSearch = Now + .15f;
        var player = Player.m_localPlayer; var center = player.transform.position; var camera = Utils.GetMainCamera();
        for (int i = 0; i < 6; i++)
        {
            float angle = UnityEngine.Random.Range(0, 360); float distance = UnityEngine.Random.Range(relocation ? settings.RelocateMin : settings.LureMin, relocation ? settings.RelocateMax : settings.LureMax);
            var candidate = center + Quaternion.Euler(0, angle, 0) * Vector3.forward * distance;
            if (!World.Ground(candidate, out var ground) || (!test && !World.Allowed(ground)) || World.LocalIntruder(center, ground, settings.Isolation)) continue;
            if (relocation)
            {
                if (Vector3.Angle(ground - center, oldPosition - center) < 90) continue;
                if (camera && GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), new Bounds(ground + Vector3.up * 1.25f, new Vector3(1.2f, 2.5f, 1.2f)))) continue;
            }
            else if (Now - searchAt < 6 && camera && !World.Obstructed(camera.transform.position, ground + Vector3.up * 1.2f)) continue;
            if (kind == EncounterKind.Tease) { route.Clear(); route.Add(ground); route.Add(center); }
            else if (!World.TargetGround(center,out var destination) || !World.ChargeRoute(ground, destination, test, 2, route)) continue;
            searching = false; plugin.Network.Candidate(id, sequence, ground, route); return;
        }
    }
    private void Discover()
    {
        if (!detection || !creature) return;
        var target = Player.m_localPlayer.transform.position; var center = position + Vector3.up * 1.2f;
        bool close = Vector3.Distance(target, position) <= settings.CloseDistance && !World.Obstructed(target + Vector3.up * 1.2f, center);
        var camera = Utils.GetMainCamera(); bool valid = false;
        if (camera)
        {
            valid = GazeProbe.Hit(camera, gazeSensors, settings.GazeDistance, World.Solids, Player.m_localPlayer.transform, out var point, out gazeReason);
            if (valid && !World.MistVisible(camera.transform.position, point, settings.MistDistance)) { valid = false; gazeReason = "Dense mist blocks gaze"; }
        }
        gaze = valid ? gaze + Time.unscaledDeltaTime : 0;
        if (!close && gaze < settings.GazeSeconds) return;
        discoverySent = true; plugin.Network.Discovery(id, sequence, close, gaze);
    }
    private void BeginChase()
    {
        route.Clear(); proposedRoute.Clear(); corner = 0; nextRoute = Now;
        previousTarget = Player.m_localPlayer.transform.position;
        travelled = speed = 0; currentRoute = hasGroundTarget = false;
        lastReachable = position; recovery.Reset(); feet?.Release();
    }
    private void Recovery(string reason)
    {
        currentRoute = false;
        if (recovery.Fail(Now, reason)) plugin.Log("Chase recovery started: " + reason);
    }
    private void Recovered()
    {
        if (recovery.Active) plugin.Log("Chase route recovered with validated movement or grounded arrival.");
        recovery.Reset();
    }
    private void Hold(Vector3 target)
    {
        speed = 0; previousTarget = target;
        if (clip != "idle") Play("idle");
    }
    private bool CatchClear(Vector3 at, Vector3 target) =>
        !World.Obstructed(at + surfaceNormal * 1.2f, target + Vector3.up * 1.2f);
    private void Move()
    {
        if (!creature || !animator || Time.deltaTime <= 0) return;
        var target = Player.m_localPlayer.transform.position;
        float stop = preview ? settings.StopDistance : 2.5f;
        chaseAudio?.Update(Vector3.Distance(position, target));
        if (!preview) Capture.Warm(landingCandidate);
        if (Vector3.Distance(position, target) <= stop && CatchClear(position, target)) { Finish(); return; }
        // A hitch cannot translate the creature across unchecked geometry.
        if (Time.unscaledDeltaTime > .2f) { nextRoute = Now; Hold(target); return; }
        hasGroundTarget = World.TargetGround(target, out groundTarget);
        bool near = Vector3.Distance(position, target) <= 8;
        bool reroute = recovery.Active || !currentRoute || corner >= route.Count ||
            !hasGroundTarget || Vector3.Distance(groundTarget, routedTarget) > (near ? .35f : 1.5f);
        if (reroute && Now >= nextRoute)
        {
            nextRoute = Now + (near ? .1f : .25f);
            if (hasGroundTarget && World.ChargeRoute(position, groundTarget, test || preview, Mathf.Max(.5f, stop-.5f), proposedRoute))
            {
                route.Clear(); route.AddRange(proposedRoute); corner = 0; routedTarget = groundTarget;
                lastReachable = route[route.Count-1]; currentRoute = true; successfulReplans++;
                // Query success alone does not clear a blocked-movement deadline.
            }
            else Recovery(hasGroundTarget ? World.LastRouteFailure : "No supporting ground below target");
        }
        bool arrived = hasGroundTarget && Vector3.Distance(position, groundTarget) <= Mathf.Max(.5f, stop-.5f) &&
            CatchClear(position, groundTarget) && World.ChaseSegment(position, groundTarget);
        if (arrived && currentRoute) Recovered();
        if (recovery.Expired(Now))
        {
            string reason = "Chase cancelled after five seconds without a usable current route: " + recovery.Reason;
            if (preview) { previewChasing = false; Hold(target); plugin.Notice(reason); } else Fail(reason);
            return;
        }
        if (arrived) { Hold(target); return; }
        while (corner < route.Count && Vector3.Distance(position, route[corner]) < .01f) corner++;
        if (corner >= route.Count) { Recovery("Last reachable destination reached; awaiting current target route"); Hold(target); return; }
        float targetSpeed = Vector3.ProjectOnPlane(target - previousTarget, Vector3.up).magnitude / Mathf.Max(.001f, Time.deltaTime);
        speed = Mathf.MoveTowards(speed, preview ? settings.Speed : (float)Rules.ChaseSpeed(targetSpeed), Time.deltaTime * 30);
        float remaining = speed * Time.deltaTime;
        var start = position;
        bool moved = false;
        // Consume dense waypoints with one distance budget, never skipping intervening terrain.
        while (remaining > .00001f && corner < route.Count)
        {
            var desired = Vector3.MoveTowards(position, route[corner], remaining);
            bool support = World.ChaseGround(desired, out var next, out var normal);
            if (!support || !World.ChaseSegment(position, next) || (!preview && !test && !World.Allowed(next)))
            {
                Recovery("Current segment blocked: " + Traversal.LastFailure + $"; from={position:F3}; next={next:F3}; target={target:F3}");
                nextRoute = Mathf.Min(nextRoute, Now + .1f); feet?.Release(); break;
            }
            float distance = Vector3.Distance(position, next);
            if (distance < .000001f)
            {
                if (Vector3.Distance(position, route[corner]) < .01f ||
                    (World.ChaseGround(route[corner],out var refreshedCorner) && Vector3.Distance(position,refreshedCorner)<.01f))
                { corner++; continue; }
                Recovery($"No progress on current segment; from={position:F3}; waypoint={route[corner]:F3}; groundTarget={groundTarget:F3}"); break;
            }
            // Projection can move a point farther than the budget on abrupt terrain transitions.
            if (distance > remaining + .01f) { Recovery("Support projection exceeds movement budget"); break; }
            position = next; surfaceNormal = normal; travelled += distance; remaining = Mathf.Max(0, remaining-distance); moved = true;
            if (Vector3.Distance(position, route[corner]) < .01f) corner++;
        }
        if (!moved) { Hold(target); return; }
        if (currentRoute) Recovered();
        var from = start - previousTarget; var to = position - target;
        // Swept detection is also bounded by actual current distance for authority validation.
        if (Rules.SweptDistanceSquared(from.x, from.y, from.z, to.x, to.y, to.z) <= stop * stop &&
            Vector3.Distance(position, target) <= 3.25f && CatchClear(position, target)) { Finish(); return; }
        if (clip != "charge") Play("charge");
        creature.transform.position = position; plugin.Assets.UpdateOrigin(renderers, position.y);
        var direction = Vector3.ProjectOnPlane(position - start, surfaceNormal);
        if (direction.sqrMagnitude > .0001f) creature.transform.rotation = Quaternion.LookRotation(direction, surfaceNormal);
        animator.Play("charge", 0, Mathf.Repeat(travelled / 5.6f, 1)); animator.speed = 0; animator.Update(0);
        previousTarget = target;
    }
    private void Finish()
    {
        if (preview) { previewChasing = false; Play("idle"); return; }
        awaitingFinish = true;
        HideVisual();
        plugin.Network.Pose(id, sequence, position); plugin.Network.Finished(id, sequence);
    }
    private void FaceTarget()
    {
        if (!creature || !Player.m_localPlayer) return;
        var direction = Vector3.ProjectOnPlane(Player.m_localPlayer.transform.position - position, Vector3.up);
        if (direction.sqrMagnitude > .001f) creature!.transform.rotation = Quaternion.LookRotation(direction);
    }
    private void HideVisual()
    {
        foreach (var renderer in renderers) if (renderer) renderer.enabled = false;
        foreach (var sensor in gazeSensors) if (sensor) sensor.enabled = false;
    }
    internal void LateUpdate()
    {
        if (creature && creature!.activeSelf && (preview || (phase != Phase.Tail && phase != Phase.Caught))) feet?.Solve(clip == "charge", Mathf.Repeat(travelled / 5.6f, 1), Time.deltaTime);
        if (!preview && !searching && !discoverySent && id != "" && Rules.CanDiscover(phase, Now - phaseAt, settings.VoiceLength))
        {
            Physics.SyncTransforms();
            Discover();
        }
    }
    internal void Permit(string encounter, int seq, Vector3 point) { if (id == encounter && seq == sequence && phase == Phase.Caught) capture?.Permit(point); }
    private void Fail(string reason) { string encounter = id; Clear(); if (encounter != "") plugin.Network.Failure(encounter, reason); plugin.Log(reason); }
    internal void Clear()
    {
        music.Clear();
        Capture.ClearWarm();
        (capture ?? Capture.Current)?.Dispose(); capture = null; captureFinished = false; landingCandidate = null;
        chaseAudio?.Dispose(); chaseAudio = null;
        if (sound) sound!.Stop();
        if (creature)
        {
            foreach (var r in renderers) if (r) foreach (var m in r.materials) UnityEngine.Object.Destroy(m);
            UnityEngine.Object.Destroy(creature);
        }
        creature = null; animator = null; sound = null; detection = null; feet = null;
        renderers = Array.Empty<Renderer>();
        gazeSensors.Clear(); gazeReason = "Not evaluated";
        id = ""; sequence = 0; preview = searching = discoverySent = awaitingFinish = false; clip = "idle"; route.Clear(); proposedRoute.Clear(); recovery.Reset(); currentRoute = hasGroundTarget = previewChasing = false; surfaceNormal = Vector3.up; successfulReplans = 0; gaze = travelled = speed = 0;
    }
}
