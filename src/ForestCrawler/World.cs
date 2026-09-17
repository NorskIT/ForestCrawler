using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ForestCrawler;

internal static class World
{
    internal static string LastRouteFailure = "No route requested";
    internal static string LastGroundFailure = "No ground query";
    internal static readonly int Solids = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle", "blocker");
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
    internal static bool Route(Vector3 from, Vector3 to, bool bypassBiome, List<Vector3> result)
    {
        result.Clear();
        var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab("Greydwarf") : null;
        var ai = prefab ? prefab.GetComponent<MonsterAI>() : null;
        if (!ai || !Pathfinding.instance) { LastRouteFailure = "Native navigation unavailable"; return false; }
        // Candidate preflight only. Actual pursuit and all route maintenance belong to MonsterAI.
        bool found = Pathfinding.instance.GetPath(from, to, result, ai!.m_pathAgentType);
        LastRouteFailure = found ? "Native route ready" : "Native navigation pending or unreachable";
        if (!found || result.Count == 0) return false;
        result.Insert(0, from);
        return result.Count <= 512 && (bypassBiome || result.TrueForAll(Allowed));
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
