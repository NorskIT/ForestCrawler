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
    private string? lateCapture;
    private bool roofFixture, holdRoof;
    private Vector3 heldRoofPoint;
    private float heldPullSyncUntil;
    private int heldPullSyncFrames;
    private bool fleeDuringCharge;
    private float nextJump;
    private int jumps, airborneFrames;
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
    private void ScreenFrame(string name,int width,int height)
    {
        var camera=Utils.GetMainCamera(); var previous=camera.targetTexture; var active=RenderTexture.active;
        var target=new RenderTexture(width,height,24); var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
            image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
        }
        finally { camera.targetTexture=previous; RenderTexture.active=active; target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(image); }
    }
    private IEnumerator ScreenRegression()
    {
        string id="screen-fixture"; int seq=0;
        var origin=Player.m_localPlayer.transform.position;
        Mod.View.Prepare(id,seq,true,false,origin,Mod.Settings,EncounterKind.Full);
        Mod.View.State(id,++seq,Phase.Lure,origin+Vector3.forward*20);
        Mod.View.ShowCue(id,Cue.Start);
        float until=Time.realtimeSinceStartup+3.3f;
        while(Time.realtimeSinceStartup<until) { Mod.View.Lease(id,seq); yield return null; }
        if(!Try(()=>
        {
            var screen=(EncounterScreen)AccessTools.Field(typeof(Presentation),"screen").GetValue(Mod.View);
            var pass=Utils.GetMainCamera().GetComponent<CrawlerScreenPass>();
            Logger.LogInfo($"Screen diagnostic: view={Mod.View.Status}, pass={pass}, frames={pass?.Frames}, cameraEnabled={Utils.GetMainCamera().enabled}, pipeline={UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline}");
            if(pass && pass.Frames==0) Utils.GetMainCamera().Render();
            Check(pass && pass.Frames>0,"Screen shader executes on the actual Valheim camera");
            var glyphs=(System.Collections.Generic.Dictionary<char,Texture2D>)AccessTools.Field(typeof(EncounterScreen),"glyphs").GetValue(screen);
            Check(glyphs.Count==26 && glyphs.Values.All(t=>t.width>5 && t.height>30),"All twenty-six rune crops contain visible glyphs");
            Check(screen.Status.Contains("rune=Start"),"Start rune survives through translation reveal");
            Mod.View.ShowCue(id,Cue.Start);
            Check(Time.realtimeSinceStartup-(float)AccessTools.Field(typeof(EncounterScreen),"cueAt").GetValue(screen)>3,"Duplicate cue does not restart text animation");
            ScreenFrame("rune-start",1280,720);
            ScreenFrame("rune-ultrawide",2560,1080);
            pass!.Set(1,.9f,0); ScreenFrame("screen-negative",1280,720);
            pass.Set(1,0,1); ScreenFrame("screen-static",1280,720);
        })) yield break;
        until=Time.realtimeSinceStartup+.7f;
        while(Time.realtimeSinceStartup<until) { Mod.View.Lease(id,seq); yield return null; }
        if(!Try(()=>
        {
            Mod.View.ShowCue(id,Cue.Warning);
            var screenState=(EncounterScreen)AccessTools.Field(typeof(Presentation),"screen").GetValue(Mod.View);
            screenState.SetPhase(Phase.Charge);
            Mod.View.ShowCue(id,Cue.Run); Mod.View.ShowCue(id,Cue.Close);
            var screen=(EncounterScreen)AccessTools.Field(typeof(Presentation),"screen").GetValue(Mod.View);
            Check(screen.Status.Contains("rune=Run"),"Run replaces warning and near cue queues behind it");
            AccessTools.Field(typeof(EncounterScreen),"cueAt").SetValue(screen,Time.realtimeSinceStartup-5.3f);
            screen.Update(); Check(screen.Status.Contains("rune=Close"),"Queued near cue appears after Run");
            Mod.View.Clear();
            Check(!Utils.GetMainCamera().GetComponent<CrawlerScreenPass>().enabled,"Cleanup immediately disables the camera pass");
        })) yield break;
        yield return null;
        if(!Try(()=>Check(!Utils.GetMainCamera().GetComponent<CrawlerScreenPass>(),"Cleanup destroys the camera component"))) yield break;
        Mod.Settings.ScreenEffects=false;
        Mod.View.Prepare("screen-disabled",0,true,false,origin,Mod.Settings,EncounterKind.Tease);
        Mod.View.State("screen-disabled",1,Phase.Tease,origin+Vector3.forward*20);
        Mod.View.ShowCue("screen-disabled",Cue.Start);
        until=Time.realtimeSinceStartup+.3f;
        while(Time.realtimeSinceStartup<until) { Mod.View.Lease("screen-disabled",1); yield return null; }
        if(!Try(()=>
        {
            var pass=Utils.GetMainCamera().GetComponent<CrawlerScreenPass>();
            var material=(Material)AccessTools.Field(typeof(CrawlerScreenPass),"material").GetValue(pass);
            Check(material.GetFloat("_Strength")==0,"Local effect opt-out preserves rune rendering with zero camera effect strength");
            Check(GameObject.Find("ForestCrawler_LocalTease").GetComponent<AudioSource>().isPlaying,"Tease audio plays with screen effects disabled");
            Mod.View.Clear(); Mod.Settings.ScreenEffects=true;
        })) yield break;
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
                Logger.LogInfo("Focused music regression passed using the exact playing MusicMan source.");
            })) yield break;
        }
        finally
        {
            Mod.View.Clear(); MusicMan.m_masterMusicVolume=previousMaster;
            if(decoy) UnityEngine.Object.Destroy(decoy);
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
        isolation.Patch(AccessTools.Method(typeof(ZSyncTransform),"OwnerSync"),prefix:new HarmonyMethod(typeof(SmokePlugin),nameof(DelayPullSync)));
        isolation.Patch(AccessTools.Method(typeof(Presentation),"LateUpdate"),postfix:new HarmonyMethod(typeof(SmokePlugin),nameof(CaptureAfterPose)));
        isolation.Patch(AccessTools.Method(typeof(Player),"SetControls"),prefix:new HarmonyMethod(typeof(SmokePlugin),nameof(FleeInput)));


        Utils.SetSaveDataPath(Path.Combine(output,"saves"));
        deadline=Time.realtimeSinceStartup+600;
    }
    // Fixture input selection only; this is not part of the shipped creature motor.
    private static bool FixturePlayerStep(Vector3 from, Vector3 to)
    {
        var delta=to-from;
        if(Mathf.Abs(delta.y)>Vector3.ProjectOnPlane(delta,Vector3.up).magnitude*.7f+.2f) return false;
        return !Physics.CapsuleCast(from+Vector3.up*.6f,from+Vector3.up*2,.48f,delta.normalized,delta.magnitude,ForestCrawler.World.Solids,QueryTriggerInteraction.Ignore);
    }
    private void FixedUpdate()
    {
        // Test-only stationary elevated target. Prevent incidental slips while native AI circles.
        if(!holdRoof || !Player.m_localPlayer) return;
        if(ArmGrab.Current!=null || ForestCrawler.Capture.Active) { holdRoof=false; return; }
        var player=Player.m_localPlayer; var body=player.GetComponent<Rigidbody>();
        body.position=heldRoofPoint; player.transform.position=heldRoofPoint; body.linearVelocity=Vector3.zero;
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
                    if (!ForestCrawler.World.Ground(player.transform.position+direction*1.2f,out var ground) || !FixturePlayerStep(player.transform.position,ground)) continue;
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
                var pressure=new ChaseAudio(Mod.Assets);
                UnityEngine.Object.DestroyImmediate(GameObject.Find("ForestCrawler_ChaseAudio"));
                pressure.Dispose(); pressure.Dispose();
                Check(true,"Chase audio cleanup tolerates scene destruction and repeated disposal");
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
            player.SetGhostMode(false);
            player.TeleportTo(current+Vector3.up*3,Quaternion.identity,true);
        })) yield break;
        yield return new WaitForSecondsRealtime(15);
        if(Environment.GetEnvironmentVariable("FORESTCRAWLER_SMOKE_MUSIC_ONLY")=="1")
        {
            yield return ScreenRegression();
            if(!failed) yield return MusicRegression();
            if(!failed) File.WriteAllText(Path.Combine(output,"verified.txt"),"Actual camera shader, rune rendering/order/cleanup and MusicMan source playback passed in injected presentation phases.");
            Application.Quit(); yield break;
        }
        if(Environment.GetEnvironmentVariable("FORESTCRAWLER_SMOKE_NATIVE_ONLY")=="1")
        { yield return ScreenRegression(); if(!failed) yield return NativeCaptureRegression(); yield break; }
        yield return ScreenRegression();
        if(failed) yield break;
        yield return MusicRegression();
        if(failed) yield break;
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
        for (int tick=0;tick<8;tick++)
        { yield return new WaitForSecondsRealtime(.5f); Logger.LogInfo("NATIVE_DIAGNOSTIC " + Mod.View.Status); }
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
            // Advance the authoritative activation clock to the warning boundary.
            var encounter=AccessTools.Field(typeof(Network),"active").GetValue(Mod.Network);
            AccessTools.Field(encounter.GetType(),"Started").SetValue(encounter,Time.realtimeSinceStartup-105);
        })) yield break;
        yield return new WaitForSecondsRealtime(.25f);
        if(!Try(()=>
        {
            var encounter=AccessTools.Field(typeof(Network),"active").GetValue(Mod.Network);
            int cues=(int)AccessTools.Field(encounter.GetType(),"Cues").GetValue(encounter);
            Check((cues & (1<<(int)Cue.Warning))!=0 && Mod.View.Status.Contains("rune=Warning"),"Server emits the fifteen-second warning to the target client");
            Check(Mod.View.Status.Contains("presentation=Lure"),"Warning does not itself start pursuit");
            AccessTools.Field(encounter.GetType(),"Started").SetValue(encounter,Time.realtimeSinceStartup-(Rules.LureDeadlineSeconds-Mod.Assets.RevealLength));
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
            chargeStart=GameObject.Find("ForestCrawler_Local").transform.position;
        })) yield break;
        yield return new WaitForSecondsRealtime(1.5f);
        if(!Try(()=>
        {
            var driver=GameObject.Find("ForestCrawler_LocalDriver");
            Check(driver && driver.GetComponent<MonsterAI>() && driver.GetComponent<Humanoid>(),"Charge uses actual MonsterAI and Humanoid");
            var view=driver!.GetComponent<ZNetView>();
            Check(view.IsValid() && view.IsOwner(),"Detached native driver has valid local owner state");
            Check(ZDOMan.instance.GetZDO(view.GetZDO().m_uid)==null,"Driver ZDO is absent from world replication registry");
            Check(!ZNetScene.instance.FindInstance(view.GetZDO().m_uid),"Driver is absent from ZNetScene instance registry");
            Check(driver.GetComponentsInChildren<Renderer>().All(r=>!r.enabled),"Native Greydwarf visuals stay hidden");
            Check(!BaseAI.IsEnemy(driver.GetComponent<Character>(),driver.GetComponent<Character>()),"Driver cannot select itself as an enemy");
            CheckMusic(true,"Native chase retains music suppression");
            Mod.Network.ClearOwn(); Mod.View.Clear();
        })) yield break;
        yield return null;
        if(!Try(()=>global::Console.instance.TryRunCommand("crawler_encounter"))) yield break;
        yield return new WaitForSecondsRealtime(14);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("presentation=Lure"),"Retreat fixture starts in Lure");
            var e=AccessTools.Field(typeof(Network),"active").GetValue(Mod.Network);
            var origin=(Vector3)AccessTools.Field(e.GetType(),"LureOrigin").GetValue(e);
            float distance=Vector3.ProjectOnPlane(Player.m_localPlayer!.transform.position-origin,Vector3.up).magnitude;
            // Inject only the baseline, keeping the real shared player position and server update path.
            AccessTools.Field(e.GetType(),"StartDistance").SetValue(e,distance-50.1f);
        })) yield break;
        yield return new WaitForSecondsRealtime(.3f);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("rune=Escape") && Mod.View.Status.Contains("presentation=Lure"),"Fifty additional metres warn without starting pursuit");
            var e=AccessTools.Field(typeof(Network),"active").GetValue(Mod.Network);
            var origin=(Vector3)AccessTools.Field(e.GetType(),"LureOrigin").GetValue(e);
            float distance=Vector3.ProjectOnPlane(Player.m_localPlayer!.transform.position-origin,Vector3.up).magnitude;
            AccessTools.Field(e.GetType(),"StartDistance").SetValue(e,distance-75.1f);
        })) yield break;
        yield return new WaitForSecondsRealtime(.25f);
        if(!Try(()=>Check(Mod.View.Status.Contains("presentation=Reveal"),"Seventy-five additional metres trigger reveal immediately"))) yield break;
        yield return new WaitForSecondsRealtime(Mod.Assets.RevealLength);
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("presentation=Charge"),"Retreat reveal enters native chase");
            Mod.Network.ClearOwn(); Mod.View.Clear();
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
        if(!Try(()=> { Check(Mod.View.Status.Contains("Reveal"),"Voice automatically starts reveal without second discovery"); })) yield break;
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
            Check(GameObject.Find("ForestCrawler_LocalDriver"),"Native driver survives moving and jumping target");
        })) yield break;
        float catchDeadline=Time.realtimeSinceStartup+35;
        while(!ForestCrawler.Capture.Active && Time.realtimeSinceStartup<catchDeadline) yield return null;
        if(!Try(()=>
        {
            fleeDuringCharge=false;
            Check(ForestCrawler.Capture.Active,"Fast chase reaches authoritative catch of a moving and jumping target: "+Mod.View.Status);
            Check(jumps>=2 && airborneFrames>10,"Target really jumps repeatedly during chase: jumps="+jumps+", airborneFrames="+airborneFrames);
            captureOrigin=Player.m_localPlayer.transform.position;
            Check(Vector3.Distance(fleeOrigin,captureOrigin)>2,"Target actually flees using normal player movement: " + Vector3.Distance(fleeOrigin,captureOrigin).ToString("F2") + "m");

            Check(Player.m_localPlayer.GetComponent<Rigidbody>().constraints==RigidbodyConstraints.FreezeAll,"Capture temporarily locks player movement");
            var pressure=GameObject.Find("ForestCrawler_ChaseAudio");
            Check(!pressure || pressure.GetComponentsInChildren<AudioSource>().All(a=>!a.isPlaying),"Catch immediately stops heartbeat and close chase audio");
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
    private IEnumerator CompareNativeTerrain()
    {
        var player=Player.m_localPlayer;
        var origin=player.transform.position;
        var rocks=Physics.OverlapSphere(origin,65,ForestCrawler.World.Solids,QueryTriggerInteraction.Ignore)
            .Where(c=>c.name.ToLowerInvariant().Contains("rock") || c.transform.root.name.ToLowerInvariant().Contains("rock"))
            .OrderBy(c=>Vector3.Distance(c.bounds.center,origin)).ToArray();
        Collider selected=null!; Vector3 top=Vector3.zero;
        foreach(var rock in rocks)
        {
            if(rock.bounds.size.y<1 || rock.bounds.size.y>6) continue;
            if(Physics.Raycast(new Vector3(rock.bounds.center.x,rock.bounds.max.y+2,rock.bounds.center.z),Vector3.down,out var hit,10,ForestCrawler.World.Solids,QueryTriggerInteraction.Ignore) && hit.collider==rock)
            { selected=rock; top=hit.point; break; }
        }
        if(!Try(()=>Check(selected,"Fixture contains a real generated rock for native comparison"))) yield break;
        player.TeleportTo(top+Vector3.up*.2f,Quaternion.identity,true);
        yield return new WaitForSecondsRealtime(5);
        Vector3 start=Vector3.zero;
        for(int i=0;i<36;i++)
            if(ForestCrawler.World.Ground(top+Quaternion.Euler(0,i*10,0)*Vector3.forward*12,out start)) break;
        var prefab=ZNetScene.instance.GetPrefab("Greydwarf");
        for(int pass=0;pass<2;pass++)
        {
            GameObject driver=null!; NativePursuit? local=null;
            if(!Try(()=>
            {
                if(pass==0)
                {
                    driver=UnityEngine.Object.Instantiate(prefab,start,Quaternion.identity);
                    var ai=driver.GetComponent<MonsterAI>();
                    NativePursuit.Write(ai,"m_targetCreature",player); NativePursuit.Write(ai,"m_lastKnownTargetPos",player.transform.position); ai.Alert();
                    driver.GetComponent<Character>().m_runSpeed=12;
                }
                else
                {
                    local=new NativePursuit(start,player); driver=local.Root;
                    Check(local.AI.m_pathAgentType==prefab.GetComponent<MonsterAI>().m_pathAgentType,"Native driver inherits the actual Greydwarf navigation agent");
                    Check(driver.GetComponent<CapsuleCollider>().radius==prefab.GetComponent<CapsuleCollider>().radius,"Native driver inherits the actual Greydwarf body radius");
                }
            })) yield break;
            float until=Time.realtimeSinceStartup+8, nearest=float.MaxValue, travel=0; var previous=start;
            while(Time.realtimeSinceStartup<until)
            {
                local?.Tick(12); var point=driver.transform.position;
                travel+=Vector3.Distance(previous,point); previous=point;
                nearest=Mathf.Min(nearest,Vector3.Distance(point,player.transform.position));
                yield return null;
            }
            if(!Try(()=>
            {
                Logger.LogInfo($"NATIVE_ROCK_COMPARISON pass={pass} rock={selected.name} movement={travel:F2} nearest={nearest:F2} path={NativePursuit.Read<bool>(driver.GetComponent<MonsterAI>(),"m_lastFindPathResult")}");
                Check(travel>.5f,(pass==0 ? "Vanilla Greydwarf" : "Local native driver")+" searches toward a player on real generated rock");
                if(local!=null)
                {
                    Check(driver.GetComponent<LocalCrawlerDriver>().AiTicks>0 && driver.GetComponent<LocalCrawlerDriver>().MotorTicks>0,"Real terrain pursuit executes both native AI and Character motor");
                    Check(!BaseAI.IsEnemy(player,local.Character) && BaseAI.IsEnemy(local.Character,player),"Driver only targets its selected player and is not an ordinary enemy target");
                    local.Dispose();
                }
                else ZNetScene.instance.Destroy(driver);
            })) yield break;
            yield return null;
        }
        player.TeleportTo(origin+Vector3.up*.2f,Quaternion.identity,true);
        yield return new WaitForSecondsRealtime(5);
    }
    private IEnumerator NativeCaptureRegression()
    {
        roofFixture=true;
        yield return CompareNativeTerrain();
        if(failed) yield break;
        yield return new WaitForSecondsRealtime(6);
        if(!Try(()=>global::Console.instance.TryRunCommand("crawler_encounter"))) yield break;
        float until=Time.realtimeSinceStartup+15;
        while(!Mod.View.Status.Contains("presentation=Lure") && Time.realtimeSinceStartup<until) yield return null;
        GameObject roof=null!; Vector3 roofStart=Vector3.zero;
        var player=Player.m_localPlayer; var body=player.GetComponent<Rigidbody>();
        var originalConstraints=body.constraints;
        if(!Try(()=>
        {
            Check(Mod.View.Status.Contains("presentation=Lure"),"Arm fixture starts a server-authorized full encounter");
            var creature=GameObject.Find("ForestCrawler_Local");
            Vector3 ground=Vector3.zero, direction=Vector3.zero;
            for(int i=0;i<36;i++)
            {
                var probeDirection=Quaternion.Euler(0,i*10,0)*Vector3.forward;
                if(!ForestCrawler.World.Ground(creature.transform.position+probeDirection*12,out var probe)) continue;
                var proposed=probe+Vector3.up*12.3f-probeDirection*1.95f;
                if(Physics.Linecast(creature.transform.position+Vector3.up*2.1f,proposed+Vector3.up*.8f,ForestCrawler.World.Solids,QueryTriggerInteraction.Ignore)) continue;
                ground=probe; direction=probeDirection; break;
            }
            Check(direction!=Vector3.zero,"Roof fixture has actual unobstructed scene visibility before placement");
            roof=GameObject.CreatePrimitive(PrimitiveType.Cube); roof.name="UnreachableRoofFixture";
            roof.transform.SetPositionAndRotation(ground+Vector3.up*6,Quaternion.LookRotation(direction));
            roof.transform.localScale=new Vector3(4,12,4); roof.layer=LayerMask.NameToLayer("piece");
            roofStart=ground+Vector3.up*12.3f-direction*1.95f;
            player.TeleportTo(roofStart,Quaternion.identity,true);
        })) yield break;
        yield return new WaitForSecondsRealtime(5);
        if(!Try(()=>
        {
            var e=AccessTools.Field(typeof(Network),"active").GetValue(Mod.Network);
            heldRoofPoint=roofStart; holdRoof=true; body.position=roofStart; player.transform.position=roofStart; body.linearVelocity=Vector3.zero; Physics.SyncTransforms();
            Check((Phase)AccessTools.Field(e.GetType(),"Phase").GetValue(e)==Phase.Lure,"Roof fixture remains undiscovered before timeout transition");
            AccessTools.Field(e.GetType(),"Started").SetValue(e,Time.realtimeSinceStartup-(Rules.LureDeadlineSeconds-Mod.Assets.RevealLength));
            var creaturePosition=GameObject.Find("ForestCrawler_Local").transform.position;
            Check(ArmGrab.Visible(creaturePosition,player,30),"Torso ray sees the player on the exposed roof edge");
            Check(!ArmGrab.Visible(player.transform.position+Vector3.right*31,player,30),"Arm grab rejects a target beyond thirty metres");
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name="ArmSightBlocker"; blocker.layer=LayerMask.NameToLayer("blocker");
            blocker.transform.position=(creaturePosition+Vector3.up*2.1f+player.GetCenterPoint())*.5f;
            blocker.transform.localScale=Vector3.one*3; Physics.SyncTransforms();
            Check(!ArmGrab.Visible(creaturePosition,player,30),"Solid obstruction prevents arm-grab visibility");
            UnityEngine.Object.DestroyImmediate(blocker); Physics.SyncTransforms();
        })) yield break;
        yield return new WaitForSecondsRealtime(3);
        bool extended=false, pulling=false; float nextTrace=0; float chargeAt=Time.realtimeSinceStartup; until=chargeAt+55;
        while(!ForestCrawler.Capture.Active && Time.realtimeSinceStartup<until)
        {
            if(!Try(()=>
            {
                if(Time.realtimeSinceStartup>=nextTrace)
                { nextTrace=Time.realtimeSinceStartup+1; Logger.LogInfo("ARM_TRACE "+Mod.View.Status+"; player="+player.transform.position.ToString("F2")); }
                var driver=GameObject.Find("ForestCrawler_LocalDriver");
                if(driver)
                {
                    // Keep this visual/pull fixture on an exposed edge as native AI circles the roof.
                    // Production still performs the real solid raycast before authorizing a grab.
                    if(!extended && Time.realtimeSinceStartup-chargeAt>20 && !ArmGrab.Visible(driver.transform.position,player,30))
                    {
                        var torsoOffset=player.GetCenterPoint()-player.transform.position;
                        for(int edge=0;edge<16;edge++)
                        {
                            float angle=edge*Mathf.PI/8;
                            var offset=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                            offset*=.48f/Mathf.Max(Mathf.Abs(offset.x),Mathf.Abs(offset.z)); offset.y=.525f;
                            var point=roof.transform.TransformPoint(offset);
                            if(Physics.Linecast(driver.transform.position+Vector3.up*2.1f,point+torsoOffset,ForestCrawler.World.Solids,QueryTriggerInteraction.Ignore)) continue;
                            heldRoofPoint=point; body.position=point; player.transform.position=point; body.linearVelocity=Vector3.zero; Physics.SyncTransforms();
                            break;
                        }
                    }
                    var view=driver!.GetComponent<ZNetView>();
                    if(ZDOMan.instance.GetZDO(view.GetZDO().m_uid)!=null || ZNetScene.instance.FindInstance(view.GetZDO().m_uid))
                        throw new Exception("Detached driver leaked into replication registry");
                }
                if(Mod.View.Status.Contains("presentation=GrabWindup") && !extended)
                {
                    extended=true;
                    Check(Time.realtimeSinceStartup-chargeAt>26,"Arm grab does not bypass the thirty-second pursuit delay");
                    CheckMusic(true,"Music remains suppressed during arm extension");
                    var voice=GameObject.Find("ForestCrawler_Local").GetComponent<AudioSource>();
                    Check(voice.clip==Mod.Assets.Audio["voice"] && voice.isPlaying,"Arm extension starts the spatial I-see-you line");
                }
                if(ArmGrab.Current!=null && !pulling)
                {
                    pulling=true;
                    Check(body.isKinematic,"Authorized pull owns player physics");
                    Check(!(bool)AccessTools.Method(typeof(Player),"TakeInput").Invoke(player,null),"Authorized pull blocks gameplay movement input");
                    lateCapture="extended-arms";
                }
            })) yield break;
            yield return null;
        }
        if(!Try(()=>
        {
            Check(extended && pulling,"Unreachable rooftop pursuit enters extension and pulling through authoritative phases: "+Mod.View.Status);
            Check(heldPullSyncFrames>0,"Capture tolerates an injected 0.9-second gap in shared player position updates");
            Check(ForestCrawler.Capture.Active,"Arm pull reaches the ordinary capture sequence: "+Mod.View.Status);
            Check(ArmGrab.Current==null,"Pull releases movement ownership before closeup capture");
        })) yield break;
        yield return new WaitForSecondsRealtime(5.5f);
        if(!Try(()=>
        {
            Check(!ForestCrawler.Capture.Active && body.constraints==originalConstraints && !body.isKinematic,"Arm capture restores native player physics");
            Check(ForestCrawler.Capture.SafeGround(player.transform.position,out _),"Arm capture lands on dry loaded ground");
            var beforeGravity=body.useGravity; var beforeKinematic=body.isKinematic; var beforePosition=body.position;
            var cancelledPull=new ArmGrab(body.position+Vector3.forward*12,10); cancelledPull.Dispose(); cancelledPull.Dispose();
            Check(ArmGrab.Current==null && body.useGravity==beforeGravity && body.isKinematic==beforeKinematic && body.constraints==originalConstraints && Vector3.Distance(body.position,beforePosition)<.01f,"Repeated pull cancellation restores exact player physics and position");
            UnityEngine.Object.Destroy(roof);
            roofFixture=false; Mod.Network.ClearOwn(); Mod.View.Clear();
            File.WriteAllText(Path.Combine(output,"verified.txt"),"Actual Valheim rooftop fixture: native pursuit, real 30s deadline, visibility, extended arms, collision-checked pull, ordinary capture, safe teleport and physics cleanup. Single client only.");
            Application.Quit();
        })) yield break;
    }
    private static bool DelayPullSync(ZSyncTransform __instance)
    {
        if(running==null || !running.roofFixture || ArmGrab.Current==null || __instance.GetComponent<Player>()!=Player.m_localPlayer) return true;
        if(running.heldPullSyncUntil==0) running.heldPullSyncUntil=Time.realtimeSinceStartup+.9f;
        if(Time.realtimeSinceStartup>=running.heldPullSyncUntil) return true;
        running.heldPullSyncFrames++; return false;
    }
    private static void CaptureAfterPose()
    {
        if(running==null || running.lateCapture==null) return;
        string name=running.lateCapture; running.lateCapture=null;
        running.Try(()=>
        {
            var model=GameObject.Find("ForestCrawler_Local");
            foreach(var hand in model.GetComponentsInChildren<Transform>().Where(t=>t.name=="HandL"||t.name=="HandR"))
                running.Check(Vector3.Distance(hand.position,Player.m_localPlayer.GetCenterPoint())<1.2f,"Rendered arm pose keeps "+hand.name+" alongside the player's torso");
            var bones=model.GetComponentsInChildren<Transform>();
            var left=bones.First(t=>t.name=="UpperArmL"); var right=bones.First(t=>t.name=="UpperArmR");
            var hands=bones.First(t=>t.name=="HandL").position-bones.First(t=>t.name=="HandR").position;
            var axis=Vector3.Cross(Vector3.up,Player.m_localPlayer.GetCenterPoint()-(left.position+right.position)*.5f).normalized;
            running.Check(Mathf.Abs(Vector3.Dot(hands,axis)-Vector3.Dot(left.position-right.position,axis))<.05f,"Actual rendered arm pose preserves side order and shoulder width");
            running.Capture(model,name);
        });
    }
    private void ReportDiscovery()
    {
        aimUntil=Time.realtimeSinceStartup+.48f;
    }
    private void LateUpdate()
    {
        if(roofFixture && Mod.View.Status.Contains("presentation=Lure"))
        { var gazeCamera=Utils.GetMainCamera(); if(gazeCamera) gazeCamera.transform.rotation=Quaternion.LookRotation(Vector3.up,Vector3.forward); return; }
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
