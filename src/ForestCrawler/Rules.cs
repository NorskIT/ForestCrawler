using System;

namespace ForestCrawler;

internal enum Phase { Preparing, Lure, Relocating, Watching, Reveal, Charge, Tail, Stare, Tease, Caught }
internal enum EncounterKind { Full, Tease }

internal sealed class ScheduleClock
{
    private double next;
    internal void Reset(double now) => next = now;
    internal bool Due(double now)
    {
        if (now < next) return false;
        next = now + 1; // Missed seconds are discarded, never replayed.
        return true;
    }
}

internal static class Rules
{
    internal const int Protocol = 2;
    internal const float LureDeadlineSeconds = 120;
    internal static bool LureExpired(EncounterKind kind, Phase phase, double elapsed) =>
        kind == EncounterKind.Full && phase == Phase.Lure && elapsed >= LureDeadlineSeconds;
    internal static EncounterKind SelectKind(bool hasTease, double draw) => !hasTease || draw < .5 ? EncounterKind.Tease : EncounterKind.Full;
    internal static bool CanStart(EncounterKind kind, double quietRemaining, double fullRemaining) => quietRemaining <= 0 && (kind != EncounterKind.Full || fullRemaining <= 0);
    internal static double HeartBpm(double distance) => 100 + 130 * Math.Max(0, Math.Min(1, (100 - distance) / 80));
    internal static double ChaseSpeed(double playerSpeed) => Math.Min(18, Math.Max(12, playerSpeed + 3));
    internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    internal static double Fraction(double seconds, double dayLength) => ((seconds % dayLength) + dayLength) % dayLength / dayLength;
    internal static bool InWindow(double fraction, double start, double end) => start > end
        ? fraction >= start || fraction < end : fraction >= start && fraction < end;
    internal static double NextWindow(double seconds, double dayLength, double start)
    {
        double candidate = Math.Floor(seconds / dayLength) * dayLength + start * dayLength;
        return candidate < seconds ? candidate + dayLength : candidate;
    }
    internal static double TriggerChance(double ratePerHour, int players, double elapsedSeconds) =>
        players < 1 ? 0 : 1 - Math.Exp(-Math.Max(0, ratePerHour) * Math.Max(0, elapsedSeconds) / (3600 * players));
    internal static bool CanDiscover(Phase phase, double elapsed, double lineLength) =>
        phase == Phase.Lure;
    internal static bool WalkableHeightChange(double horizontal, double vertical) =>
        Finite(horizontal) && Finite(vertical) && horizontal >= 0 && Math.Abs(vertical) <= horizontal * .7 + .2;
    // Minimum distance of a relative movement segment to the origin. Handles movement of both actors.
    internal static double SweptDistanceSquared(double ax, double ay, double az, double bx, double by, double bz)
    {
        double dx = bx - ax, dy = by - ay, dz = bz - az;
        double length = dx * dx + dy * dy + dz * dz;
        double t = length < 1e-12 ? 0 : Math.Max(0, Math.Min(1, -(ax * dx + ay * dy + az * dz) / length));
        ax += dx * t; ay += dy * t; az += dz * t;
        return ax * ax + ay * ay + az * az;
    }
}
