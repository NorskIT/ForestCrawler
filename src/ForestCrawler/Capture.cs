using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ForestCrawler;

// A bounded target-client transaction. The origin stays loaded until commit or rollback.
// No global teleport timing, god-mode setting or saved player state is changed.
internal sealed class Capture : IDisposable
{
    private static readonly Harmony patches = new(Plugin.Id + ".capture");
    internal static Capture? Current;
    private static Vector3? warming;
    private static float warmAt;
    private static int warmIndex;
    internal static string GroundFailure = "No query";
    internal static void Warm(Vector3? destination)
    {
        warming = destination;
        if (!destination.HasValue || !ZoneSystem.instance || Time.realtimeSinceStartup < warmAt) return;
        warmAt = Time.realtimeSinceStartup + .08f;
        int i = warmIndex++ % 9;
        var point = destination.Value + new Vector3((i % 3 - 1) * 64, 0, (i / 3 - 1) * 64);
        AccessTools.Method(typeof(ZoneSystem), "PokeLocalZone").Invoke(ZoneSystem.instance, new object[] { ZoneSystem.GetZone(point) });
        var nearby = new List<ZDO>();
        ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(destination.Value), new SimulationDistance(1, 0), nearby);
        AccessTools.Method(typeof(ZNetScene), "CreateObjects").Invoke(ZNetScene.instance, new object[] { nearby, new List<ZDO>() });
    }
    internal static void ClearWarm() { warming = null; warmIndex = 0; warmAt = 0; }
    private readonly Plugin plugin;
    private readonly Player player;
    private readonly Rigidbody body;
    private readonly RigidbodyConstraints constraints;
    private readonly Vector3 origin;
    private readonly Quaternion rotation;
    private readonly float started;
    private readonly Action<Vector3> request;
    private readonly GameObject root, model;
    private readonly Camera camera;
    private readonly RenderTexture texture;
    private readonly SkinnedMeshRenderer face;
    private readonly Transform head;
    private readonly AudioSource scream, tail;
    private readonly Animator animator;
    private Vector3? candidate, approved;
    private bool requested, committed, completed, disposed;
    internal string Status { get; private set; } = "Loading destination; origin retained";
    internal static bool Active => Current != null && !Current.disposed;
    internal static void Install()
    {
        void Patch(Type type, string method, string prefix) => patches.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(Capture), prefix));
        Patch(typeof(Player), "TakeInput", nameof(Input));
        Patch(typeof(Player), "SetControls", nameof(Controls));
        Patch(typeof(Character), "RPC_Damage", nameof(Damage));
        Patch(typeof(Character), "ApplyDamage", nameof(Damage));
        Patch(typeof(Player), "TeleportTo", nameof(OtherTeleport));
        Patch(typeof(ZNet), "GetReferencePosition", nameof(Reference));
        Patch(typeof(ZNet), "SetReferencePosition", nameof(SetReference));
        Patch(typeof(ZNetScene), "RemoveObjects", nameof(Retain));
        Patch(typeof(ZoneSystem), "UpdateTTL", nameof(Retain));
        Patch(typeof(ZNetScene), "InLoadingScreen", nameof(Loading));
    }
    internal static void Uninstall() => patches.UnpatchSelf();
    private static bool Input(Player __instance, ref bool __result)
    { if (!Active || __instance != Current!.player) return true; __result = false; return false; }
    private static bool Damage(Character __instance) => !Active || __instance != Current!.player;
    private static bool OtherTeleport(Player __instance, ref bool __result)
    { if (!Active || __instance != Current!.player) return true; __result = false; return false; }
    private static bool Reference(ref Vector3 __result)
    { if (!Active || !Current!.candidate.HasValue) return true; __result = Current.candidate.Value; return false; }
    private static void SetReference(ref Vector3 pos) { if (Active && Current!.candidate.HasValue) pos = Current.candidate.Value; }
    private static bool Retain() => !Active && !warming.HasValue;
    private static bool Loading(ref bool __result)
    { if (!Active) return true; __result = true; return false; }
    private static void Controls(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold,
        ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold,
        ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
    {
        if (!Active || __instance != Current!.player) return;
        movedir = Vector3.zero; attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = jump = crouch = run = autoRun = dodge = false;
    }
    internal Capture(Plugin plugin, Vector3? destination, Action<Vector3> request)
    {
        this.plugin = plugin; this.request = request; player = Player.m_localPlayer;
        origin = player.transform.position; rotation = player.transform.rotation; started = Time.realtimeSinceStartup;
        candidate = destination.HasValue && Horizontal(destination.Value, origin) >= 200 && Horizontal(destination.Value, origin) <= 500
            ? destination : FindCandidate(origin, plugin.Settings.Isolation);
        body = player.GetComponent<Rigidbody>(); constraints = body.constraints;
        root = new GameObject("ForestCrawler_Capture"); root.transform.position = new Vector3(0, 10000, 0);
        model = UnityEngine.Object.Instantiate(plugin.Assets.CloseupPrefab, root.transform);
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
        face = model.GetComponentInChildren<SkinnedMeshRenderer>(); head = model.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
        animator = model.GetComponent<Animator>(); animator.Play("idle", 0, 0); animator.speed = 0; animator.updateMode = AnimatorUpdateMode.UnscaledTime; animator.Update(0);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            foreach (var material in renderer.materials) { material.shader = plugin.Assets.CloseupShader; material.SetFloat("_CrawlerOriginY", 10000); }
        }
        var cameraObject = new GameObject("CaptureCamera"); cameraObject.transform.SetParent(root.transform, false);
        camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.nearClipPlane = .02f; camera.farClipPlane = 5; camera.fieldOfView = 40;
        camera.transform.localPosition = new Vector3(0, 2.18f, .85f); camera.transform.localRotation = Quaternion.Euler(0, 180, 0);
        texture = new RenderTexture(1280, 720, 24); texture.Create(); camera.targetTexture = texture;
        var light = cameraObject.AddComponent<Light>(); light.type = LightType.Point; light.range = 5; light.intensity = 3; light.cullingMask = 1 << 31;
        scream = ChaseAudio.Source(root, plugin.Assets.Audio[UnityEngine.Random.value < .5f ? "caught1" : "caught2"]); scream.volume = .72f;
        tail = ChaseAudio.Source(root, plugin.Assets.Audio["attack"]); tail.volume = .55f;
        double now = AudioSettings.dspTime; scream.PlayScheduled(now + .02); tail.PlayScheduled(now + .02 + scream.clip.length);
        Current = this; body.constraints = RigidbodyConstraints.FreezeAll;
        var attack = AccessTools.Field(typeof(Humanoid), "m_currentAttack"); (attack.GetValue(player) as Attack)?.Stop(); attack.SetValue(player, null);
        player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
    }
    internal static float Horizontal(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
    internal static Vector3? FindCandidate(Vector3 origin, float isolation)
    {
        if (WorldGenerator.instance == null || !ZoneSystem.instance) return null;
        Vector3? fallback = null;
        for (int i = 0; i < 128; i++)
        {
            var point = origin + Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0) * Vector3.forward * UnityEngine.Random.Range(220, 470);
            point.y = WorldGenerator.instance.GetHeight(point.x, point.z, out _);
            if (point.y < ZoneSystem.instance.m_waterLevel + 3 || ZoneSystem.IsLavaPreHeightmap(point)) continue;
            bool slope = false;
            foreach (var offset in new[] { Vector3.right * 3, Vector3.left * 3, Vector3.forward * 3, Vector3.back * 3 })
                if (Mathf.Abs(WorldGenerator.instance.GetHeight(point.x + offset.x, point.z + offset.z, out _) - point.y) > 1.2f) slope = true;
            if (!slope && !World.LocalIntruder(origin, point, isolation))
            {
                if (SafeGround(point, out var ground)) return ground;
                fallback ??= point;
            }
        }
        return fallback;
    }
    internal static bool SafeGround(Vector3 candidate, out Vector3 ground)
    {
        ground = candidate;
        if (!ZNetScene.instance || !ZNetScene.instance.IsAreaReady(candidate)) { GroundFailure = "Area streaming"; return false; }
        GroundFailure = "Terrain, clearance, liquid or slope";
        if (!Physics.Raycast(candidate + Vector3.up * 20, Vector3.down, out var hit, 45, World.Solids, QueryTriggerInteraction.Ignore) ||
            !hit.collider.GetComponent<Heightmap>() || Vector3.Angle(hit.normal, Vector3.up) > 25) return false;
        ground = hit.point;
        if (ground.y < ZoneSystem.instance.m_waterLevel + 1 || ZoneSystem.instance.IsLava(ground, true) ||
            Physics.CheckCapsule(ground + Vector3.up * .65f, ground + Vector3.up * 1.8f, .5f, World.Solids, QueryTriggerInteraction.Ignore) ||
            Physics.Raycast(ground + Vector3.up * .6f, Vector3.up, 10, World.Solids, QueryTriggerInteraction.Ignore)) return false;
        // Water, tar and other liquid triggers can sit above the global sea level.
        foreach (var collider in Physics.OverlapSphere(ground + Vector3.up * .3f, 1.1f, ~0, QueryTriggerInteraction.Collide))
        {
            if (collider.GetComponentInParent<LiquidVolume>() || collider.GetComponentInParent<WaterVolume>()) return false;
            if (collider.GetComponentInParent<Piece>()) return false;
        }
        return true;
    }
    internal void Permit(Vector3 point)
    { if (!requested || approved.HasValue || !candidate.HasValue || Horizontal(point, candidate.Value) > 8) return; approved = point; Status = "Server permitted; awaiting four-second reveal"; }
    internal bool Update()
    {
        if (disposed) return true;
        float elapsed = Time.realtimeSinceStartup - started;
        if (!committed) { player.transform.SetPositionAndRotation(origin, rotation); body.position = origin; body.linearVelocity = Vector3.zero; }
        if (candidate.HasValue && !requested && elapsed < 4.6f)
        {
            Warm(candidate);
            for (int i = 0; i < 25; i++)
            {
                var point = candidate.Value + new Vector3((i % 5 - 2) * 2, 0, (i / 5 - 2) * 2);
                if (Horizontal(point, origin) < 200 || Horizontal(point, origin) > 500) continue;
                if (!SafeGround(point, out var ground) || World.LocalIntruder(origin, ground, plugin.Settings.Isolation)) continue;
                candidate = ground; requested = true; Status = "Dry ground ready; requesting authority"; request(ground); break;
            }
        }
        if (elapsed >= 4 && approved.HasValue && SafeGround(approved.Value, out var landing) &&
            Vector3.Distance(landing, approved.Value) < .1f && !World.LocalIntruder(origin, landing, plugin.Settings.Isolation))
        {
            MovePlayer(landing + Vector3.up * .15f); committed = completed = true; Status = "Committed to loaded dry ground"; return true;
        }
        if (elapsed >= 5) { completed = true; Status = "No safe destination before deadline; origin retained"; plugin.Log(Status + "; " + GroundFailure + "; requested=" + requested + "; approved=" + approved.HasValue); return true; }
        return false;
    }
    private void MovePlayer(Vector3 point)
    {
        player.transform.SetPositionAndRotation(point, rotation); body.position = point; body.linearVelocity = Vector3.zero;
        AccessTools.Field(typeof(Character), "m_maxAirAltitude").SetValue(player, point.y);
        AccessTools.Method(typeof(Character), "InvalidateCachedLiquidDepth").Invoke(player, null);
        if (ZNet.instance) ZNet.instance.SetReferencePosition(point); if (EnvMan.instance) EnvMan.instance.ForceInstantEnvironmentSwitch(); player.ResetCloth();
    }
    internal void Render()
    {
        if (disposed || completed) return;
        float t = Time.realtimeSinceStartup - started;
        for (int i = 0; i < face.sharedMesh.blendShapeCount; i++)
        {
            string name = face.sharedMesh.GetBlendShapeName(i);
            float weight = name.Contains("Jaw") ? 65 + 35 * Mathf.Sin(t * 13) : name.Contains("Eye") ? 50 + 50 * Mathf.Sin(t * 19 + 1) : 80 * Mathf.Sin(t * 11);
            face.SetBlendShapeWeight(i, weight);
        }
        animator.Update(0);
        head.localRotation *= Quaternion.Euler(Mathf.Sin(t * 53) * 3, Mathf.Sin(t * 39) * 5, Mathf.Sin(t * 47) * 7);
        camera.Render();
    }
    internal static void Draw()
    {
        if (!Active || Current!.completed || (Menu.instance && Menu.IsVisible()) || (global::Console.instance && global::Console.IsVisible())) return;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Current.texture, ScaleMode.ScaleAndCrop, false);
    }
    public void Dispose()
    {
        if (disposed) return;
        try
        {
            if (scream) scream.Stop(); if (tail) tail.Stop();
            if (player && body && !committed && !player.IsDead() && ZNet.instance && ZNetScene.instance)
                MovePlayer(origin);
        }
        catch (Exception error) { plugin.Log("Capture rollback interrupted during cleanup: " + error.Message); }
        finally
        {
            if (body) body.constraints = constraints;
            disposed = true; ClearWarm(); if (Current == this) Current = null;
            if (ZNet.instance && player) ZNet.instance.SetReferencePosition(player.transform.position);
            if (camera) camera.targetTexture = null;
            if (texture) { texture.Release(); UnityEngine.Object.Destroy(texture); }
            if (model) foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                foreach (var material in renderer.materials) UnityEngine.Object.Destroy(material);
            if (root) UnityEngine.Object.Destroy(root);
        }
    }
}
