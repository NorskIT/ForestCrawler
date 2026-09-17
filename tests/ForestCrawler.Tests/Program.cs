using ForestCrawler;

int passed = 0;
void Check(string name, bool condition) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
bool Near(double a, double b, double epsilon = 1e-6) => Math.Abs(a - b) < epsilon;
Check("Midnight window wraps and excludes end", Rules.InWindow(.99, .95, .05) && Rules.InWindow(.01, .95, .05) && !Rules.InWindow(.05, .95, .05) && !Rules.InWindow(.5, .95, .05));
Check("Inclusive start", Rules.InWindow(.95, .95, .05));
Check("Non-wrapping window", Rules.InWindow(.5, .4, .6) && !Rules.InWindow(.7, .4, .6));
Check("Midnight fraction is zero", Near(Rules.Fraction(2400, 1200), 0));
Check("Modified day length", Near(Rules.Fraction(2850, 3000), .95));
Check("Time command advances without rewinding", Near(Rules.NextWindow(1170, 1200, .95), 2340));
Check("Time command uses current day when possible", Near(Rules.NextWindow(600, 1200, .95), 1140));
Check("Time command boundary is stable", Near(Rules.NextWindow(1140, 1200, .95), 1140));
Check("Baseline twenty percent per eligible two minutes", Near(Rules.TriggerChance(6.6943065, 1, 120), .20, .00001));
double previous = 1;
foreach (int population in new[] { 1, 2, 4, 8 })
{
    double p = Rules.TriggerChance(6.6943065, population, 120);
    Check($"Total server chance decreases for N={population}", p < previous);
    Check($"Per-second integration for N={population}", Near(1 - Math.Pow(1 - Rules.TriggerChance(6.6943065, population, 1), 120), p));
    previous = p;
}
Check("No players means no trigger", Rules.TriggerChance(6.6943065, 0, 120) == 0);
Check("Disabled rate means no trigger", Rules.TriggerChance(0, 1, 120) == 0);
Check("First discovery available during lure", Rules.CanDiscover(Phase.Lure, 1, 8));
Check("Relocation no longer accepts a second discovery", !Rules.CanDiscover(Phase.Watching, 8.4, 8) && !Rules.CanDiscover(Phase.Watching, 80, 8));
foreach (var p in new[] { Phase.Preparing, Phase.Relocating, Phase.Reveal, Phase.Charge, Phase.Tail, Phase.Stare, Phase.Caught, Phase.Tease }) Check("Discovery rejected during " + p, !Rules.CanDiscover(p, 100, 8));
Check("Swept charge detects overshoot", Rules.SweptDistanceSquared(0, 0, 8, 0, 0, -8) < 9);
Check("Swept charge preserves safe miss", Rules.SweptDistanceSquared(4, 0, 8, 4, 0, -8) > 9);
Check("Stationary relative movement", Near(Rules.SweptDistanceSquared(0, 0, 4, 0, 0, 4), 16));
Check("Moving player crosses creature path", Rules.SweptDistanceSquared(-2, 0, 4, 2, 0, -4) < 9);
Check("Finite validation rejects malicious values", !Rules.Finite(double.NaN) && !Rules.Finite(double.PositiveInfinity));
var scheduler = new ScheduleClock(); scheduler.Reset(10);
Check("Scheduler runs first eligible interval", scheduler.Due(10));
Check("Scheduler does not multiply per frame", !scheduler.Due(10.1) && !scheduler.Due(10.9));
Check("Scheduler discards missed opportunities", scheduler.Due(5000) && !scheduler.Due(5000) && !scheduler.Due(5000.5));
Check("Scheduler resumes at normal interval", scheduler.Due(5001));
string directory = Path.Combine(Path.GetTempPath(), "ForestCrawlerTests-" + Guid.NewGuid().ToString("N"));
try
{
    var progression = new Progression(); progression.Load(directory, 123);
    Check("First natural event has no completed tease", !progression.HasTease("Steam_1"));
    progression.Start("Steam_1", 1000);
    Check("Shared quiet interval is fifteen real minutes", Near(progression.Remaining("Steam_1", 1000), 900));
    Check("Starting tease does not grant completion", !progression.HasTease("Steam_1"));
    progression.CompleteTease("Steam_1"); progression.CompleteTease("Steam_1");
    var persisted = new Progression(); persisted.Load(directory, 123);
    Check("Completed tease survives reconnect", persisted.HasTease("Steam_1"));
    Check("Quiet interval survives reconnect", Near(persisted.Remaining("Steam_1", 1100), 800));
    Check("Tease completion is per-player", !persisted.HasTease("Steam_2"));
    persisted.Load(directory, 456); Check("Tease completion is per-world", !persisted.HasTease("Steam_1"));
    var store = new Cooldowns(); store.Load(directory, 123);
    Check("No artificial cooldown for first encounter", store.Remaining("Steam_1", 1000) == 0);
    store.Start("Steam_1", 1000, 45);
    Check("Minimum cooldown is 2700 real seconds", Near(store.Remaining("Steam_1", 1000), 2700));
    var reconnect = new Cooldowns(); reconnect.Load(directory, 123);
    Check("Cooldown survives restart and reconnect", Near(reconnect.Remaining("Steam_1", 1300), 2400));
    Check("Cooldown is per player", reconnect.Remaining("Steam_2", 1300) == 0);
    Check("Cooldown expiration is exact", reconnect.Remaining("Steam_1", 3700) == 0);
    reconnect.Load(directory, 456);
    Check("Worlds have separate cooldown ledgers", reconnect.Remaining("Steam_1", 1300) == 0);
    store.Start("Steam_2", 1500, 45);
    var updated = new Cooldowns(); updated.Load(directory, 123);
    Check("Atomic replacement preserves other player deadlines", Near(updated.Remaining("Steam_1", 1500), 2200) && Near(updated.Remaining("Steam_2", 1500), 2700));
}
finally { Directory.Delete(directory, true); }
Check("First natural draw always selects tease", Rules.SelectKind(false, .99999) == EncounterKind.Tease);
Check("Experienced player tease half", Rules.SelectKind(true, .49999) == EncounterKind.Tease);
Check("Experienced player full half", Rules.SelectKind(true, .5) == EncounterKind.Full);
Check("Quiet period blocks either kind", !Rules.CanStart(EncounterKind.Tease, 1, 0) && !Rules.CanStart(EncounterKind.Full, 1, 0));
Check("Full cooldown does not block tease", Rules.CanStart(EncounterKind.Tease, 0, 100));
Check("Full draw blocked without replacement", !Rules.CanStart(Rules.SelectKind(true, .9), 0, 100));
Check("Eligible full draw allowed", Rules.CanStart(EncounterKind.Full, 0, 0));
Check("Heartbeat reaches 100 BPM at 100 metres", Near(Rules.HeartBpm(100), 100));
Check("Heartbeat reaches 230 BPM at 20 metres", Near(Rules.HeartBpm(20), 230));
Check("Heartbeat interpolates linearly", Near(Rules.HeartBpm(60), 165));
Check("Heartbeat never exceeds 230 BPM", Near(Rules.HeartBpm(-10), 230));
Check("Distant heartbeat remains at 100 BPM", Near(Rules.HeartBpm(300), 100));
Check("Chase minimum speed is twelve metres per second", Near(Rules.ChaseSpeed(0), 12));
Check("Chase outruns ordinary sprinting", Near(Rules.ChaseSpeed(11), 14));
Check("Chase hard speed cap", Near(Rules.ChaseSpeed(40), 18));
Check("Full lure waits until two minutes", !Rules.LureExpired(EncounterKind.Full, Phase.Lure, 119.999));
Check("Full lure expires at exactly two minutes", Rules.LureExpired(EncounterKind.Full, Phase.Lure, 120));
Check("Late update still expires full lure", Rules.LureExpired(EncounterKind.Full, Phase.Lure, 121));
Check("Tease cannot auto-charge", !Rules.LureExpired(EncounterKind.Tease, Phase.Lure, 120));
Check("Discovery cancels lure deadline", !Rules.LureExpired(EncounterKind.Full, Phase.Stare, 120));
Check("Relocated encounter cannot repeat timeout transition", !Rules.LureExpired(EncounterKind.Full, Phase.Watching, 120));
Check("Grab waits thirty seconds", !Rules.CanStartGrab(Phase.Charge,29.999,0,20,30,60,30));
Check("Grab starts at thirty seconds and includes range boundary", Rules.CanStartGrab(Phase.Charge,30,0,30,30,60,30));
Check("Grab rejects targets beyond reach", !Rules.CanStartGrab(Phase.Charge,30,0,30.01,30,60,30));
Check("Grab cannot restart windup", !Rules.CanStartGrab(Phase.GrabWindup,35,0,20,30,60,30));
Check("Grab cannot restart pulling", !Rules.CanStartGrab(Phase.Pulling,35,0,20,30,60,30));
Check("Blocked pull retry waits three seconds", !Rules.CanStartGrab(Phase.Charge,35,.01,20,30,60,30));
Check("Unsuccessful pursuit expires at sixty seconds", !Rules.CanStartGrab(Phase.Charge,60,0,20,30,60,30));
Check("Grab rejects non-finite target distance", !Rules.CanStartGrab(Phase.Charge,35,0,double.NaN,30,60,30));
Console.WriteLine($"{passed} core checks passed. These are not Valheim runtime tests.");
