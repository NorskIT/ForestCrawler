using System;
using BepInEx.Configuration;
using UnityEngine;

namespace ForestCrawler;

[Serializable]
internal sealed class Settings
{
    public bool Natural = true;
    public bool TeaseWhisper;
    public float Start = .95f, End = .05f, Rate = 6.6943065f, Cooldown = 45, Alone = 30, Isolation = 150;
    public float GazeDistance = 35, GazeSeconds = .4f, CloseDistance = 5, MistDistance = 8;
    public float Timeout = 180, Speed = 8, StopDistance = 3;
    public float LureMin = 60, LureMax = 90, RelocateMin = 40, RelocateMax = 70;
    public float VoiceLength, ScreamLength, RevealLength;
    public float HorrorStrength = .9f, ChromaticPixels = 1.8f, GrainPixels = 1.2f;
    internal static Settings Bind(ConfigFile config)
    {
        var s = new Settings();
        s.Natural = config.Bind("Scheduling", "NaturalEncounters", true, "Enable natural server-wide scheduling.").Value;
        float B(string section, string name, float value, float min, float max, string description) =>
            config.Bind(section, name, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max))).Value;
        s.Start = B("Scheduling", "MidnightStart", s.Start, 0, 1, "Raw network day fraction, inclusive. Midnight wraps through zero.");
        s.End = B("Scheduling", "MidnightEnd", s.End, 0, 1, "Raw network day fraction, exclusive.");
        s.Rate = B("Scheduling", "BaselineRatePerEligibleHour", s.Rate, 0, 60, "Server-wide Poisson rate divided by online player count. No accumulated missed rolls.");
        s.Cooldown = B("Scheduling", "CooldownMinutes", s.Cooldown, 45, 1440, "Real minutes from successful activation; persists across reconnects.");
        s.Alone = B("Eligibility", "AloneSeconds", s.Alone, 30, 300, "Continuous known isolation before activation.");
        s.Isolation = B("Eligibility", "IsolationMetres", s.Isolation, 150, 500, "Radius around both player and creature.");
        s.GazeDistance = B("Discovery", "GazeMetres", s.GazeDistance, 5, 60, "Maximum direct camera ray distance.");
        s.GazeSeconds = B("Discovery", "ContinuousGazeSeconds", s.GazeSeconds, .1f, 3, "Reset when line of sight or aim breaks.");
        s.CloseDistance = B("Discovery", "ProximityMetres", s.CloseDistance, 1, 10, "Requires unobstructed proximity.");
        s.MistDistance = B("Discovery", "MistGazeMetres", s.MistDistance, 1, 10, "Conservative visibility cap through uncleared mist.");
        s.Timeout = B("Encounter", "TimeoutSeconds", s.Timeout, 30, 300, "Maximum encounter length; eligibility can cancel earlier.");
        s.Speed = B("Encounter", "ChargeMetresPerSecond", s.Speed, 3, 10, "Preview motor speed; full chase uses a 12-18m/s adaptive speed. Animation follows actual displacement.");
        s.StopDistance = B("Encounter", "DisappearanceMetres", s.StopDistance, 3, 6, "Preview stopping separation. Full encounters catch at 2.5 metres.");
        s.LureMin = B("Encounter", "LureMinMetres", s.LureMin, 40, 90, "Minimum initial distance.");
        s.LureMax = B("Encounter", "LureMaxMetres", s.LureMax, s.LureMin, 110, "Maximum initial distance.");
        s.RelocateMin = B("Encounter", "RelocateMinMetres", s.RelocateMin, 20, 70, "Minimum relocation distance.");
        s.RelocateMax = B("Encounter", "RelocateMaxMetres", s.RelocateMax, s.RelocateMin, 90, "Maximum relocation distance.");
        s.HorrorStrength = B("Visuals", "HorrorStrength", s.HorrorStrength, 0, 1, "Local creature-only darkness and surface grain. The body remains opaque. Zero restores ordinary materials.");
        s.ChromaticPixels = B("Visuals", "ChromaticPixels", s.ChromaticPixels, 0, 5, "Local red/blue contour displacement in screen pixels; does not affect the world camera.");
        s.GrainPixels = B("Visuals", "GrainPixels", s.GrainPixels, 1, 4, "Local dither grain size in screen pixels.");
        return s;
    }
    internal string Serialize() => JsonUtility.ToJson(this);
}
