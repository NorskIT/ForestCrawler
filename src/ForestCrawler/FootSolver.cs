using System;
using System.Linq;
using UnityEngine;

namespace ForestCrawler;

internal sealed class FootSolver
{
    private sealed class Leg
    {
        internal Transform Upper = null!, Lower = null!, Foot = null!;
        internal Vector3 Lock, Normal = Vector3.up, SoleAxis; internal bool Locked; internal float Offset;
    }
    private readonly Transform root, hips;
    private readonly Leg[] legs;
    private float pelvisOffset;
    internal FootSolver(GameObject obj)
    {
        root = obj.transform;
        var transforms = obj.GetComponentsInChildren<Transform>(true);
        Transform Find(string name) => transforms.First(t => t.name == name);
        hips = Find("Hips");
        legs = new[] { new Leg { Upper = Find("ThighL"), Lower = Find("CalfL"), Foot = Find("FootL"), Offset = 0 },
            new Leg { Upper = Find("ThighR"), Lower = Find("CalfR"), Foot = Find("FootR"), Offset = .5f } };
        foreach (var leg in legs) leg.SoleAxis = leg.Foot.InverseTransformDirection(root.up);
    }
    internal void Release() { foreach (var leg in legs) leg.Locked = false; pelvisOffset = 0; }
    internal void Solve(bool running, float phase, float dt)
    {
        // A paused Animator has not restored the authored pose. Re-applying the pelvis
        // offset to that same pose would sink it farther on every paused frame.
        if (dt <= 0) return;
        var up = root.up;
        float desiredPelvis = 0;
        foreach (var leg in legs)
        {
            float p = Mathf.Repeat(phase + leg.Offset, 1);
            bool contact = !running || p < .20f;
            if (!contact) { leg.Locked = false; continue; }
            if (!leg.Locked)
            {
                if (!Physics.Raycast(leg.Foot.position + up * .6f, -up, out var hit, 1.2f, World.Solids, QueryTriggerInteraction.Ignore)) continue;
                leg.Lock = hit.point + up * .12f; leg.Normal = hit.normal; leg.Locked = true;
            }
            // Lower the pelvis before judging reach. On an uphill stance the root rises
            // while the planted foot must remain at its original world elevation.
            float reach = Vector3.Distance(leg.Upper.position, leg.Lower.position) + Vector3.Distance(leg.Lower.position, leg.Foot.position);
            float horizontal = Vector3.ProjectOnPlane(leg.Upper.position - leg.Lock, up).magnitude;
            if (horizontal >= reach - .02f) { leg.Locked = false; continue; }
            float maximumHipY = Vector3.Dot(leg.Lock, up) + Mathf.Sqrt(Mathf.Max(0, (reach - .015f) * (reach - .015f) - horizontal * horizontal));
            desiredPelvis = Mathf.Min(desiredPelvis, Mathf.Clamp(maximumHipY - Vector3.Dot(leg.Upper.position, up), -.5f, 0));
            desiredPelvis = Mathf.Min(desiredPelvis, Mathf.Clamp(Vector3.Dot(leg.Lock - leg.Foot.position, up), -.35f, .12f));
        }
        pelvisOffset = Mathf.MoveTowards(pelvisOffset, desiredPelvis, dt * 6f);
        hips.position += up * pelvisOffset;
        foreach (var leg in legs)
        {
            if (!leg.Locked) continue;
            var authoredFootRotation = leg.Foot.rotation;
            SolveTwoBone(leg.Upper, leg.Lower, leg.Foot, leg.Lock, root.forward);
            // Bone +Y points along the toe; it is not the sole normal in this Generic rig.
            leg.Foot.rotation = Quaternion.FromToRotation(authoredFootRotation * leg.SoleAxis, leg.Normal) * authoredFootRotation;
        }
    }
    private static void SolveTwoBone(Transform upper, Transform lower, Transform foot, Vector3 target, Vector3 pole)
    {
        var origin = upper.position; float a = Vector3.Distance(origin, lower.position), b = Vector3.Distance(lower.position, foot.position);
        var vector = target - origin; float d = Mathf.Clamp(vector.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
        if (d < .001f || a < .001f || b < .001f) return;
        var axis = vector.normalized; var side = Vector3.ProjectOnPlane(pole, axis).normalized;
        float x = (a * a - b * b + d * d) / (2 * d); float y = Mathf.Sqrt(Mathf.Max(0, a * a - x * x));
        var knee = origin + axis * x + side * y;
        upper.rotation = Quaternion.FromToRotation(lower.position - origin, knee - origin) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(foot.position - lower.position, target - lower.position) * lower.rotation;
    }
}
