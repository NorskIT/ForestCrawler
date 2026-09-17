using System.Collections.Generic;
using UnityEngine;

namespace ForestCrawler;

internal static class GazeProbe
{
    private static readonly RaycastHit[] obstructionBuffer = new RaycastHit[32];
    internal static bool Hit(Camera camera, IReadOnlyList<Collider> sensors, float range, int solids,
        Transform ignoredPlayer, out Vector3 point, out string reason)
    {
        // Use the rendered viewport centre, including modified camera projection matrices.
        var ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
        float nearest = range; bool found = false; point = default;
        foreach (var sensor in sensors)
            if (sensor && sensor.enabled && sensor.gameObject.activeInHierarchy && sensor.Raycast(ray, out var hit, range) && (!found || hit.distance < nearest))
            { nearest = hit.distance; point = hit.point; found = true; }
        if (!found) { reason = "Aim misses detection surface or exceeds gaze range"; return false; }
        int count = Physics.RaycastNonAlloc(ray, obstructionBuffer, nearest, solids, QueryTriggerInteraction.Ignore);
        // Saturation is uncommon, but an allocating fallback must preserve wall detection.
        var hits = count == obstructionBuffer.Length ? Physics.RaycastAll(ray, nearest, solids, QueryTriggerInteraction.Ignore) : obstructionBuffer;
        if (hits != obstructionBuffer) count = hits.Length;
        for (int i = 0; i < count; i++)
        {
            var obstruction = hits[i];
            // The player's own body/equipment can sit between a third-person camera and the reticle.
            if (ignoredPlayer && obstruction.collider.transform.IsChildOf(ignoredPlayer)) continue;
            reason = "Blocked by " + obstruction.collider.name; return false;
        }
        reason = "Direct gaze"; return true;
    }
}
