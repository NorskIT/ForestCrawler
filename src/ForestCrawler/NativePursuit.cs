using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ForestCrawler;

// Marker is installed while the clone is inactive, before any native Awake runs.
internal sealed class LocalCrawlerDriver : MonoBehaviour
{
    internal ZDO? State;
    internal int AiTicks, MotorTicks;
    internal Player Target = null!;
    private void OnDestroy() { if (State != null) { NativePursuit.Release(State); State = null; } }
}

internal sealed class NativePursuit : IDisposable
{
    private static readonly Harmony patches = new(Plugin.Id + ".native");
    private static readonly HashSet<ZDO> detached = new();
    private static readonly long localNamespace = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
    private static uint serial;
    internal readonly GameObject Root;
    internal readonly MonsterAI AI;
    internal readonly Humanoid Character;
    private readonly Rigidbody body;
    private readonly RigidbodyConstraints constraints;
    private readonly Renderer[] hidden;
    private readonly Collider[] colliders;
    private float nextCollisions;
    internal Vector3 Position => body.position;
    internal float Speed => Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
    internal bool Running { get; private set; }
    internal string Status => $"native MonsterAI/Character; path={Read<bool>(AI, "m_lastFindPathResult")}; corners={Read<List<Vector3>>(AI, "m_path").Count}; target={AI.GetTargetCreature()?.name ?? "searching"}; ticks={Marker(AI)!.AiTicks}/{Marker(AI)!.MotorTicks}; velocity={body.linearVelocity:F2}; weapon={Character.GetCurrentWeapon()?.m_shared.m_name ?? "none"}";
    internal static T Read<T>(object obj, string field) => (T)AccessTools.Field(obj.GetType(), field).GetValue(obj);
    internal static void Write(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    internal static LocalCrawlerDriver? Marker(Component obj) => obj ? obj.GetComponent<LocalCrawlerDriver>() : null;
    private static bool Local(Component obj) => Marker(obj) != null;

    internal static void Install()
    {
        void Patch(Type type, string method, string handler, Type[]? args = null) => patches.Patch(
            AccessTools.Method(type, method, args) ?? throw new MissingMethodException(type.Name, method),
            prefix: new HarmonyMethod(typeof(NativePursuit), handler));
        Patch(typeof(MonsterAI), "UpdateAI", nameof(AiTick));
        Patch(typeof(Character), "UpdateMotion", nameof(MotorTick));
        Patch(typeof(ZNetView), "Awake", nameof(ViewAwake));
        Patch(typeof(ZNetView), "InvokeRPC", nameof(LocalRpc), new[] { typeof(long), typeof(string), typeof(object[]) });
        Patch(typeof(ZNetView), "InvokeRPC", nameof(LocalRpc), new[] { typeof(string), typeof(object[]) });
        Patch(typeof(ZDO), "SetSector", nameof(Sector));
        Patch(typeof(ZDO), "IncreaseDataRevision", nameof(DataRevision));
        Patch(typeof(ZDO), "IncreaseOwnerRevision", nameof(OwnerRevision));
        patches.Patch(AccessTools.Method(typeof(BaseAI), "FindEnemy"), postfix: new HarmonyMethod(typeof(NativePursuit), nameof(SelectedTarget)));
        Patch(typeof(BaseAI), "IsEnemy", nameof(Enemy), new[] { typeof(Character), typeof(Character) });
        Patch(typeof(Humanoid), "StartAttack", nameof(Attack));
        Patch(typeof(Character), "ApplyDamage", nameof(NoDamage));
        Patch(typeof(Character), "RPC_Damage", nameof(NoDamage));
        Patch(typeof(Character), "AddNoise", nameof(NoDamage));
        Patch(typeof(Character), "SetVisible", nameof(NoDamage));
        Patch(typeof(EnemyHud), "ShowHud", nameof(Hud));
    }
    private static void AiTick(MonsterAI __instance) { var marker = Marker(__instance); if (marker) marker!.AiTicks++; }
    private static void MotorTick(Character __instance) { var marker = Marker(__instance); if (marker) marker!.MotorTicks++; }
    internal static void Uninstall() => patches.UnpatchSelf();
    private static bool ViewAwake(ZNetView __instance)
    {
        var marker = Marker(__instance); if (!marker) return true;
        var state = new ZDO { m_uid = new ZDOID(localNamespace, ++serial) };
        detached.Add(state); marker!.State = state;
        state.Init(); state.SetPrefab("ForestCrawler_LocalDriver".GetStableHashCode());
        state.SetOwner(ZDOMan.GetSessionID()); state.SetPosition(__instance.transform.position);
        state.SetRotation(__instance.transform.rotation); state.Persistent = false;
        Write(__instance, "m_zdo", state); Write(__instance, "m_body", __instance.GetComponent<Rigidbody>());
        return false; // Never create a managed ZDO or register a ZNetScene instance.
    }
    private static bool LocalRpc(ZNetView __instance, string method, object[] parameters)
    {
        if (!Local(__instance)) return true;
        var functions = Read<System.Collections.IDictionary>(__instance, "m_functions");
        var callback = functions[method.GetStableHashCode()];
        if (callback != null)
        {
            var packet = new ZPackage(); ZRpc.Serialize(parameters, ref packet); packet.SetPos(0);
            AccessTools.Method(callback.GetType(), "Invoke").Invoke(callback, new object[] { ZNet.GetUID(), packet });
        }
        return false;
    }
    private static bool Sector(ZDO __instance) => !detached.Contains(__instance);
    private static bool DataRevision(ZDO __instance)
    { if (!detached.Contains(__instance)) return true; __instance.DataRevision++; return false; }
    private static bool OwnerRevision(ZDO __instance)
    { if (!detached.Contains(__instance)) return true; __instance.OwnerRevision++; return false; }
    internal static void Release(ZDO state) { if (detached.Remove(state)) state.Reset(); }
    private static bool Enemy(Character a, Character b, ref bool __result)
    {
        var source = Marker(a); var target = Marker(b);
        if (!source && !target) return true;
        __result = source && b == source!.Target; return false;
    }
    private static void SelectedTarget(BaseAI __instance, ref Character __result)
    {
        var marker = Marker(__instance);
        // HuntPlayer's native closest-player fallback must not switch encounter owners.
        if (marker && __result != marker!.Target) __result = null!;
    }
    private static bool Attack(Humanoid __instance, ref bool __result)
    { if (!Local(__instance)) return true; __result = false; return false; }
    private static bool NoDamage(Character __instance) => !Local(__instance);
    private static bool Hud(Character c) => !Local(c);

    internal NativePursuit(Vector3 position, Player target)
    {
        var prefab = ZNetScene.instance.GetPrefab("Greydwarf");
        if (!prefab) throw new InvalidOperationException("Installed Greydwarf prefab unavailable.");
        var staging = new GameObject("ForestCrawler_DriverStaging"); staging.SetActive(false);
        try
        {
            Root = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity, staging.transform);
            Root.SetActive(false); Root.name = "ForestCrawler_LocalDriver";
            Root.AddComponent<LocalCrawlerDriver>().Target = target;
            // Preserve native animation dependencies and equipment metadata. Remove
            // unrelated spawning, drops, sound and visual-effect behaviours before Awake.
            foreach (var component in Root.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(component is LocalCrawlerDriver || component is Humanoid || component is MonsterAI ||
                    component is ZNetView || component is ZSyncAnimation || component is ZSyncTransform ||
                    component is CharacterAnimEvent || component is VisEquipment)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in Root.GetComponentsInChildren<Component>(true))
            {
                if (component is AudioSource audio) { audio.playOnAwake = false; audio.enabled = false; }
                if (component is ParticleSystem particle) particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach (var field in component.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (field.FieldType == typeof(EffectList)) field.SetValue(component, new EffectList());
            }
            hidden = Root.GetComponentsInChildren<Renderer>(true); foreach (var renderer in hidden) renderer.enabled = false;
            AI = Root.GetComponent<MonsterAI>(); Character = Root.GetComponent<Humanoid>(); body = Root.GetComponent<Rigidbody>(); constraints = body.constraints;
            // Ranged throws would stop pursuit before the encounter's contact radius.
            bool Melee(GameObject item) => item && item.GetComponent<ItemDrop>() && item.GetComponent<ItemDrop>().m_itemData.m_shared.m_aiAttackRange <= 3;
            Character.m_defaultItems = Character.m_defaultItems.Where(Melee).ToArray();
            Character.m_randomWeapon = Character.m_randomWeapon.Where(Melee).ToArray();
            foreach (var lod in Root.GetComponentsInChildren<LODGroup>(true)) lod.enabled = false;
            foreach (var animator in Root.GetComponentsInChildren<Animator>(true)) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Character.m_name = ""; Character.m_aiSkipTarget = true;
            AI.m_attackPlayerObjects = false; AI.m_sleeping = false; AI.m_enableHuntPlayer = true;
            AI.m_afraidOfFire = AI.m_avoidFire = false; AI.m_fleeIfHurtWhenTargetCantBeReached = false;
            AI.m_crownFearRange = 0; AI.m_consumeItems = new List<ItemDrop>();
            colliders = Root.GetComponentsInChildren<Collider>(true);
            Root.transform.SetParent(null, true); Root.SetActive(true);
            Write(AI, "m_targetCreature", target); Write(AI, "m_lastKnownTargetPos", target.transform.position);
            AI.Alert(); Running = true; IgnoreCharacters();
        }
        catch { if (Root) UnityEngine.Object.Destroy(Root); throw; }
        finally { UnityEngine.Object.Destroy(staging); }
    }
    internal void Tick(float runSpeed)
    {
        Character.m_runSpeed = runSpeed;
        foreach (var renderer in Root.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        if (Time.time >= nextCollisions) { nextCollisions = Time.time + .2f; IgnoreCharacters(); }
    }
    private void IgnoreCharacters()
    {
        foreach (var other in global::Character.GetAllCharacters())
            if (other != Character) foreach (var c in other.GetComponentsInChildren<Collider>())
                foreach (var own in colliders) if (own && c) Physics.IgnoreCollision(own, c);
    }
    internal bool HasReachablePath(Vector3 target)
    {
        var path = Read<List<Vector3>>(AI, "m_path");
        return Read<bool>(AI, "m_lastFindPathResult") && path.Count > 0 && Vector3.Distance(path[path.Count - 1], target) <= 2.5f;
    }
    internal void Pause(bool pause)
    {
        Running = !pause; AI.enabled = !pause; body.constraints = pause ? RigidbodyConstraints.FreezeAll : constraints;
        if (pause) { Character.SetMoveDir(Vector3.zero); Character.SetRun(false); body.linearVelocity = Vector3.zero; }
    }
    public void Dispose() { if (Root) { Root.SetActive(false); UnityEngine.Object.Destroy(Root); } }
}
