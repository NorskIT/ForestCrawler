using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ForestCrawler;

internal static class World
{
    internal static string LastRouteFailure = "No route requested";
    internal static string LastGroundFailure = "No ground query";
    internal static readonly int Solids = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
    internal static bool Finite(Vector3 v) => Rules.Finite(v.x) && Rules.Finite(v.y) && Rules.Finite(v.z);
    internal static bool Allowed(Vector3 point)
    {
        if (WorldGenerator.instance == null) return false;
        var b = WorldGenerator.instance.GetBiome(point);
        return b == Heightmap.Biome.BlackForest || b == Heightmap.Biome.Swamp || b == Heightmap.Biome.Mistlands;
    }
    internal static bool Ground(Vector3 point, out Vector3 ground)
    {
        ground = point; LastGroundFailure = "No solid ground below candidate";
        if (!Physics.Raycast(point + Vector3.up * 6, Vector3.down, out var hit, 16, Solids, QueryTriggerInteraction.Ignore)) return false;
        LastGroundFailure = $"Ground hit {hit.collider.name} at {hit.point:F2}, slope={Vector3.Angle(hit.normal, Vector3.up):F1}";
        if (hit.collider.GetComponentInParent<Piece>() || hit.collider.GetComponentInParent<Character>()) return false;
        if (Vector3.Angle(hit.normal, Vector3.up) > 30) return false;
        ground = hit.point;
        if (ZoneSystem.instance && ground.y < ZoneSystem.instance.m_waterLevel + .05f) return false;
        return !Physics.CheckCapsule(ground + Vector3.up * .60f, ground + Vector3.up * 2f, .48f, Solids, QueryTriggerInteraction.Ignore);
    }
    internal static bool SegmentClear(Vector3 from, Vector3 to)
    {
        var delta = to - from;
        // Flat ledge tops can both pass Ground() while hiding a large drop between them.
        if (!Rules.WalkableHeightChange(Vector3.ProjectOnPlane(delta, Vector3.up).magnitude, delta.y)) return false;
        return !Physics.CapsuleCast(from + Vector3.up * .60f, from + Vector3.up * 2f, .48f, delta.normalized,
            delta.magnitude, Solids, QueryTriggerInteraction.Ignore);
    }
    private static bool ChaseSupport(RaycastHit hit) =>
        !hit.collider.GetComponentInParent<Piece>() && !hit.collider.GetComponentInParent<Character>() &&
        (!ZoneSystem.instance || hit.point.y >= ZoneSystem.instance.m_waterLevel + .05f);
    internal static bool ChaseGround(Vector3 point, out Vector3 ground) => ChaseGround(point, out ground, out _);
    internal static bool ChaseGround(Vector3 point, out Vector3 ground, out Vector3 normal) =>
        Traversal.Support(point, Solids, ChaseSupport, out ground, out normal) && Traversal.ClearBody(ground, normal, Solids);
    internal static bool TargetGround(Vector3 point, out Vector3 ground) =>
        Traversal.Support(point, Solids, ChaseSupport, out ground, out _, true);
    internal static bool ChaseSegment(Vector3 from, Vector3 to) => Traversal.Segment(from, to, Solids, ChaseSupport);
    internal static bool Route(Vector3 from, Vector3 to, bool bypassBiome, List<Vector3> result)
    {
        result.Clear();
        bool Fail(string reason) { LastRouteFailure = reason; result.Clear(); return false; }
        if (!Pathfinding.instance || !Pathfinding.instance.GetPath(from, to, result, Pathfinding.AgentType.HumanoidBigNoSwim, true)) return Fail("Navigation tiles pending or no complete no-swim path");
        if (result.Count < 2 || result.Count > 128) return Fail("Invalid navigation corner count");
        var corners = result.ToArray(); result.Clear(); result.Add(from);
        Vector3 previous = from, previousGround = from;
        foreach (var corner in corners)
        {
            float length = Vector3.Distance(previous, corner);
            int count = Math.Max(1, Mathf.CeilToInt(length / .35f));
            for (int i = 1; i <= count; ++i)
            {
                var p = Vector3.Lerp(previous, corner, i / (float)count);
                bool Sample(Vector3 candidate, out Vector3 sampled) => ChaseGround(candidate, out sampled) && (bypassBiome || Allowed(sampled));
                if (!Sample(p, out var ground) || !ChaseSegment(previousGround, ground))
                {
                    // Game navigation corners can skim small rocks and trunk colliders.
                    // Repair the corridor locally using fully checked ground and sweeps.
                    var tangent = Vector3.ProjectOnPlane(corner - previous, Vector3.up).normalized;
                    var side = Vector3.Cross(Vector3.up, tangent);
                    bool repaired = false;
                    foreach (float offset in new[] { .75f, -.75f, 1.5f, -1.5f, 2.25f, -2.25f })
                    {
                        if (!Sample(p + side * offset, out var alternative) || !ChaseSegment(previousGround, alternative)) continue;
                        ground = alternative; repaired = true; break;
                    }
                    if (!repaired) return Fail("Untraversable route sample: " + Traversal.LastFailure);
                }
                if (Vector3.Distance(previousGround, ground) > .001f) result.Add(ground);
                if (result.Count > 512) return Fail("Route exceeds the supported waypoint budget");
                previousGround = ground;
            }
            previous = corner;
        }
        LastRouteFailure = "Ready";
        return result.Count >= 2;
    }
    // target is already projected onto supporting ground; catch validation uses the actual player separately.
    internal static bool ChargeRoute(Vector3 from, Vector3 target, bool bypassBiome, float stopRadius, List<Vector3> result)
    {
        bool GroundInBiome(Vector3 point, out Vector3 ground) => ChaseGround(point, out ground) && (bypassBiome || Allowed(ground));
        bool GoalClear(Vector3 point) => !Obstructed(point + Vector3.up * 1.2f, target + Vector3.up * 1.2f);
        if (ApproachPath.TryBuild(from, target, stopRadius, GroundInBiome, ChaseSegment, result, 90) && GoalClear(result[result.Count-1]))
        { LastRouteFailure = "Validated grounded approach"; return true; }
        LastRouteFailure = "No reachable grounded destination: " + Traversal.LastFailure;
        var away = Vector3.ProjectOnPlane(from - target, Vector3.up).normalized;
        foreach (float angle in new[] { 0f, 45f, -45f, 90f, -90f })
        {
            var candidate = target + Quaternion.Euler(0, angle, 0) * away * (stopRadius - .25f);
            if (!GroundInBiome(candidate, out var goal) || Vector3.Distance(goal, target) > stopRadius ||
                Obstructed(goal + Vector3.up * 1.2f, target + Vector3.up * 1.2f)) continue;
            if (Route(from, goal, bypassBiome, result) && Vector3.Distance(result[result.Count-1], target) <= stopRadius && GoalClear(result[result.Count-1])) return true;
        }
        if (SurfacePath.TryBuild(from,target,stopRadius,GroundInBiome,ChaseSegment,GoalClear,result))
        { LastRouteFailure = "Validated local surface route"; return true; }
        if (Vector3.Distance(from,target)>24)
        {
            // Connect the ordinary navigation route to a local rock-surface route.
            var prefix=new List<Vector3>(); var suffix=new List<Vector3>();
            foreach(float angle in new[]{0f,45f,-45f,90f,-90f,180f,135f,-135f})
            {
                var probe=target+Quaternion.Euler(0,angle,0)*away*10;
                if(!GroundInBiome(probe,out var entry) || !Route(from,entry,bypassBiome,prefix)) continue;
                entry=prefix[prefix.Count-1];
                if(!SurfacePath.TryBuild(entry,target,stopRadius,GroundInBiome,ChaseSegment,GoalClear,suffix) || prefix.Count+suffix.Count>512) continue;
                result.Clear(); result.AddRange(prefix); result.AddRange(suffix);
                LastRouteFailure="Navigation connected to local surface route"; return true;
            }
        }
        LastRouteFailure = "No grounded approach after navigation and bounded surface search: " + Traversal.LastFailure;
        result.Clear(); return false;
    }
    internal static bool Obstructed(Vector3 from, Vector3 to) => Physics.Linecast(from, to, Solids, QueryTriggerInteraction.Ignore);
    internal static bool MistVisible(Vector3 from, Vector3 to, float limit)
    {
        float distance = Vector3.Distance(from, to);
        if (ParticleMist.IsMistBlocked(from, to)) return false;
        if (distance <= limit) return true;
        int count = Mathf.CeilToInt(distance);
        for (int i = 0; i <= count; ++i) if (ParticleMist.IsInMist(Vector3.Lerp(from, to, i / (float)Math.Max(1, count)))) return false;
        return true;
    }
    internal static bool LocalIntruder(Vector3 target, Vector3 creature, float radius) => Player.GetAllPlayers().Any(p =>
        p != Player.m_localPlayer && !p.IsDead() && (Vector3.Distance(p.transform.position, target) < radius || Vector3.Distance(p.transform.position, creature) < radius));
}
