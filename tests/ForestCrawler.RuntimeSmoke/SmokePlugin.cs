using System;
using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ForestCrawler.RuntimeSmoke;

// Test-only plugin. Runs solely with an explicit output directory and isolated -savedir.
// Never packaged or installed into a user's mod profile.
[BepInPlugin("norskit.ForestCrawler.smoke", "ForestCrawler Runtime Smoke", "0.1.0")]
[BepInDependency(Plugin.Id)]
[DefaultExecutionOrder(9999)]
public sealed class SmokePlugin : BaseUnityPlugin
{
    private string output = "";
    private bool failed;
    private float deadline;
    private Vector3 chargeStart;
    private float aimUntil;
    private bool fleeDuringCharge;
    private float nextJump;
    private int jumps, airborneFrames;
    private static bool forceRouteFailure, forceMovementFailure;
    private static bool BlockRoute(ref bool __result) { if(!forceRouteFailure) return true; __result=false; return false; }
    private static bool BlockMovement(ref bool __result) { if(!forceMovementFailure) return true; __result=false; return false; }
    private Vector3 fleeOrigin, fleeDirection;
    private static SmokePlugin? running;
    private Plugin Mod => Plugin.Instance;
    private bool Try(Action action)
    {
        try { action(); return true; }
        catch (Exception e) { failed = true; File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString()); Logger.LogError(e); Application.Quit(); return false; }
    }
    private void Check(bool condition, string message) { if (!condition) throw new Exception(message); File.AppendAllText(Path.Combine(output,"checks.txt"),"PASS: "+message+"\n"); }
    private static void FleeInput(Player __instance, ref Vector3 movedir, ref bool run)
    {
        var test=running;
        if (test==null || !test.fleeDuringCharge || ForestCrawler.Capture.Active || __instance!=Player.m_localPlayer || !test.Mod.View.Status.Contains("presentation=Charge")) return;
        if(test.fleeDirection.sqrMagnitude>.01f) { __instance.SetLookDir(test.fleeDirection); movedir=Vector3.forward; run=true; }
    }
    private IEnumerator MusicRegression()
    {
        var manager=MusicMan.instance;
        var actual=(AudioSource)AccessTools.Field(typeof(MusicMan),"m_musicSource").GetValue(manager);
        var first=manager.GetComponentInChildren<AudioSource>(true);
        Logger.LogInfo("Music source diagnostic: first="+first.name+", actual="+actual.name+", same="+(first==actual));
        float previousMaster=MusicMan.m_masterMusicVolume;
        float savedMusicPreference=PlatformPrefs.GetFloat("MusicVolume",1);
        var decoy=manager.gameObject.AddComponent<AudioSource>();
        decoy.mute=false;
        try
        {
            MusicMan.m_masterMusicVolume=.75f;
            var track=manager.m_music.First(m=>m.m_enabled && m.m_clips.Length>0 && m.m_clips[0]);
            manager.TriggerMusic(track.m_name);
            yield return new WaitForSecondsRealtime(6);
            if(!Try(()=>
            {
                Check(actual.isPlaying && actual.volume>0 && !actual.mute,"Actual MusicMan source plays at nonzero volume before encounter");
                Check(manager.GetComponentInChildren<AudioSource>(true)!=actual,"Fixture distinguishes hierarchy lookup from actual music source");
            })) yield break;
            foreach(var kind in new[]{EncounterKind.Tease,EncounterKind.Full})
            {
                string id="music-fixture-"+kind; int seq=0;
                Mod.View.Prepare(id,seq,true,false,Player.m_localPlayer.transform.position,Mod.Settings,kind);
                var phases=kind==EncounterKind.Tease ? new[]{Phase.Tease} : new[]{Phase.Lure,Phase.Stare,Phase.Relocating,Phase.Watching,Phase.Reveal,Phase.Charge,Phase.Caught};
                forceRouteFailure=forceMovementFailure=true;
                foreach(var phase in phases)
                {
                    if(!Try(()=>Mod.View.State(id,++seq,phase,Player.m_localPlayer.transform.position+Vector3.forward*12))) yield break;
                    float until=Time.realtimeSinceStartup+.4f;
                    while(Time.realtimeSinceStartup<until) { Mod.View.Lease(id,seq); yield return null; }
                    if(!Try(()=>
                    {
                        CheckMusic(true,kind+" "+phase+" mutes exact playing MusicMan source");
                        Check(!decoy.mute,"Unrelated source remains unmuted in "+phase);
                        MusicMan.m_masterMusicVolume=.9f;
                        Check(actual.isPlaying,"Music scheduling continues under mute in "+phase);
                    })) yield break;
                }
                if(!Try(()=>
                {
                    Mod.View.Clear();
                    CheckMusic(false,kind+" cleanup restores actual music source");
                    Check(MusicMan.m_masterMusicVolume==.9f,"Cleanup preserves volume changes made during encounter");
                })) yield break;
            }
            if(!Try(()=>
            {
                Check(PlatformPrefs.GetFloat("MusicVolume",1)==savedMusicPreference,"Encounter never changes saved MusicVolume preference");
                Logger.LogInfo("Focused music regression passed; continuing to elevated rock target fixture.");
            })) yield break;
        }
        finally
        {
            forceRouteFailure=forceMovementFailure=false;
            Mod.View.Clear(); MusicMan.m_masterMusicVolume=previousMaster;
            if(decoy) UnityEngine.Object.Destroy(decoy);
        }
        yield return RockRegression();
        if(!failed) File.WriteAllText(Path.Combine(output,"verified.txt"),"Focused actual-Valheim regression passed: actual MusicMan source with nonzero track volume, independent source inspection, decoy and injected presentation phases; production charge motor climbs an isolated rock fixture to an elevated player. Not a natural full encounter or human listening test.");
        Application.Quit();
    }
    private IEnumerator RockRegression()
    {
        var origin=Player.m_localPlayer.transform.position;
        var center=origin+Vector3.up*100;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name="SmokeRockFloor"; floor.transform.position=center+Vector3.down*.5f; floor.transform.localScale=new Vector3(80,1,80);
        var rock=new GameObject("SmokeClimbableRock"); rock.transform.position=center;
        var mesh=new Mesh(); mesh.vertices=new[]{new Vector3(-3,0,0),new Vector3(3,0,0),new Vector3(-3,3,0),new Vector3(3,3,0),new Vector3(-3,0,6),new Vector3(3,0,6)};
        mesh.triangles=new[]{0,2,1,1,2,3,2,4,3,3,4,5,0,4,2,1,3,5,0,1,4,1,5,4}; mesh.RecalculateNormals();
        rock.AddComponent<MeshCollider>().sharedMesh=mesh; Physics.SyncTransforms();
        try
        {
            Player.m_localPlayer.TeleportTo(center+new Vector3(0,2.7f,1),Quaternion.identity,true);
            yield return new WaitForSecondsRealtime(5);
            string id="rock-fixture"; int seq=1;
            if(!Try(()=>
            {
                Check(Player.m_localPlayer.transform.position.y>center.y+2,"Rock fixture player stands on elevated collider");
                Mod.View.Prepare(id,0,true,false,center+new Vector3(0,0,-5),Mod.Settings,EncounterKind.Full);
                Mod.View.State(id,seq,Phase.Lure,center+new Vector3(0,0,-5));
                Mod.View.State(id,++seq,Phase.Charge,center+new Vector3(0,0,-5));
            })) yield break;
            float until=Time.realtimeSinceStartup+15, maximumHeight=0, maximumZ=0;
            bool finished=false;
            while(Time.realtimeSinceStartup<until)
            {
                Mod.View.Lease(id,seq);
                var creature=GameObject.Find("ForestCrawler_Local");
                if(creature) { maximumHeight=Mathf.Max(maximumHeight,creature.transform.position.y-center.y); maximumZ=Mathf.Max(maximumZ,creature.transform.position.z-center.z); }
                finished=(bool)AccessTools.Field(typeof(Presentation),"awaitingFinish").GetValue(Mod.View);
                if(finished) break;
                yield return null;
            }
            if(!Try(()=>
            {
                Check(finished,"Production charge reaches player on rock: "+Mod.View.Status+"; route="+ForestCrawler.World.LastRouteFailure);
                Check(maximumHeight>1 && maximumZ>3,"Charge physically goes around steep face and climbs accessible rock side");
                CheckMusic(true,"Rock pursuit retains actual music suppression");
            })) yield break;
        }
        finally
        {
            Mod.View.Clear(); UnityEngine.Object.Destroy(rock); UnityEngine.Object.Destroy(mesh); UnityEngine.Object.Destroy(floor);
            Player.m_localPlayer.TeleportTo(origin,Quaternion.identity,true);
        }
    }
    private void CheckMusic(bool muted, string message)
    {
        var source=MusicMan.instance ? (AudioSource)AccessTools.Field(typeof(MusicMan),"m_musicSource").GetValue(MusicMan.instance) : null;
        Check(source && source.mute==muted,message);
    }
    private static bool NoCloud(ref bool __result) { __result=false; return false; }
    private static bool IsolatedSavePath(ref string __result) { __result=Path.Combine(Environment.GetEnvironmentVariable("FORESTCRAWLER_SMOKE_OUTPUT")!,"saves"); return false; }
    private void Awake()
    {
        output=Environment.GetEnvironmentVariable("FORESTCRAWLER_SMOKE_OUTPUT") ?? "";
        if(output=="") { enabled=false; return; }
        if(!Environment.GetCommandLineArgs().Contains("-savedir")) throw new Exception("Smoke run requires isolated save directory");
        running=this;
        var isolation=new Harmony("norskit.ForestCrawler.smoke.cloud");
        isolation.Patch(AccessTools.PropertyGetter(typeof(FileHelpers),"CloudStorageSupportedAndEnabled"),prefix:new HarmonyMethod(typeof(SmokePlugin),nameof(NoCloud)));
        isolation.Patch(AccessTools.Method(typeof(Utils),"GetSaveDataPath"),prefix:new HarmonyMethod(typeof(SmokePlugin),nameof(IsolatedSavePath)));
        isolation.Patch(AccessTools.Method(typeof(Player),"SetControls"),prefix:new HarmonyMethod(typeof(SmokePlugin),nameof(FleeInput)));
        isolation.Patch(AccessTools.Method(typeof(ForestCrawler.World),"ChargeRoute"),prefix:new HarmonyMethod(typeof(SmokePlugin),nameof(BlockRoute)));
        isolation.Patch(AccessTools.Method(typeof(ForestCrawler.World),"ChaseSegment"),prefix:new HarmonyMethod(typeof(SmokePlugin),nameof(BlockMovement)));
        Utils.SetSaveDataPath(Path.Combine(output,"saves"));
        deadline=Time.realtimeSinceStartup+360;
    }
    private void Update()
    {
        if (fleeDuringCharge && Player.m_localPlayer && Mod.View.Status.Contains("presentation=Charge"))
        {
            var creature = GameObject.Find("ForestCrawler_Local");
            if (creature)
            {
                var player = Player.m_localPlayer;
                if(!player.IsOnGround()) airborneFrames++;
                if(Time.realtimeSinceStartup>=nextJump && player.IsOnGround())
                {
                    player.Jump(); jumps++; nextJump=Time.realtimeSinceStartup+1.1f;
                }
                var away = Vector3.ProjectOnPlane(player.transform.position-creature.transform.position,Vector3.up).normalized;
                foreach (float angle in new[] {0f,30f,-30f,60f,-60f,90f,-90f})
                {
                    var direction=Quaternion.Euler(0,angle,0)*away;
                    if (!ForestCrawler.World.Ground(player.transform.position+direction*1.2f,out var ground) || !ForestCrawler.World.SegmentClear(player.transform.position,ground)) continue;
                    fleeDirection=direction;
                    player.SetLookDir(direction);
                    player.SetControls(Vector3.forward,false,false,false,false,false,false,false,false,true,false);
                    break;
                }
            }
        }
        if(output!="" && !failed && Time.realtimeSinceStartup>deadline) Try(()=>throw new TimeoutException("Smoke test exceeded six minutes"));
    }
    private IEnumerator Start()
    {
        if(output=="") yield break;
        yield return new WaitForSecondsRealtime(15);
        if(Environment.GetEnvironmentVariable("FORESTCRAWLER_SMOKE_ASSETS_ONLY")=="1")
        {
            Try(()=>
            {
                Check(Mod.Assets.Ensure(),"Final runtime bundle and all animation/face references load in Valheim");
                Check(Mod.Assets.Audio.Count==8,"All eight runtime audio keys resolve");
                var beat=Mod.Assets.Audio["heartbeat"]; var samples=new float[beat.samples];
                Check((bool)AccessTools.Method(typeof(AudioClip),"GetData",new[]{typeof(float[]),typeof(int)}).Invoke(beat,new object[]{samples,0}) && samples.Max(x=>Mathf.Abs(x))>.1f,"Imported pulse contains decoded PCM samples in game");
                Check(beat.channels==1 && beat.length<=.241f && Mathf.Abs(samples[0])<.001f && Mathf.Abs(samples[samples.Length-1])<.001f,"Pulse duration and click-free edges survive bundling");
                File.WriteAllText(Path.Combine(output,"verified.txt"),"Actual Valheim final asset-load smoke passed. No world was loaded in this focused run.");
                Application.Quit();
            });
            yield break;
        }
        if(!Try(()=>
        {
            Check(Mod.Assets.Ensure(),"Asset bundle and three clips/eight audio assets and closeup morphs load in actual Valheim");
            Check(Path.GetFullPath(Utils.GetSaveDataPath(FileHelpers.FileSource.Local))==Path.GetFullPath(Path.Combine(output,"saves")),"Game save APIs point only to the isolated fixture");
            var profile=new PlayerProfile("ForestCrawlerSmoke",FileHelpers.FileSource.Local); profile.SetName("ForestCrawlerSmoke"); profile.Save();
            var world=new global::World("ForestCrawlerSmoke","crawler01") {m_fileSource=FileHelpers.FileSource.Local}; world.SaveWorldFWLData(DateTime.Now);
            Game.SetProfile("ForestCrawlerSmoke",FileHelpers.FileSource.Local);
            ZNet.SetServer(true,false,false,world.m_name,"",world); ZNet.ResetServerHost();
            var startup=UnityEngine.Object.FindFirstObjectByType<FejdStartup>();
            AccessTools.Method(typeof(FejdStartup),"LoadMainScene").Invoke(startup,null);
        })) yield break;
        float wait=Time.realtimeSinceStartup+160;
        while(!Player.m_localPlayer && Time.realtimeSinceStartup<wait) yield return new WaitForSecondsRealtime(1);
        if(!Try(()=>Check(Player.m_localPlayer!,"Isolated test world loaded a player"))) yield break;
        yield return new WaitForSecondsRealtime(12);
        if(!Try(()=>
        {
            var player=Player.m_localPlayer!; player.SetGodMode(true); player.SetGhostMode(true); player.SetIntro(false);
            // Only the new smoke fixture is moved; the application's savedir is isolated.
            var current=player.transform.position;
            var valkyrie=UnityEngine.Object.FindFirstObjectByType<Valkyrie>();
            if(valkyrie) { current=(Vector3)AccessTools.Field(typeof(Valkyrie),"m_targetPoint").GetValue(valkyrie); valkyrie.DropPlayer(true); }
            current+=Vector3.forward*35;
            player.TeleportTo(current+Vector3.up*3,Quaternion.identity,true);
        })) yield break;
        yield return new WaitForSecondsRealtime(15);
        if(Environment.GetEnvironmentVariable("FORESTCRAWLER_SMOKE_MUSIC_ONLY")=="1")
        {
            yield return MusicRegression();
            yield break;
        }
        // Select a reachable preview position in this procedurally generated fixture.
        // A valid isolated patch of ground alone need not have a route back to the player.
        Vector3 previewDirection=Vector3.zero;
        float navigationDeadline=Time.realtimeSinceStartup+20;
        var previewPath=new System.Collections.Generic.List<Vector3>();
        while(previewDirection==Vector3.zero && Time.realtimeSinceStartup<navigationDeadline)
        {
            var center=Player.m_localPlayer!.transform.position;
            for(int i=0;i<24;i++)
            {
                var direction=Quaternion.Euler(0,i*15,0)*Vector3.forward;
                if(ForestCrawler.World.Ground(center+direction*12,out var ground) && ForestCrawler.World.Route(ground,center,true,previewPath)) { previewDirection=direction; break; }
            }
            if(previewDirection==Vector3.zero) yield return new WaitForSecondsRealtime(.5f);
        }
        if(!Try(()=>
        {
            Check(Mod.Network.Connected,"Local authority handshake");
            Check(previewDirection!=Vector3.zero,"Fixture offers a validated preview charge route");
            Utils.GetMainCamera().transform.rotation=Quaternion.LookRotation(previewDirection);
            global::Console.instance.TryRunCommand("crawler_spawn");
        })) yield break;
        yield return new WaitForSecondsRealtime(2);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("preview"),"crawler_spawn creates persistent preview in game world");
            var creature=GameObject.Find("ForestCrawler_Local"); Check(creature,"Local creature exists");
            Check(!creature!.GetComponentInChildren<Character>() && !creature.GetComponentInChildren<ZNetView>(),"Creature has no combat/network entity");
            Capture(creature,"idle"); global::Console.instance.TryRunCommand("crawler_anim scream");
        })) yield break;
        yield return new WaitForSecondsRealtime(.65f);
        if(!Try(()=>Capture(GameObject.Find("ForestCrawler_Local"),"scream"))) yield break;
        yield return new WaitForSecondsRealtime(2);
        if(!Try(()=>MoveToReachableApproach(GameObject.Find("ForestCrawler_Local"),12))) yield break;
        yield return new WaitForSecondsRealtime(5);
        float routeDeadline=Time.realtimeSinceStartup+12;
        var path=new System.Collections.Generic.List<Vector3>();
        while(Time.realtimeSinceStartup<routeDeadline && !ForestCrawler.World.Route(GameObject.Find("ForestCrawler_Local").transform.position,Player.m_localPlayer!.transform.position,true,path)) yield return new WaitForSecondsRealtime(.3f);
        if(!Try(()=> { chargeStart=GameObject.Find("ForestCrawler_Local").transform.position; global::Console.instance.TryRunCommand("crawler_anim charge"); })) yield break;
        yield return new WaitForSecondsRealtime(.5f);
        if(!Try(()=>
        {
            var creature=GameObject.Find("ForestCrawler_Local");
            Check(Vector3.Distance(creature.transform.position,chargeStart)>.25f,"Preview charge actually moves across real terrain");
            Capture(creature,"charge");
        })) yield break;
        yield return new WaitForSecondsRealtime(5);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("preview/idle"),"Preview charge stops and returns to idle");
            global::Console.instance.TryRunCommand("crawler_clear");
        })) yield break;
        yield return null;
        if(!Try(()=>
        {
            Check(!GameObject.Find("ForestCrawler_Local"),"crawler_clear removes preview");
            CheckMusic(false,"Preview leaves game music unmuted");
            global::Console.instance.TryRunCommand("crawler_encounter tease");
        })) yield break;
        float teaseDeadline=Time.realtimeSinceStartup+14;
        while(!Mod.View.Status.Contains("presentation=Tease") && Time.realtimeSinceStartup<teaseDeadline) yield return null;
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("presentation=Tease"),"Manual tease activates");
            CheckMusic(true,"Tease mutes the actual MusicMan source");
            var tease=GameObject.Find("ForestCrawler_LocalTease");
            Check(tease && tease.GetComponentsInChildren<Renderer>().Length==0,"Tease has no visible renderer or model");
            Check(tease!.GetComponent<AudioSource>().spatialBlend==1 && tease.GetComponent<AudioSource>().isPlaying,"Tease plays a single spatial opening clip");
        })) yield break;
        yield return new WaitForSecondsRealtime(18);
        if(!Try(()=>
        {
            Check(!GameObject.Find("ForestCrawler_LocalTease"),"Tease clears itself after audio finishes");
            CheckMusic(false,"Tease completion restores music");
            global::Console.instance.TryRunCommand("crawler_encounter");
        })) yield break;
        yield return new WaitForSecondsRealtime(14);
        AudioSource? lureSource = null;
        AudioClip? firstLure = null;
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("presentation=Lure"),"Undiscovered full encounter remains in lure");
            CheckMusic(true,"Full lure suppresses actual game music");
            lureSource=GameObject.Find("ForestCrawler_Local").GetComponent<AudioSource>();
            firstLure=lureSource.clip;
        })) yield break;
        float alternateDeadline=Time.realtimeSinceStartup+17;
        float silenceAt=-1, longestSilence=0;
        while(lureSource && lureSource.clip==firstLure && Time.realtimeSinceStartup<alternateDeadline)
        {
            if(!lureSource.isPlaying)
            {
                if(silenceAt<0) silenceAt=Time.realtimeSinceStartup;
                longestSilence=Mathf.Max(longestSilence,Time.realtimeSinceStartup-silenceAt);
            }
            else silenceAt=-1;
            yield return null;
        }
        if(!Try(()=>
        {
            Check(lureSource && lureSource.clip!=firstLure && lureSource.isPlaying,"Lure immediately alternates to the other opening clip");
            Check(longestSilence<.15f,"Lure has no deliberate inter-clip silence");
            Check(GameObject.Find("ForestCrawler_Local").GetComponents<AudioSource>().Length==1,"Alternating lure uses one source without overlap");
            // Advance only the authority's phase clock; production transitions and presentation run normally.
            var encounter=AccessTools.Field(typeof(Network),"active").GetValue(Mod.Network);
            AccessTools.Field(encounter.GetType(),"PhaseAt").SetValue(encounter,Time.realtimeSinceStartup-Rules.LureDeadlineSeconds);
        })) yield break;
        yield return new WaitForSecondsRealtime(.25f);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("presentation=Reveal"),"Undiscovered two-minute deadline starts reveal without discovery");
            Check(lureSource!.clip==Mod.Assets.Audio["attack"],"Timeout reveal replaces lure with scream");
        })) yield break;
        yield return new WaitForSecondsRealtime(Mod.Assets.RevealLength);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("presentation=Charge"),"Lure timeout proceeds to real terrain chase");
            forceRouteFailure=true;
            chargeStart=GameObject.Find("ForestCrawler_Local").transform.position;
        })) yield break;
        yield return new WaitForSecondsRealtime(1.5f);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("presentation=Charge"),"Failed replan preserves active charge beyond old timeout");
            Check(Vector3.Distance(chargeStart,GameObject.Find("ForestCrawler_Local").transform.position)>.5f,"Failed replan follows last validated route");
            forceRouteFailure=false;
        })) yield break;
        yield return new WaitForSecondsRealtime(.5f);
        if(!Try(()=>
        {
            var recovery=(ChaseRecovery)AccessTools.Field(typeof(Presentation),"recovery").GetValue(Mod.View);
            Check(!recovery.Active,"Current route recovers before five-second deadline");
            forceRouteFailure=forceMovementFailure=true;
        })) yield break;
        yield return new WaitForSecondsRealtime(4);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("presentation=Charge"),"Persistent obstruction retains encounter before five seconds");
            Check(GameObject.Find("ForestCrawler_ChaseAudio"),"Recovery retains chase pressure audio");
            CheckMusic(true,"Music remains muted during route recovery");
        })) yield break;
        yield return new WaitForSecondsRealtime(1.4f);
        if(!Try(()=>
        {
            forceRouteFailure=forceMovementFailure=false;
            Check(!GameObject.Find("ForestCrawler_Local"),"Persistent obstruction cancels after five seconds");
            Check(!GameObject.Find("ForestCrawler_ChaseAudio"),"Recovery cancellation cleans chase audio immediately");
            CheckMusic(false,"Encounter cancellation restores game music");
        })) yield break;
        yield return null;
        if(!Try(()=>global::Console.instance.TryRunCommand("crawler_encounter"))) yield break;
        yield return new WaitForSecondsRealtime(14);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("Lure"),"crawler_encounter activates spatial lure on real terrain");
            var creature=GameObject.Find("ForestCrawler_Local");
            var audio=creature.GetComponent<AudioSource>();
            Check(audio.clip && audio.spatialBlend==1 && audio.isActiveAndEnabled,"Lure uses a configured spatial AudioSource");
            // Aim the actual camera; production gaze accumulation must send the discovery report.
            MoveToReachableApproach(creature,25);
        })) yield break;
        yield return new WaitForSecondsRealtime(5);
        if(!Try(()=>ReportDiscovery())) yield break;
        yield return new WaitForSecondsRealtime(.6f);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("Stare"),"Sustained camera gaze enters the pre-teleport stare");
            var creature=GameObject.Find("ForestCrawler_Local");
            var direction=Vector3.ProjectOnPlane(Player.m_localPlayer!.transform.position-creature.transform.position,Vector3.up);
            Check(Vector3.Angle(creature.transform.forward,direction)<5,"Creature faces the target during stare");
        })) yield break;
        float relocatedDeadline=Time.realtimeSinceStartup+14;
        while(!Mod.View.Status.Contains("Watching") && Time.realtimeSinceStartup<relocatedDeadline) yield return null;
        Vector3 captureOrigin=Player.m_localPlayer!.transform.position;
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("Watching"),"First discovery relocates and starts I-see-you");
            CheckMusic(true,"Relocation preserves music suppression");
            var creature=GameObject.Find("ForestCrawler_Local");
            Check(creature.GetComponent<AudioSource>().clip==Mod.Assets.Audio["voice"],"I-see-you plays from relocated source");
            float range=Vector3.Distance(creature.transform.position,Player.m_localPlayer.transform.position);
            Check(range>=38 && range<=72,"Narrative relocation remains 40-70 metres");
        })) yield break;
        yield return new WaitForSecondsRealtime(Mod.Assets.VoiceLength+.25f);
        if(!Try(()=> { Check(Mod.View.Status.Contains("Reveal"),"Voice automatically starts reveal without second discovery"); forceRouteFailure=true; })) yield break;
        yield return new WaitForSecondsRealtime(Mod.Assets.RevealLength);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("Charge"),"Reveal automatically enters fast chase");
            Check(GameObject.Find("ForestCrawler_ChaseAudio"),"Target-only chase pressure audio exists");
            fleeOrigin=Player.m_localPlayer.transform.position; fleeDuringCharge=true;
        })) yield break;
        yield return new WaitForSecondsRealtime(2);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("presentation=Charge") && GameObject.Find("ForestCrawler_Local"),"Initial route failure waits rather than cancelling charge");
            forceRouteFailure=false;
        })) yield break;
        float catchDeadline=Time.realtimeSinceStartup+35;
        while(!ForestCrawler.Capture.Active && Time.realtimeSinceStartup<catchDeadline) yield return null;
        if(!Try(()=>
        {
            fleeDuringCharge=false;
            Check(ForestCrawler.Capture.Active,"Fast chase reaches authoritative catch of a moving and jumping target");
            Check(jumps>=2 && airborneFrames>10,"Target really jumps repeatedly during chase: jumps="+jumps+", airborneFrames="+airborneFrames);
            captureOrigin=Player.m_localPlayer.transform.position;
            Check(Vector3.Distance(fleeOrigin,captureOrigin)>2,"Target actually flees using normal player movement: " + Vector3.Distance(fleeOrigin,captureOrigin).ToString("F2") + "m");
            Check((int)AccessTools.Field(typeof(Presentation),"successfulReplans").GetValue(Mod.View)>0,"Chase replans against the moving target before capture");
            Check(Player.m_localPlayer.GetComponent<Rigidbody>().constraints==RigidbodyConstraints.FreezeAll,"Capture temporarily locks player movement");
            Check(!GameObject.Find("ForestCrawler_ChaseAudio"),"Catch stops heartbeat and close chase audio");
            CheckMusic(true,"Capture preserves music suppression");
            var hidden=GameObject.Find("ForestCrawler_Local");
            Check(hidden.GetComponentsInChildren<Renderer>().All(r=>!r.enabled),"Catch hides world creature");
            Check(hidden.GetComponentsInChildren<Collider>().All(c=>!c.enabled),"Catch disables all detection colliders");
        })) yield break;
        yield return new WaitForSecondsRealtime(1);
        if(!Try(()=>
        {
            var capture=ForestCrawler.Capture.Current!;
            var texture=(RenderTexture)AccessTools.Field(typeof(ForestCrawler.Capture),"texture").GetValue(capture);
            var old=RenderTexture.active; RenderTexture.active=texture;
            var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(output,"capture-face.png"),image.EncodeToPNG()); RenderTexture.active=old; UnityEngine.Object.Destroy(image);
            var renderer=GameObject.Find("ForestCrawler_Capture").GetComponentInChildren<SkinnedMeshRenderer>();
            Check(renderer.sharedMesh.blendShapeCount==3 && Enumerable.Range(0,3).Any(i=>Mathf.Abs(renderer.GetBlendShapeWeight(i))>1),"Closeup animates original model facial morphs");
            Check(GameObject.Find("ForestCrawler_Capture").GetComponents<AudioSource>().All(x=>x.spatialBlend==0),"Capture screams are non-spatial");
        })) yield break;
        yield return new WaitForSecondsRealtime(4.5f);
        if(!Try(()=>
        {
            Check(!ForestCrawler.Capture.Active && !GameObject.Find("ForestCrawler_Capture"),"Capture transaction and overlay clean up by deadline");
            Check(Player.m_localPlayer.GetComponent<Rigidbody>().constraints!=RigidbodyConstraints.FreezeAll,"Capture restores player controls");
            float displacement=ForestCrawler.Capture.Horizontal(captureOrigin,Player.m_localPlayer.transform.position);
            Check(displacement>=200 && displacement<=500,"Capture teleports 200-500 metres during scare");
            Check(ForestCrawler.Capture.SafeGround(Player.m_localPlayer.transform.position,out _),"Player lands on loaded dry terrain with clearance");
            Check(!GameObject.Find("ForestCrawler_Local"),"Encounter completion removes world creature and audio");
            CheckMusic(false,"Full encounter completion restores game music");
            global::Console.instance.TryRunCommand("crawler_clear");
        })) yield break;
        // Focused transaction regression: no server permit must retain the exact safe origin.
        var fallbackOrigin=Player.m_localPlayer!.transform.position;
        var priorConstraints=Player.m_localPlayer.GetComponent<Rigidbody>().constraints;
        ForestCrawler.Capture? fallback=null;
        if(!Try(()=>
        {
            fallback=new ForestCrawler.Capture(Mod,null,_=>{});
            AccessTools.Field(typeof(ForestCrawler.Capture),"candidate").SetValue(fallback,null);
            Player.m_localPlayer.SetGodMode(false);
            float health=Player.m_localPlayer.GetHealth(); var hit=new HitData(); hit.m_damage.m_blunt=10;
            Player.m_localPlayer.ApplyDamage(hit,false,false);
            Check(Mathf.Abs(Player.m_localPlayer.GetHealth()-health)<.01f,"Capture suppresses direct damage without persistent god mode");
            Check(!(bool)AccessTools.Method(typeof(Player),"TakeInput").Invoke(Player.m_localPlayer,null),"Capture blocks gameplay input");
            Player.m_localPlayer.SetGodMode(true);
        })) yield break;
        float fallbackAt=Time.realtimeSinceStartup;
        while(fallback!=null && !fallback.Update()) yield return null;
        if(!Try(()=>
        {
            Check(Time.realtimeSinceStartup-fallbackAt>=4.8f && Time.realtimeSinceStartup-fallbackAt<5.6f,"Unapproved landing obeys five-second deadline");
            fallback!.Dispose();
            Check(Vector3.Distance(Player.m_localPlayer.transform.position,fallbackOrigin)<.1f,"No permit leaves player at origin");
            Check(!ForestCrawler.Capture.Active && Player.m_localPlayer.GetComponent<Rigidbody>().constraints==priorConstraints,"Fallback restores original movement constraints");
            var cancelled=new ForestCrawler.Capture(Mod,null,_=>{});
            AccessTools.Field(typeof(Presentation),"capture").SetValue(Mod.View,cancelled);
            global::Console.instance.TryRunCommand("crawler_clear");
            Check(!ForestCrawler.Capture.Active,"crawler_clear cancels active closeup immediately");
            Check(Player.m_localPlayer.GetComponent<Rigidbody>().constraints==priorConstraints,"Cancellation restores movement constraints");
        })) yield break;
        yield return null;
        if(!Try(()=>
        {
            Check(!GameObject.Find("ForestCrawler_Capture"),"Cancellation removes closeup camera and audio objects");
            File.WriteAllText(Path.Combine(output,"verified.txt"),"Actual Valheim isolated-world smoke passed: tease, real gaze, stare, relocation, automatic chase, capture face/audio, dry teleport, damage/input guard, denied landing fallback and cancellation cleanup. Fallback/cancellation are focused injected transaction fixtures. Not a two-player or human audio evaluation.");
            Application.Quit();
        })) yield break;
    }
    private void ReportDiscovery()
    {
        aimUntil=Time.realtimeSinceStartup+.48f;
    }
    private void LateUpdate()
    {
        if(Time.realtimeSinceStartup>=aimUntil || !Player.m_localPlayer) return;
        var creature=GameObject.Find("ForestCrawler_Local"); if(!creature) return;
        var camera=Utils.GetMainCamera();
        camera.transform.position=Player.m_localPlayer.transform.position+Vector3.up*1.7f;
        camera.transform.LookAt(creature.transform.position+Vector3.up*1.25f);
    }
    private void MoveToReachableApproach(GameObject creature,float distance)
    {
        var path=new System.Collections.Generic.List<Vector3>();
        for(int i=0;i<36;i++)
        {
            var candidate=creature.transform.position+Quaternion.Euler(0,i*10,0)*Vector3.forward*distance;
            if(!ForestCrawler.World.Ground(candidate,out var ground) || !ForestCrawler.World.Route(creature.transform.position,ground,true,path) || ForestCrawler.World.Obstructed(ground+Vector3.up*1.7f,creature.transform.position+Vector3.up*1.25f)) continue;
            Player.m_localPlayer!.TeleportTo(ground+Vector3.up*.2f,Quaternion.identity,true); return;
        }
        throw new Exception("Fixture has no reachable approach at "+distance+"m: "+ForestCrawler.World.LastRouteFailure);
    }
    private void Capture(GameObject creature,string name)
    {
        if(!creature) throw new Exception("Missing creature for "+name);
        var camera=new GameObject("SmokeCaptureCamera").AddComponent<Camera>();
        var center=creature.transform.position+Vector3.up*1.2f;
        camera.transform.position=center+creature.transform.forward*4+creature.transform.right*2+Vector3.up*.5f; camera.transform.LookAt(center);
        camera.nearClipPlane=.1f; camera.farClipPlane=150;
        var light=new GameObject("SmokeCaptureLight").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.4f; light.transform.rotation=camera.transform.rotation;
        var target=new RenderTexture(900,900,24); camera.targetTexture=target; camera.Render();
        var previous=RenderTexture.active; RenderTexture.active=target; var image=new Texture2D(900,900,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,900,900),0,0); image.Apply(); File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
        var transforms=creature.GetComponentsInChildren<Transform>(); var layers=transforms.Select(t=>t.gameObject.layer).ToArray();
        for(int i=0;i<transforms.Length;i++) transforms[i].gameObject.layer=31;
        camera.cullingMask=1<<31; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
        camera.transform.position=center+creature.transform.forward*3.4f; camera.transform.LookAt(center); camera.fieldOfView=45;
        camera.Render(); RenderTexture.active=target; image.ReadPixels(new Rect(0,0,900,900),0,0); image.Apply();
        File.WriteAllBytes(Path.Combine(output,name+"-portrait.png"),image.EncodeToPNG());
        for(int i=0;i<transforms.Length;i++) transforms[i].gameObject.layer=layers[i];
        RenderTexture.active=previous; camera.targetTexture=null; target.Release();
        UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(camera.gameObject); UnityEngine.Object.Destroy(light.gameObject);
    }
}
