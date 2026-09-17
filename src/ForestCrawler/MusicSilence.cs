using UnityEngine;

namespace ForestCrawler;

// Local, temporary mute ownership. Never changes volume settings or stops music scheduling.
internal sealed class MusicSilence
{
    private AudioSource? source;
    private bool previousMute;
    internal bool Active { get; private set; }
    internal string Status => $"active={Active}, source={(source ? source!.name : "missing")}, muted={(source && source!.mute)}, playing={(source && source!.isPlaying)}, volume={(source ? source!.volume : 0):F2}";
    internal void Begin(AudioSource? current) { Active = true; Refresh(current); }
    internal void Refresh(AudioSource? current)
    {
        if (!Active) return;
        if (source != current)
        {
            Restore();
            source = current;
            if (source) previousMute = source!.mute;
        }
        if (source) source!.mute = true;
    }
    private void Restore()
    {
        if (source) source!.mute = previousMute;
        source = null;
    }
    internal void Clear() { Active = false; Restore(); }
}
