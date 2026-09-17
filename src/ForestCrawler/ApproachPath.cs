using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestCrawler;

// Direct steering validates and retains dense support samples.
// The motor consumes multiple waypoints per frame without shortcutting terrain.
internal static class ApproachPath
{
    internal delegate bool GroundQuery(Vector3 point, out Vector3 ground);
    internal static bool TryBuild(Vector3 from, Vector3 target, float stopRadius,
        GroundQuery groundQuery, Func<Vector3, Vector3, bool> segmentClear, List<Vector3> result, float maximumDistance = 8)
    {
        result.Clear();
        var horizontal = Vector3.ProjectOnPlane(target - from, Vector3.up);
        float distance = horizontal.magnitude;
        if (distance > maximumDistance || stopRadius <= .3f) return false;
        var end = Vector3.Distance(from, target) <= stopRadius ? target : target - (target - from).normalized * (stopRadius - .25f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, end) / .35f));
        var previous = from;
        var samples = new List<Vector3> { from };
        for (int i = 1; i <= steps; i++)
        {
            var point = Vector3.Lerp(from, end, i / (float)steps);
            if (!groundQuery(point, out var ground) || !segmentClear(previous, ground)) return false;
            previous = ground; samples.Add(ground);
        }
        // A player on a ledge/roof must not be caught from the ground below.
        if (Vector3.Distance(previous, target) > stopRadius) return false;
        result.AddRange(samples); return true;
    }
}
