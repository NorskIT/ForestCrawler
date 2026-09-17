using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ForestCrawler;

internal sealed class Network : IDisposable
{
    private const string Rpc = "ForestCrawler.v2";
    private enum Kind { Hello, Welcome, Debug, Action, Notice, Prepare, Candidate, State, Discovery, Pose, Lease, Stop, Clear, Failure, Finished, Landing, Permit, Grab, Progress }
    private sealed class Ready { internal float Seen, Voice, Scream, Reveal; internal bool Assets; }
    private sealed class Actor { internal long Peer; internal string Identity = ""; internal Vector3 Position; internal bool Dead; }
    private sealed class Encounter
    {
        internal string Id = Guid.NewGuid().ToString("N");
        internal EncounterKind Type; internal string Identity = ""; internal Vector3 CatchOrigin, Landing; internal bool Permitted, TeaseWhisper;
        internal long Owner; internal bool Test, SetTime, Activated; internal Phase Phase;
        internal Vector3 Position; internal float Started, PhaseAt, LastPose, LastSeen;
        internal float LastProgress, ProgressAt, NextGrab; internal Vector3 ProgressPosition, PullPosition; internal float PullAt; internal bool Chasing;
        internal int Sequence; internal float Voice, Scream, Reveal, AttackAt;
    }
    private readonly Plugin plugin;
    private readonly Dictionary<long, Ready> ready = new();
    private readonly Dictionary<long, float> alone = new(), requests = new();
    private readonly Cooldowns cooldowns = new();
    private readonly Progression progression = new();
    private readonly System.Random random = new();
    private readonly ScheduleClock schedule = new();
    private ZRoutedRpc? router;
    private Encounter? active;
    private float nextHello, nextTick;
    private double utcHighWater;
    private bool welcomed, disposed;
    private int helloAttempts;
    internal bool Server => ZNet.instance && ZNet.instance.IsServer();
    internal long ServerId => Server ? ZNet.GetUID() : ZNet.instance?.GetServerPeer()?.m_uid ?? 0;
    internal bool Connected => welcomed;
    private float Now => Time.realtimeSinceStartup;
    private double Utc => utcHighWater = Math.Max(utcHighWater, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d);
    internal Network(Plugin plugin) { this.plugin = plugin; }
    internal void Update()
    {
        if (disposed) return;
        if (router != ZRoutedRpc.instance || !ZNet.instance)
        {
            plugin.View.Clear(); active = null; ready.Clear(); alone.Clear(); requests.Clear(); welcomed = false; helloAttempts = 0;
            router = ZNet.instance ? ZRoutedRpc.instance : null;
            if (router != null)
            {
                router.Register<ZPackage>(Rpc, Receive);
                if (Server) { cooldowns.Load(BepInEx.Paths.ConfigPath, ZNet.instance!.GetWorldUID()); progression.Load(BepInEx.Paths.ConfigPath, ZNet.instance.GetWorldUID()); }
                nextHello = nextTick = Now; schedule.Reset(Now);
            }
        }
        if (router == null) return;
        if (Player.m_localPlayer && Now >= nextHello)
        {
            nextHello = Now + (welcomed ? .25f : helloAttempts++ < 3 ? 2 : 30);
            bool assets = plugin.Assets.Ensure();
            Send(ServerId, Kind.Hello, p => { p.Write(Plugin.Version); p.Write(assets); p.Write(plugin.Assets.VoiceLength); p.Write(plugin.Assets.ScreamLength); p.Write(plugin.Assets.RevealLength); });
        }
        if (!Server || Now < nextTick) return;
        nextTick = Now + .1f;
        var actors = Actors(out bool complete);
        foreach (var key in alone.Keys.ToArray()) if (!actors.Any(a => a.Peer == key)) alone.Remove(key);
        foreach (var actor in actors)
        {
            bool isolated = complete && !actor.Dead && !actors.Any(a => a.Peer != actor.Peer && !a.Dead && Vector3.Distance(actor.Position, a.Position) < plugin.Settings.Isolation);
            if (!isolated) alone.Remove(actor.Peer); else if (!alone.ContainsKey(actor.Peer)) alone[actor.Peer] = Now;
        }
        if (active != null)
        {
            var e = active;
            string reason = Eligibility(e.Owner, actors, complete, e.Test, !e.Activated, e.SetTime && !e.Activated, null);
            if (e.Activated) reason = Eligibility(e.Owner, actors, complete, e.Test || TerminalLanding(e, actors), false, false, e.Position);
            if (e.Permitted && actors.Any(a => a.Peer != e.Owner && !a.Dead && Vector3.Distance(a.Position, e.Landing) < plugin.Settings.Isolation)) reason = "Landing isolation changed.";
            if (reason != "" || Now - e.Started > plugin.Settings.Timeout || Now - e.LastSeen > 2)
            { Stop(reason != "" ? reason : "Encounter timed out or presentation disconnected."); return; }
            if ((e.Phase == Phase.Charge || e.Phase == Phase.GrabWindup) && Now-e.LastProgress >= plugin.Settings.PursuitTimeout)
            { Stop("Native pursuit made no reachable progress before its deadline."); return; }
            if (e.Phase == Phase.GrabWindup && Now-e.PhaseAt > plugin.Settings.GrabExtension + 2)
            { Stop("Arm contact report timed out."); return; }
            if (e.Phase == Phase.Pulling)
            {
                var pulled = actors.FirstOrDefault(a => a.Peer == e.Owner);
                if (pulled == null || Vector3.Distance(pulled.Position,e.PullPosition) > plugin.Settings.PullSpeed*(Now-e.PullAt)+2 ||
                    Now-e.PhaseAt > plugin.Settings.GrabRange/plugin.Settings.PullSpeed+3)
                { Stop("Pull exceeded authorized movement or duration."); return; }
                // Unchanged shared-state samples do not advance the observation clock.
                // A later batched ZDO position must receive its full movement interval.
                if (Vector3.Distance(pulled.Position,e.PullPosition) > .01f)
                { e.PullPosition = pulled.Position; e.PullAt = Now; }
            }
            if (Rules.LureExpired(e.Type, e.Phase, Now - e.PhaseAt)) Change(Phase.Reveal);
            if (e.Phase == Phase.Watching && Now - e.PhaseAt >= e.Voice) Change(Phase.Reveal);
            if (e.Phase == Phase.Caught && Now - e.PhaseAt > 5.5f) { Stop("Capture deadline expired."); return; }
            if (e.Phase == Phase.Reveal && Now - e.PhaseAt >= e.Reveal) Change(Phase.Charge);
            if (e.Phase == Phase.Stare && Now - e.PhaseAt >= .5f) { Change(Phase.Relocating); Prepare(); }
            if (e.Phase == Phase.Tail && Now - e.AttackAt > e.Scream + 2) { Stop("Completed."); return; }
            if (active != null) Send(e.Owner, Kind.Lease, p => { p.Write(e.Id); p.Write(e.Sequence); });
        }
        if (!schedule.Due(Now)) return;
        if (active != null || !plugin.Settings.Natural || !complete || actors.Count == 0) return;
        if (random.NextDouble() >= Rules.TriggerChance(plugin.Settings.Rate, actors.Count, 1)) return;
        var candidates = actors.Where(a => EligibleAssets(a.Peer) && Eligibility(a.Peer, actors, complete, false, true, false, null) == "").ToArray();
        if (candidates.Length > 0) Begin(candidates[random.Next(candidates.Length)].Peer, false, false);
    }
    private List<Actor> Actors(out bool complete)
    {
        complete = true; var result = new List<Actor>();
        foreach (var info in ZNet.instance.GetPlayerList())
        {
            var zdo = ZDOMan.instance.GetZDO(info.m_characterID);
            long peer = info.m_characterID == ZNet.instance.LocalPlayerCharacterID ? ZNet.GetUID()
                : ZNet.instance.GetPeers().FirstOrDefault(p => p.m_characterID == info.m_characterID)?.m_uid ?? 0;
            if (zdo == null || peer == 0 || !World.Finite(zdo.GetPosition())) { complete = false; continue; }
            if (!ready.TryGetValue(peer, out var heartbeat) || Now - heartbeat.Seen > 1) complete = false;
            result.Add(new Actor { Peer = peer, Identity = info.m_userInfo.m_id.ToString(), Position = zdo.GetPosition(), Dead = zdo.GetBool(ZDOVars.s_dead) });
        }
        // Unknown joining peers block encounters, including players without map sharing.
        if (ZNet.instance.GetPeers().Any(p => p.IsReady() && !result.Any(a => a.Peer == p.m_uid))) complete = false;
        return result;
    }
    private static bool TerminalLanding(Encounter e, List<Actor> actors) => e.Phase == Phase.Caught && e.Permitted &&
        actors.Any(a => a.Peer == e.Owner && Vector3.Distance(a.Position, e.Landing) < 3);
    private bool Night() => EnvMan.instance && Rules.InWindow(Rules.Fraction(ZNet.instance.GetTimeSeconds(), EnvMan.instance.m_dayLengthSec), plugin.Settings.Start, plugin.Settings.End);
    private string Eligibility(long owner, List<Actor> actors, bool complete, bool test, bool initial, bool skipTime, Vector3? creature)
    {
        var target = actors.FirstOrDefault(a => a.Peer == owner);
        if (!complete) return "Player state is incomplete; isolation cannot be confirmed.";
        if (target == null || target.Dead) return "Target disconnected or dead.";
        if (!EligibleAssets(owner)) return "Target client assets/protocol are unavailable.";
        if (actors.Any(a => a.Peer != owner && !a.Dead && (Vector3.Distance(a.Position, target.Position) < plugin.Settings.Isolation || (creature.HasValue && Vector3.Distance(a.Position, creature.Value) < plugin.Settings.Isolation)))) return "Another living player entered the isolation radius.";
        if (initial && (!alone.TryGetValue(owner, out float since) || Now - since < plugin.Settings.Alone)) return "Requires 30 continuous seconds of confirmed isolation.";
        if (!test)
        {
            if (!World.Allowed(target.Position) || (creature.HasValue && !World.Allowed(creature.Value))) return "Player or creature is outside the allowed biomes.";
            if (!skipTime && !Night()) return "Outside the configured midnight window.";
            if (initial && progression.Remaining(target.Identity, Utc) > 0) return $"Quiet period: {progression.Remaining(target.Identity, Utc) / 60:F1} real minutes remaining.";
        }
        return "";
    }
    private bool EligibleAssets(long peer) => ready.TryGetValue(peer, out var r) && r.Assets && Now - r.Seen < 6;
    internal void Debug(string command)
    {
        if (!Player.m_localPlayer || router == null) { plugin.Notice("Enter a world first."); return; }
        if (!welcomed && !Server)
        {
            if (command == "spawn" || command.StartsWith("anim ")) { plugin.Notice("Server plugin unavailable; local preview only."); plugin.View.Debug(command); }
            else plugin.Notice("Server handshake unavailable. Install matching ForestCrawler on the server for encounters.");
            return;
        }
        Send(ServerId, Kind.Debug, p => p.Write(command));
    }
    internal void ClearOwn() { if (router != null) Send(ServerId, Kind.Clear); }
    internal void Candidate(string id, int sequence, Vector3 position, List<Vector3> route)
        => Send(ServerId, Kind.Candidate, p => { p.Write(id); p.Write(sequence); p.Write(position); p.Write(route.Count); foreach (var v in route) p.Write(v); });
    internal void Discovery(string id, int sequence, bool close, float gaze) => Send(ServerId, Kind.Discovery, p => { p.Write(id); p.Write(sequence); p.Write(close); p.Write(gaze); });
    internal void Pose(string id, int sequence, Vector3 position) => Send(ServerId, Kind.Pose, p => { p.Write(id); p.Write(sequence); p.Write(position); });
    internal void Failure(string id, string reason) => Send(ServerId, Kind.Failure, p => { p.Write(id); p.Write(reason); });
    internal void Finished(string id, int sequence) => Send(ServerId, Kind.Finished, p => { p.Write(id); p.Write(sequence); });
    internal void Grab(string id, int seq, int action) => Send(ServerId, Kind.Grab, p => { p.Write(id); p.Write(seq); p.Write(action); });
    internal void Progress(string id, int seq) => Send(ServerId, Kind.Progress, p => { p.Write(id); p.Write(seq); });
    internal void Landing(string id, int seq, Vector3 point) => Send(ServerId, Kind.Landing, p => { p.Write(id); p.Write(seq); p.Write(point); });
    private void Send(long target, Kind kind, Action<ZPackage>? write = null)
    {
        if (router == null || target == 0) return;
        var p = new ZPackage(); p.Write(Rules.Protocol); p.Write((int)kind); write?.Invoke(p); router.InvokeRoutedRPC(target, Rpc, p);
    }
    private void Notice(long peer, string message) => Send(peer, Kind.Notice, p => p.Write(message));
    private bool Known(long peer) => peer == ZNet.GetUID() || ZNet.instance.GetPeer(peer)?.IsReady() == true;
    private bool Admin(long peer) => peer == ZNet.GetUID() || (ZNet.instance.GetPeer(peer) is { } p && p.IsReady() && ZNet.instance.IsAdmin(p.m_socket.GetHostName()));
    private void Receive(long sender, ZPackage p)
    {
        if (disposed || !ZNet.instance || p.Size() > 16384) return;
        try
        {
            if (p.ReadInt() != Rules.Protocol) return;
            var kind = (Kind)p.ReadInt(); bool fromServer = sender == ServerId;
            if (Server && !Known(sender)) return;
            switch (kind)
            {
                case Kind.Hello when Server:
                    string version = p.ReadString(); bool assets = p.ReadBool(); float voice = p.ReadSingle(), scream = p.ReadSingle(), reveal = p.ReadSingle();
                    ready[sender] = new Ready { Seen = Now, Assets = assets && version == Plugin.Version && ValidLength(voice) && ValidLength(scream) && ValidLength(reveal), Voice = voice, Scream = scream, Reveal = reveal };
                    Send(sender, Kind.Welcome); break;
                case Kind.Welcome when fromServer: welcomed = true; break;
                case Kind.Debug when Server: DebugRequest(sender, p.ReadString()); break;
                case Kind.Action when fromServer: plugin.View.Debug(p.ReadString()); break;
                case Kind.Notice when fromServer: plugin.Notice(p.ReadString()); break;
                case Kind.Prepare when fromServer:
                    plugin.View.Prepare(p.ReadString(), p.ReadInt(), p.ReadBool(), p.ReadBool(), p.ReadVector3(), JsonUtility.FromJson<Settings>(p.ReadString()), (EncounterKind)p.ReadInt()); break;
                case Kind.Candidate when Server: AcceptCandidate(sender, p); break;
                case Kind.State when fromServer: plugin.View.State(p.ReadString(), p.ReadInt(), (Phase)p.ReadInt(), p.ReadVector3()); break;
                case Kind.Discovery when Server: AcceptDiscovery(sender, p); break;
                case Kind.Pose when Server: AcceptPose(sender, p); break;
                case Kind.Lease when fromServer: plugin.View.Lease(p.ReadString(), p.ReadInt()); break;
                case Kind.Stop when fromServer: plugin.View.Stop(p.ReadString(), p.ReadString()); break;
                case Kind.Clear when Server: if (active?.Owner == sender) Stop("Cleared by target."); break;
                case Kind.Failure when Server:
                    string id = p.ReadString(); string error = p.ReadString(); if (active?.Owner == sender && active.Id == id) Stop(error); break;
                case Kind.Permit when fromServer: plugin.View.Permit(p.ReadString(), p.ReadInt(), p.ReadVector3()); break;
                case Kind.Landing when Server:
                    if (Match(sender, p, out var landing) && landing.Phase == Phase.Caught && !landing.Permitted)
                    {
                        var point = p.ReadVector3(); var roster = Actors(out bool valid);
                        float distance = Vector3.ProjectOnPlane(point - landing.CatchOrigin, Vector3.up).magnitude;
                        if (!valid || !World.Finite(point) || distance < 200 || distance > 500 || point.y < ZoneSystem.instance.m_waterLevel + 1 ||
                            Mathf.Abs(WorldGenerator.instance.GetHeight(point.x, point.z, out _) - point.y) > 8 ||
                            roster.Any(a => a.Peer != sender && !a.Dead && Vector3.Distance(a.Position, point) < plugin.Settings.Isolation)) break;
                        landing.Permitted = true; landing.Landing = point;
                        Send(sender, Kind.Permit, q => { q.Write(landing.Id); q.Write(landing.Sequence); q.Write(point); });
                    }
                    break;
                case Kind.Progress when Server:
                    if (Match(sender,p,out var progress) && progress.Phase == Phase.Charge && Now-progress.ProgressAt >= .3f)
                    {
                        var target = Actors(out bool valid).FirstOrDefault(a => a.Peer == sender);
                        if (valid && target != null && Vector3.Dot(progress.Position-progress.ProgressPosition,(target.Position-progress.ProgressPosition).normalized) >= .1f)
                            progress.LastProgress = Now;
                        progress.ProgressPosition = progress.Position; progress.ProgressAt = Now;
                    }
                    break;
                case Kind.Grab when Server:
                    if (Match(sender,p,out var grab))
                    {
                        int action = p.ReadInt(); var roster = Actors(out bool valid); var target = roster.FirstOrDefault(a => a.Peer == sender);
                        if (!valid || target == null || Eligibility(sender,roster,valid,grab.Test,false,false,grab.Position) != "") break;
                        float distance = Vector3.Distance(target.Position,grab.Position);
                        if (action == 0 && grab.Phase == Phase.Charge)
                        {
                            if (Rules.CanStartGrab(grab.Phase, Now-grab.LastProgress, grab.NextGrab-Now, distance, plugin.Settings.GrabDelay, plugin.Settings.PursuitTimeout, plugin.Settings.GrabRange + .5f))
                                Change(Phase.GrabWindup);
                            else Change(Phase.Charge);
                        }
                        else if (action == 1 && grab.Phase == Phase.GrabWindup && Now-grab.PhaseAt >= plugin.Settings.GrabExtension && distance <= plugin.Settings.GrabRange + .5f)
                        { grab.PullPosition = target.Position; grab.PullAt = Now; Change(Phase.Pulling); }
                        else if (action == 2 && (grab.Phase == Phase.GrabWindup || grab.Phase == Phase.Pulling))
                        { grab.NextGrab = Now+3; Change(Phase.Charge); }
                    }
                    break;
                case Kind.Finished when Server:
                    if (Match(sender, p, out var end))
                    {
                        if (end.Phase == Phase.Charge || end.Phase == Phase.Pulling)
                        {
                            var roster = Actors(out bool valid); var target = roster.FirstOrDefault(a => a.Peer == sender);
                            if (valid && target != null && Vector3.Distance(target.Position, end.Position) <= 3.5f)
                            { end.CatchOrigin = target.Position; Change(Phase.Caught); }
                        }
                        else if (end.Phase == Phase.Tease && Now - end.PhaseAt >= (end.TeaseWhisper ? AudioTiming.Whisper : AudioTiming.Woo) - .1f)
                        {
                            try { if (!end.Test) progression.CompleteTease(end.Identity); }
                            catch (Exception persistenceError) when (persistenceError is System.IO.IOException || persistenceError is UnauthorizedAccessException)
                            { plugin.Log("Tease persistence failed: " + persistenceError.Message); Stop("Unable to save tease completion."); break; }
                            Stop("Tease completed.");
                        }
                        else if (end.Phase == Phase.Caught && Now - end.PhaseAt >= 3.9f) Stop("Capture completed.");
                    }
                    break;
            }
        }
        catch (Exception e) { plugin.Log("Rejected malformed message: " + e.GetType().Name); }
    }
    private static bool ValidLength(float value) => Rules.Finite(value) && value > 0 && value < 120;
    private bool Match(long sender, ZPackage p, out Encounter e)
    {
        string id = p.ReadString(); int sequence = p.ReadInt(); e = active!;
        return e != null && e.Owner == sender && e.Id == id && e.Sequence == sequence;
    }
    private void DebugRequest(long sender, string command)
    {
        if (requests.TryGetValue(sender, out float last) && Now - last < .2f) return; requests[sender] = Now;
        var actors = Actors(out bool complete);
        if (command == "status")
        {
            string reason = Eligibility(sender, actors, complete, false, true, false, null);
            var actor = actors.FirstOrDefault(a => a.Peer == sender);
            double remaining = actor == null ? 0 : cooldowns.Remaining(actor.Identity, Utc);
            float solitude = alone.TryGetValue(sender, out float since) ? Now - since : 0;
            Notice(sender, $"authority=server; phase={active?.Phase.ToString() ?? "none"}; type={active?.Type.ToString() ?? "none"}; hasNaturalTease={(actor != null && progression.HasTease(actor.Identity))}; quietMinutes={(actor == null ? 0 : progression.Remaining(actor.Identity, Utc) / 60):F1}; online={actors.Count}; eligibility={(reason == "" ? "ready" : reason)}; natural={plugin.Settings.Natural}; rate={plugin.Settings.Rate:F4}/eligible-hour/N; cooldownRemaining={remaining / 60:F1} real minutes; cooldownMinimum={plugin.Settings.Cooldown}m; isolation={solitude:F1}s; time={Rules.Fraction(ZNet.instance.GetTimeSeconds(), EnvMan.instance.m_dayLengthSec):F4}"); return;
        }
        if (!Admin(sender)) { Notice(sender, "Multiplayer debug commands require server administrator permission."); return; }
        if (command == "spawn" || command == "anim idle" || command == "anim scream" || command == "anim charge")
        { if (active?.Owner == sender) Stop("Replaced by preview."); Send(sender, Kind.Action, p => p.Write(command)); return; }
        if (command == "start" || command == "encounter" || command == "encounter tease")
        {
            if (active != null) { Notice(sender, "An encounter already owns the server slot."); return; }
            string reason = Eligibility(sender, actors, complete, command.StartsWith("encounter"), true, command == "start", null);
            if (reason != "") { Notice(sender, reason); return; }
            Begin(sender, command.StartsWith("encounter"), command == "start", command == "encounter tease");
        }
    }
    private void Begin(long owner, bool test, bool setTime, bool tease = false)
    {
        var actor = Actors(out _).FirstOrDefault(a => a.Peer == owner); if (actor == null) return;
        var type = test ? (tease ? EncounterKind.Tease : EncounterKind.Full) :
            Rules.SelectKind(progression.HasTease(actor.Identity), random.NextDouble());
        if (!test && !Rules.CanStart(type, progression.Remaining(actor.Identity, Utc), cooldowns.Remaining(actor.Identity, Utc)))
        { Notice(owner, "Full encounter cooldown active; draw skipped without reroll."); return; }
        var r = ready[owner]; active = new Encounter { TeaseWhisper = random.NextDouble() < .5, Type = type, Identity = actor.Identity, Owner = owner, Test = test, SetTime = setTime, Started = Now, PhaseAt = Now, LastSeen = Now, Phase = Phase.Preparing, Voice = AudioTiming.Voice, Scream = AudioTiming.Scream, Reveal = AudioTiming.Reveal };
        Prepare();
    }
    private void Prepare()
    {
        var e = active!; var settings = JsonUtility.FromJson<Settings>(plugin.Settings.Serialize());
        settings.TeaseWhisper = e.TeaseWhisper; settings.VoiceLength = e.Voice; settings.ScreamLength = e.Scream; settings.RevealLength = e.Reveal;
        Send(e.Owner, Kind.Prepare, p => { p.Write(e.Id); p.Write(e.Sequence); p.Write(e.Test); p.Write(e.Phase == Phase.Relocating); p.Write(e.Position); p.Write(settings.Serialize()); p.Write((int)e.Type); });
    }
    private void AcceptCandidate(long sender, ZPackage p)
    {
        if (!Match(sender, p, out var e) || (e.Phase != Phase.Preparing && e.Phase != Phase.Relocating)) return;
        var position = p.ReadVector3(); int count = p.ReadInt(); if (!World.Finite(position) || count < 2 || count > 512) return;
        var route = new List<Vector3>(); for (int i = 0; i < count; i++) { var v = p.ReadVector3(); if (!World.Finite(v)) return; route.Add(v); }
        var actors = Actors(out bool complete); var target = actors.FirstOrDefault(a => a.Peer == sender); if (target == null) { Stop("Target left."); return; }
        string reason = Eligibility(sender, actors, complete, e.Test, !e.Activated, e.SetTime && !e.Activated, position);
        float distance = Vector3.Distance(position, target.Position); bool relocate = e.Phase == Phase.Relocating;
        var s = plugin.Settings;
        if (reason != "" || distance < (relocate ? s.RelocateMin : s.LureMin) - 2 || distance > (relocate ? s.RelocateMax : s.LureMax) + 2 ||
            Vector3.Distance(route[0], position) > 3 || Vector3.Distance(route.Last(), target.Position) > 5 ||
            (relocate && Vector3.Angle(position - target.Position, e.Position - target.Position) < 90)) { Stop(reason == "" ? "Invalid candidate location." : reason); return; }
        Vector3 previous = position;
        foreach (var corner in route)
        {
            float length = Vector3.Distance(previous, corner); if (length > 150) { Stop("Invalid route."); return; }
            for (int i = 0; i <= Mathf.CeilToInt(length); i++)
            {
                var sample = Vector3.Lerp(previous, corner, i / Math.Max(1, length));
                if ((!e.Test && !World.Allowed(sample)) || actors.Any(a => a.Peer != sender && !a.Dead && Vector3.Distance(a.Position, sample) < s.Isolation)) { Stop("Route violates eligibility."); return; }
            }
            previous = corner;
        }
        if (e.SetTime && !e.Activated)
        {
            ZNet.instance.SetNetTime(Rules.NextWindow(ZNet.instance.GetTimeSeconds(), EnvMan.instance.m_dayLengthSec, s.Start) + .05);
            Notice(sender, "Shared world time advanced to the midnight window for all players.");
        }
        if (!e.Test && !Night()) { Stop("Midnight window ended before activation."); return; }
        if (actors.Any(a => a.Peer != sender && !a.Dead && Vector3.Distance(a.Position, position) < s.Isolation)) { Stop("Isolation changed before activation."); return; }
        e.Position = position; e.LastPose = e.LastSeen = Now;
        if (!e.Activated)
        {
            if (!e.Test)
            {
                try { progression.Start(target.Identity, Utc); if (e.Type == EncounterKind.Full) cooldowns.Start(target.Identity, Utc, s.Cooldown); }
                catch (System.IO.IOException error) { plugin.Log("Cooldown persistence failed: " + error.Message); Stop("Unable to save cooldown; encounter cancelled."); return; }
                catch (UnauthorizedAccessException error) { plugin.Log("Cooldown persistence failed: " + error.Message); Stop("Unable to save cooldown; encounter cancelled."); return; }
            }
            e.Activated = true; e.Started = Now;
        }
        Change(relocate ? Phase.Watching : e.Type == EncounterKind.Tease ? Phase.Tease : Phase.Lure);
    }
    private void AcceptDiscovery(long sender, ZPackage p)
    {
        if (!Match(sender, p, out var e)) return;
        bool close = p.ReadBool(); float gaze = p.ReadSingle();
        if (!Rules.Finite(gaze) || !Rules.CanDiscover(e.Phase, Now - e.PhaseAt, e.Voice)) return;
        var actors = Actors(out bool complete); var target = actors.FirstOrDefault(a => a.Peer == sender);
        if (target == null || Eligibility(sender, actors, complete, e.Test, false, false, e.Position) != "") { Stop("Eligibility changed."); return; }
        if (Vector3.Distance(target.Position, e.Position) > (close ? plugin.Settings.CloseDistance + 1 : plugin.Settings.GazeDistance + 6)) return;
        if (!close && (gaze < plugin.Settings.GazeSeconds || Now - e.PhaseAt < plugin.Settings.GazeSeconds)) return;
        if (e.Phase == Phase.Lure) Change(Phase.Stare); else Change(Phase.Reveal);
    }
    private void AcceptPose(long sender, ZPackage p)
    {
        if (!Match(sender, p, out var e)) return;
        var position = p.ReadVector3(); if (!World.Finite(position)) return;
        if (e.Phase == Phase.Preparing || e.Phase == Phase.Relocating) { e.LastSeen = Now; return; }
        float distance = Vector3.Distance(position, e.Position);
        if (distance > (e.Phase == Phase.Charge ? 18 * Math.Min(1, Now - e.LastPose) + 1 : .1f)) { Stop("Movement outside authorized envelope."); return; }
        var actors = Actors(out bool complete);
        string reason = Eligibility(sender, actors, complete, e.Test || TerminalLanding(e, actors), false, false, position);
        if (reason != "") { Stop(reason); return; }
        e.Position = position; e.LastPose = e.LastSeen = Now;
    }
    private void Change(Phase phase)
    {
        var e = active!; e.Phase = phase; e.PhaseAt = Now; e.Sequence++;
        if (phase == Phase.Reveal) e.AttackAt = Now;
        if (phase == Phase.Charge && !e.Chasing)
        { e.Chasing = true; e.LastProgress = e.ProgressAt = Now; e.ProgressPosition = e.Position; }
        Send(e.Owner, Kind.State, p => { p.Write(e.Id); p.Write(e.Sequence); p.Write((int)e.Phase); p.Write(e.Position); });
    }
    private void Stop(string reason)
    {
        var e = active; active = null; if (e == null) return;
        Send(e.Owner, Kind.Stop, p => { p.Write(e.Id); p.Write(reason); }); plugin.Log($"Encounter {e.Id}: {reason}");
    }
    public void Dispose() { if (Server && active != null) Stop("Plugin unloaded."); disposed = true; router = null; }
}
