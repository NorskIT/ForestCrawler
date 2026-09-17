using System;
using UnityEngine;

namespace ForestCrawler;

// Shared by runtime and editor physics tests. Placement uses separate conservative rules.
internal static class Traversal
{
    internal const float MaximumSlope = 85;
    internal static string LastFailure = "none";
    private static bool Fail(string reason) { LastFailure = reason; return false; }
    internal static bool Support(Vector3 point, int mask, Func<RaycastHit, bool> accept,
        out Vector3 ground, out Vector3 normal, bool target = false)
    {
        ground = point; normal = Vector3.up;
        // Target projection starts at the feet, so roofs above an airborne player cannot become destinations.
        if (!Physics.Raycast(point + Vector3.up * (target ? .2f : 6), Vector3.down,
            out var hit, target ? 16.2f : 22, mask, QueryTriggerInteraction.Ignore)) return Fail("No supporting ray hit at " + point.ToString("F3"));
        if (Vector3.Angle(hit.normal, Vector3.up) > MaximumSlope + .01f || !accept(hit)) return Fail("Rejected support " + hit.collider.name + " at " + hit.point.ToString("F3"));
        ground = hit.point; normal = hit.normal;
        return true;
    }
    internal static bool ClearBody(Vector3 point, Vector3 normal, int mask)
    {
        if (!Physics.CheckCapsule(point + normal * .60f, point + normal * 2f, .48f, mask, QueryTriggerInteraction.Ignore)) return true;
        var hits = Physics.OverlapCapsule(point + normal * .60f, point + normal * 2f, .48f, mask, QueryTriggerInteraction.Ignore);
        return Fail("Body overlaps " + (hits.Length > 0 ? hits[0].name : "solid") + " at " + point.ToString("F3") + " normal=" + normal.ToString("F3"));
    }

    internal static bool Segment(Vector3 from, Vector3 to, int mask, Func<RaycastHit, bool> accept)
    {
        var delta = to - from;
        float horizontal = Vector3.ProjectOnPlane(delta, Vector3.up).magnitude;
        if (Mathf.Abs(delta.y) > horizontal * Mathf.Tan(MaximumSlope * Mathf.Deg2Rad) + .3f) return Fail("Unsupported vertical step");
        int count = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / .15f));
        if (!Support(from, mask, accept, out var previous, out var previousNormal) || Vector3.Distance(previous, from) > .35f || !ClearBody(previous, previousNormal, mask)) return false;
        for (int i = 1; i <= count; i++)
        {
            var expected = Vector3.Lerp(from, to, i / (float)count);
            if (!Support(expected, mask, accept, out var point, out var normal) ||
                Vector3.Distance(point, expected) > .35f || !ClearBody(point, normal, mask)) return false;
            var translation = point - previous;
            if (translation.sqrMagnitude > .0000001f && Physics.CapsuleCast(previous + previousNormal * .60f,
                previous + previousNormal * 2f, .48f, translation.normalized, out var sweepHit, translation.magnitude,
                mask, QueryTriggerInteraction.Ignore)) return Fail("Body sweep hits " + sweepHit.collider.name + " at " + sweepHit.point.ToString("F3"));
            // Sweep both capsule endpoints across translation and changes of surface orientation.
            // This includes supporting terrain: no collider is globally ignored.
            foreach (float offset in new[] { .60f, 1.3f, 2f })
            {
                var a = previous + previousNormal * offset;
                var b = point + normal * offset;
                var movement = b - a;
                if (movement.sqrMagnitude > .0000001f && Physics.SphereCast(a, .48f, movement.normalized,
                    out var turnHit, movement.magnitude, mask, QueryTriggerInteraction.Ignore)) return Fail("Body turn hits " + turnHit.collider.name + " at " + turnHit.point.ToString("F3"));
            }
            // Rotating capsule interiors must also remain clear.
            if (!ClearBody((previous + point) * .5f, Vector3.Slerp(previousNormal, normal, .5f), mask)) return false;
            previous = point; previousNormal = normal;
        }
        return true;
    }
}
