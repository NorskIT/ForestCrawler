using System;
using HarmonyLib;
using UnityEngine;

namespace ForestCrawler;

// The only player movement authority while an authorized pull is active.
internal sealed class ArmGrab : IDisposable
{
    private static readonly Harmony patches = new(Plugin.Id + ".pull");
    internal static ArmGrab? Current;
    private readonly Player player;
    private readonly Rigidbody body;
    private readonly bool gravity, kinematic;
    private readonly RigidbodyConstraints constraints;
    private readonly Vector3 destination;
    private readonly float speed;
    private Vector3 safeRelease, liftPoint, overPoint;
    private bool lifting, crossing;
    internal bool Blocked { get; private set; }
    internal bool Arrived => Vector3.Distance(body.position, destination) <= 2.5f;
    internal static void Install()
    {
        void Patch(Type type, string method, string handler) => patches.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(ArmGrab), handler));
        Patch(typeof(Player), "TakeInput", nameof(Input));
        Patch(typeof(Character), "UpdateMotion", nameof(Motion));
        Patch(typeof(Character), "ApplyDamage", nameof(Motion));
        Patch(typeof(Character), "RPC_Damage", nameof(Motion));
    }
    internal static void Uninstall() => patches.UnpatchSelf();
    private static bool Input(Player __instance, ref bool __result)
    { if (Current == null || __instance != Current.player) return true; __result = false; return false; }
    private static bool Motion(Character __instance) => Current == null || __instance != Current.player;
    internal ArmGrab(Vector3 destination, float speed)
    {
        if (Current != null) throw new InvalidOperationException("A pull already owns player movement.");
        player = Player.m_localPlayer; body = player.GetComponent<Rigidbody>();
        this.destination = destination; this.speed = speed; safeRelease = body.position;
        lifting = body.position.y-destination.y > .75f;
        liftPoint = body.position + Vector3.up * 1.5f;
        crossing = lifting;
        var away = Vector3.ProjectOnPlane(destination-body.position,Vector3.up).normalized;
        overPoint = destination + away*1.2f; overPoint.y = liftPoint.y;
        gravity = body.useGravity; kinematic = body.isKinematic; constraints = body.constraints;
        player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
        body.linearVelocity = Vector3.zero; body.useGravity = false; body.isKinematic = true;
        Current = this;
    }
    internal void FixedUpdate()
    {
        if (Blocked || !player || player.IsDead() || Arrived) return;
        if (lifting && Vector3.Distance(body.position,liftPoint)<.03f) lifting = false;
        if (!lifting && crossing && Vector3.Distance(body.position,overPoint)<.03f) crossing = false;
        var delta = (lifting ? liftPoint : crossing ? overPoint : destination) - body.position;
        float step = Mathf.Min(speed * Time.fixedDeltaTime, Mathf.Max(0, delta.magnitude - (lifting || crossing ? 0 : 2.3f)));
        var capsule = player.GetComponent<CapsuleCollider>();
        // Sweep from the physics pose, independent of render interpolation.
        var center = body.position + body.rotation * Vector3.Scale(capsule.center, capsule.transform.lossyScale);
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x), Mathf.Abs(capsule.transform.lossyScale.z));
        float half = Mathf.Max(0, capsule.height * Mathf.Abs(capsule.transform.lossyScale.y) / 2 - radius);
        // Slight skin avoids treating an existing floor contact as a forward blockage.
        if (Physics.CapsuleCast(center + Vector3.up * half, center - Vector3.up * half, radius * .95f,
            delta.normalized, out var obstacle, step + .04f, World.Solids, QueryTriggerInteraction.Ignore))
        { Plugin.Instance.Log($"Pull blocked by {obstacle.collider.name}; player={body.position:F2}; destination={destination:F2}; lifting={lifting}; crossing={crossing}"); Blocked = true; return; }
        body.MovePosition(body.position + delta.normalized * step);
        NativePursuit.Write(player, "m_maxAirAltitude", body.position.y);
        // Save a grounded release point, rather than leaving a cancelled pull mid-air.
        if (Physics.Raycast(body.position + Vector3.up * .1f, Vector3.down, out var hit, .35f, World.Solids, QueryTriggerInteraction.Ignore))
            safeRelease = hit.point;
    }
    public void Dispose()
    {
        if (Current != this) return;
        Current = null;
        if (!body) return;
        body.isKinematic = kinematic; body.useGravity = gravity; body.constraints = constraints;
        if (!kinematic) body.linearVelocity = Vector3.zero;
        if (player)
        {
            if (!Arrived && !player.IsDead()) { body.position = safeRelease; player.transform.position = safeRelease; }
            NativePursuit.Write(player, "m_maxAirAltitude", body.position.y);
        }
    }

    internal static bool Visible(Vector3 creature, Player target, float range)
    {
        if (!target || Vector3.Distance(creature, target.transform.position) > range) return false;
        var from = creature + Vector3.up * 2.1f;
        var to = target.GetCenterPoint();
        return !Physics.Linecast(from, to, World.Solids, QueryTriggerInteraction.Ignore);
    }
}
