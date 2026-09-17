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
    internal ExtendedArms(GameObject model)
    {
        root = model.transform; var transforms = model.GetComponentsInChildren<Transform>();
        Transform Find(string name) => transforms.First(t => t.name == name);
        upper = new[] { Find("UpperArmL"), Find("UpperArmR") };
        lower = new[] { Find("ForearmL"), Find("ForearmR") };
        hand = new[] { Find("HandL"), Find("HandR") };
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
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1 : 1;
            var end = Vector3.Lerp(hand[i].position, target + root.right * side * .22f, Mathf.SmoothStep(0, 1, extension));
            var elbow = Vector3.Lerp(upper[i].position, end, .52f) + root.right * side * .35f;
            upper[i].rotation = Quaternion.FromToRotation(lower[i].position - upper[i].position, elbow - upper[i].position) * upper[i].rotation;
            Stretch(upper[i], lower[i], Vector3.Distance(upper[i].position, elbow));
            lower[i].position = elbow;
            lower[i].rotation = Quaternion.FromToRotation(hand[i].position - elbow, end - elbow) * lower[i].rotation;
            Stretch(lower[i], hand[i], Vector3.Distance(elbow,end));
            hand[i].position = end;
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
