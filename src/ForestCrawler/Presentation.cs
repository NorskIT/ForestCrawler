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
    private EncounterScreen? screen;
    private bool grabVoice;
    internal void ShowCue(string encounter, Cue cue) { if (id == encounter && !preview) screen?.Show(cue); }
    private Vector3? landingCandidate;
    private bool captureFinished;
    private string id = "", clip = "idle";
    private int sequence;
    private Phase phase;
    private Settings settings;
    private bool preview, test, searching, relocation, discoverySent, awaitingFinish;
    private float leaseAt, phaseAt, searchAt, nextSearch, nextPose, gaze, travelled;
    private string lureClip = "whisper";
    private Vector3 position, oldPosition, previousTarget;
    private readonly List<Vector3> route = new();
    private NativePursuit? pursuit;
    private ExtendedArms? arms;
    private ArmGrab? pull;
    private bool previewChasing, grabRequested;
    private float unsuccessfulSince, nextGrab, progressAt, finishAt, nextFinishReport;
    private Vector3 progressPosition;
    private string grabReason = "Not chasing";
    private float Now => Time.realtimeSinceStartup;
    internal string Status => $"type={kind}; {screen?.Status ?? "screen=off"}; music=({music.Status}); heartbeat={chaseAudio?.Bpm ?? 0:F0} BPM; pursuit=({pursuit?.Status ?? "none"}); unsuccessful={Now-unsuccessfulSince:F1}s; grab={grabReason}; teleport={capture?.Status ?? "none"}; presentation={(preview ? "preview/" + clip : id == "" ? "none" : phase.ToString())}; authorityLeaseAge={Now-leaseAt:F2}s; gaze={gaze:F2}/{settings.GazeSeconds:F2}s ({gazeReason})";
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
                if (requested != "charge") { previewChasing = false; pursuit?.Pause(true); }
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
        if (state == Phase.Lure || state == Phase.Tease)
        { music.Begin(MusicSource); screen ??= new EncounterScreen(plugin, kind == EncounterKind.Tease); }
        screen?.SetPhase(state);
        if (state == Phase.Relocating) { if (sound) sound!.Stop(); if (creature) creature!.SetActive(false); return; }
        searching = false;
        if (state == Phase.Caught)
        {
            if (Player.m_localPlayer.IsTeleporting()) { Fail("Target is already teleporting."); return; }
            pull?.Dispose(); pull = null; arms?.Restore(); pursuit?.Dispose(); pursuit = null;
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
            if (pursuit == null) BeginChase(); else { pull?.Dispose(); pull = null; arms?.Restore(); pursuit.Pause(false); nextGrab = Now + 3; grabRequested = false; }
            Play("charge"); chaseAudio ??= new ChaseAudio(plugin.Assets);
            landingCandidate = Capture.FindCandidate(Player.m_localPlayer.transform.position, settings.Isolation);
        }
        if (state == Phase.GrabWindup)
        {
            grabRequested = false; pursuit?.Pause(true); Play("scream"); FaceTarget();
            if (!grabVoice) { Voice("voice"); grabVoice = true; }
            arms ??= new ExtendedArms(creature!); grabReason = "Extending";
        }
        if (state == Phase.Pulling)
        { pull = new ArmGrab(position, settings.PullSpeed); grabRequested = false; grabReason = "Pulling"; }
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
        arms?.Restore();
        music.Refresh(MusicSource);
        screen?.Update();
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
        if (phase == Phase.Stare || phase == Phase.GrabWindup || phase == Phase.Pulling) FaceTarget();
        if (awaitingFinish && phase == Phase.Charge)
        {
            if (Now-finishAt > .4f || Vector3.Distance(position,player.transform.position)>3.25f)
            { awaitingFinish = false; pursuit?.Pause(false); }
            else if (Now >= nextFinishReport)
            { nextFinishReport = Now+.1f; plugin.Network.Pose(id,sequence,position); plugin.Network.Finished(id,sequence); }
        }
        if (!preview && (phase == Phase.Tail || phase == Phase.Tease))
        {
            if (!awaitingFinish && (!sound || !sound!.isPlaying)) { awaitingFinish = true; plugin.Network.Finished(id, sequence); }
            return;
        }
        if (phase == Phase.GrabWindup || phase == Phase.Pulling)
        {
            chaseAudio?.Update(Vector3.Distance(position, player.transform.position));
            if (phase == Phase.GrabWindup && !grabRequested)
            {
                if (!ArmGrab.Visible(position, player, settings.GrabRange)) { grabRequested = true; plugin.Network.Grab(id, sequence, 2); }
                else if (Now-phaseAt >= settings.GrabExtension) { grabRequested = true; plugin.Network.Grab(id, sequence, 1); }
            }
            if (phase == Phase.Pulling && pull != null)
            {
                if (pull.Blocked && !grabRequested) { pull.Dispose(); pull = null; grabRequested = true; plugin.Network.Grab(id, sequence, 2); }
                else if (pull.Arrived && Now >= nextFinishReport)
                { nextFinishReport = Now+.1f; plugin.Network.Finished(id, sequence); }
            }
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
        if (Now - searchAt > 12) { Fail("No safe encounter location found: " + World.LastRouteFailure); return; }
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
            else if (!World.Route(ground, center, test, route)) continue;
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
        pursuit?.Dispose(); pursuit = new NativePursuit(position, Player.m_localPlayer);
        previousTarget = Player.m_localPlayer.transform.position; travelled = 0;
        unsuccessfulSince = progressAt = Now; progressPosition = position;
        grabRequested = false; nextGrab = Now; feet?.Release();
    }
    private bool CatchClear(Vector3 at, Vector3 target) => !World.Obstructed(at + Vector3.up * 1.2f, target + Vector3.up * 1.2f);
    private void Move()
    {
        if (!creature || !animator || pursuit == null || Time.deltaTime <= 0) return;
        var target = Player.m_localPlayer.transform.position;
        var start = position; position = pursuit.Position;
        float targetSpeed = Vector3.ProjectOnPlane(target - previousTarget, Vector3.up).magnitude / Mathf.Max(.001f, Time.deltaTime);
        pursuit.Tick(preview ? settings.Speed : (float)Rules.ChaseSpeed(targetSpeed));
        creature.transform.SetPositionAndRotation(position, pursuit.Root.transform.rotation);
        plugin.Assets.UpdateOrigin(renderers, position.y);
        chaseAudio?.Update(Vector3.Distance(position, target));
        if (!preview) Capture.Warm(landingCandidate);
        float stop = preview ? settings.StopDistance : 2.5f;
        var from = start-previousTarget; var to = position-target;
        if (Rules.SweptDistanceSquared(from.x, from.y, from.z, to.x, to.y, to.z) <= stop*stop &&
            Vector3.Distance(position, target) <= 3.25f && CatchClear(position, target)) { Finish(); return; }
        if (Now-progressAt >= .5f)
        {
            bool progress = pursuit.HasReachablePath(target) && Vector3.Dot(position-progressPosition, (target-progressPosition).normalized) >= .15f;
            if (progress) { unsuccessfulSince = Now; if (!preview) plugin.Network.Progress(id, sequence); }
            progressAt = Now; progressPosition = position;
        }
        float unsuccessful = Now-unsuccessfulSince;
        grabReason = unsuccessful < settings.GrabDelay ? "Waiting for unsuccessful pursuit delay" :
            !ArmGrab.Visible(position, Player.m_localPlayer, settings.GrabRange) ? "Target out of range or obstructed" : "Ready";
        if (!preview && !grabRequested && Now >= nextGrab && unsuccessful >= settings.GrabDelay &&
            ArmGrab.Visible(position, Player.m_localPlayer, settings.GrabRange))
        { grabRequested = true; plugin.Network.Pose(id, sequence, position); plugin.Network.Grab(id, sequence, 0); }
        if (preview && unsuccessful >= settings.PursuitTimeout) { pursuit.Pause(true); previewChasing = false; Play("idle"); return; }
        float movement = Vector3.Distance(start, position); travelled += movement;
        if (pursuit.Speed > .15f)
        {
            if (clip != "charge") Play("charge");
            animator.Play("charge", 0, Mathf.Repeat(travelled / 5.6f, 1)); animator.speed = 0; animator.Update(0);
        }
        else if (clip != "idle") Play("idle");
        previousTarget = target;
    }
    internal void FixedUpdate() => pull?.FixedUpdate();
    private void Finish()
    {
        pursuit?.Pause(true);
        if (preview) { previewChasing = false; Play("idle"); return; }
        // Visibility changes only on the server's Caught transition. A delayed or
        // rejected contact report must not leave an invisible, stalled encounter.
        awaitingFinish = true; finishAt = Now; nextFinishReport = Now+.1f;
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
        if (arms != null && (phase == Phase.GrabWindup || phase == Phase.Pulling) && Player.m_localPlayer)
            arms.Pose(Player.m_localPlayer.GetCenterPoint(), phase == Phase.Pulling ? 1 : Mathf.Clamp01((Now-phaseAt)/settings.GrabExtension));
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
        pull?.Dispose(); pull = null; arms?.Dispose(); arms = null; pursuit?.Dispose(); pursuit = null;
        music.Clear(); screen?.Dispose(); screen = null; grabVoice = false;
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
        id = ""; sequence = 0; preview = searching = discoverySent = awaitingFinish = previewChasing = grabRequested = false;
        phase = Phase.Preparing; kind = EncounterKind.Full;
        clip = "idle"; route.Clear(); gaze = travelled = 0;
    }
}
