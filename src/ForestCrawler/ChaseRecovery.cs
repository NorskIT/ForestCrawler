namespace ForestCrawler;

// One monotonic deadline for all navigation and movement failures.
internal sealed class ChaseRecovery
{
    internal const double Duration = 5;
    private double started = -1;
    internal string Reason { get; private set; } = "none";
    internal bool Active => started >= 0;
    internal double Remaining(double now) => Active ? System.Math.Max(0, Duration - (now - started)) : Duration;
    internal bool Expired(double now) => Active && now - started >= Duration;
    internal bool Fail(double now, string reason)
    {
        Reason = reason;
        if (Active) return false;
        started = now; return true;
    }
    internal void Reset() { started = -1; Reason = "none"; }
}
