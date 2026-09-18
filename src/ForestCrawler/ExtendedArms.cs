using System;
using System.Linq;
using UnityEngine;

namespace ForestCrawler;

// Deforms the supplied skin, rather than drawing replacement arms or changing the root.
internal sealed class ExtendedArms : IDisposable
{
    private readonly Transform root;
    private readonly Transform[] upper, lower, hand;
    private readonly Transform[] bones;
    private readonly Vector3[] positions, scales;
    private readonly Quaternion[] rotations;
    private readonly SkinnedMeshRenderer[] skins;
    private readonly Bounds[] bounds;
    private bool posed;
    private readonly float[] offsets = new float[2];
    private Vector3 lateral;
    internal ExtendedArms(GameObject model)
    {
        root = model.transform; var transforms = model.GetComponentsInChildren<Transform>();
        Transform Find(string name) => transforms.First(t => t.name == name);
        lateral = root.right;
        upper = new[] { Find("UpperArmL"), Find("UpperArmR") };
        lower = new[] { Find("ForearmL"), Find("ForearmR") };
        hand = new[] { Find("HandL"), Find("HandR") };
        var midpoint = (upper[0].position + upper[1].position) * .5f;
        for (int i = 0; i < 2; i++) offsets[i] = Vector3.Dot(upper[i].position - midpoint, root.right);
        bones = upper.Concat(lower).Concat(hand).ToArray(); positions = new Vector3[bones.Length]; scales = new Vector3[bones.Length]; rotations = new Quaternion[bones.Length];
        skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(); bounds = skins.Select(s => s.localBounds).ToArray();
    }
    internal void Restore()
    {
        if (!posed) return;
        for (int i = 0; i < bones.Length; i++) if (bones[i]) { bones[i].localPosition = positions[i]; bones[i].localScale = scales[i]; bones[i].localRotation = rotations[i]; }
        for (int i = 0; i < skins.Length; i++) if (skins[i]) skins[i].localBounds = bounds[i];
        posed = false;
    }
    internal void Pose(Vector3 target, float extension)
    {
        Restore();
        for (int i = 0; i < bones.Length; i++) { positions[i] = bones[i].localPosition; scales[i] = bones[i].localScale; rotations[i] = bones[i].localRotation; }
        posed = true;
        var shoulders = (upper[0].position + upper[1].position) * .5f;
        var reach = target - shoulders;
        var candidate = Vector3.Cross(Vector3.up, reach).normalized;
        if (candidate.sqrMagnitude > .01f) lateral = Vector3.Dot(candidate, root.right) < 0 ? -candidate : candidate;
        float blend = Mathf.SmoothStep(0, 1, extension);
        for (int i = 0; i < 2; i++)
        {
            var end = Vector3.Lerp(hand[i].position, target + lateral * offsets[i], blend);
            var elbow = Vector3.Lerp(lower[i].position, shoulders + reach * .52f + lateral * offsets[i], blend);
            upper[i].rotation = Quaternion.FromToRotation(lower[i].position - upper[i].position, elbow - upper[i].position) * upper[i].rotation;
            Stretch(upper[i], lower[i], Vector3.Distance(upper[i].position, elbow));
            lower[i].position = elbow;
            lower[i].rotation = Quaternion.FromToRotation(hand[i].position - elbow, end - elbow) * lower[i].rotation;
            Stretch(lower[i], hand[i], Vector3.Distance(elbow,end));
            hand[i].position = end;
            var inward = target - end;
            if (inward.sqrMagnitude > .001f) hand[i].rotation = Quaternion.Slerp(hand[i].rotation, Quaternion.LookRotation(inward, Vector3.up), blend * .65f);
        }
        foreach (var skin in skins)
        {
            var enlarged = skin.localBounds; enlarged.Encapsulate((skin.rootBone ? skin.rootBone : skin.transform).InverseTransformPoint(target));
            enlarged.Expand(2); skin.localBounds = enlarged;
        }
    }
    private static void Stretch(Transform parent, Transform child, float length)
    {
        float current = Vector3.Distance(parent.position, child.position);
        if (current < .001f) return;
        var axis = child.localPosition.normalized; var scale = parent.localScale;
        int coordinate = Mathf.Abs(axis.x) > Mathf.Abs(axis.y) ? 0 : 1;
        if (Mathf.Abs(axis.z) > Mathf.Abs(axis[coordinate])) coordinate = 2;
        scale[coordinate] *= length/current; parent.localScale = scale;
    }
    public void Dispose() => Restore();
}
