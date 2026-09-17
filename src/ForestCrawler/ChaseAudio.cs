using System;
using UnityEngine;

namespace ForestCrawler;

// All chase pressure audio belongs to the target client. DSP scheduling is independent of frame rate.
internal sealed class ChaseAudio : IDisposable
{
    private readonly GameObject root = new("ForestCrawler_ChaseAudio");
    private readonly AudioSource[] beats = new AudioSource[2];
    private readonly AudioDistortionFilter distortion;
    private readonly AudioSource close;
    private double nextBeat;
    private int index;
    private bool near;
    internal float Bpm { get; private set; }
    internal ChaseAudio(AssetStore assets)
    {
        for (int i = 0; i < beats.Length; i++) beats[i] = Source(root, assets.Audio["heartbeat"]);
        distortion = root.AddComponent<AudioDistortionFilter>(); distortion.distortionLevel = .02f;
        var bed = new GameObject("CloseChaseLoop"); bed.transform.SetParent(root.transform);
        close = Source(bed, assets.Audio["close"]); close.loop = true; close.volume = 0; close.Play();
        nextBeat = AudioSettings.dspTime + .03;
    }
    internal static AudioSource Source(GameObject root, AudioClip clip)
    {
        var s = root.AddComponent<AudioSource>(); s.clip = clip; s.playOnAwake = false;
        s.spatialBlend = 0; s.dopplerLevel = 0; s.priority = 16; s.volume = .5f; return s;
    }
    internal void Update(float distance)
    {
        Bpm = (float)Rules.HeartBpm(distance);
        float pressure = Mathf.Clamp01((100 - distance) / 80);
        foreach (var beat in beats) beat.volume = Mathf.Lerp(.14f, .58f, pressure);
        distortion.distortionLevel = Mathf.Lerp(.02f, .68f, pressure * pressure);
        if (distance < 25) near = true; else if (distance > 30) near = false;
        close.volume = Mathf.MoveTowards(close.volume, near ? .38f * Mathf.Clamp01((30 - distance) / 10) : 0, Time.unscaledDeltaTime * .8f);
        double now = AudioSettings.dspTime;
        if (nextBeat < now) nextBeat = now + .025; // Drop missed beats; never catch up in a burst.
        if (nextBeat < now + .08)
        {
            beats[index].PlayScheduled(nextBeat); index = (index + 1) % beats.Length;
            nextBeat += 60 / Bpm;
        }
    }
    public void Dispose()
    {
        if (!root) return; // Scene teardown may destroy audio before plugin cleanup.
        foreach (var source in root.GetComponentsInChildren<AudioSource>()) source.Stop();
        UnityEngine.Object.Destroy(root);
    }
}
