using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ForestCrawler;

namespace ForestCrawler { internal static class World { internal const int Solids = 1; } }

public static class CrawlerPreview
{
    public static void AnalyzeAudio()
    {
        var report=new System.Text.StringBuilder();
        foreach(string guid in AssetDatabase.FindAssets("t:AudioClip",new[]{"Assets/Crawler"}))
        {
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)); clip.LoadAudioData();
            var data=new float[clip.samples*clip.channels]; if(!clip.GetData(data,0)) throw new Exception("Cannot inspect audio "+clip.name);
            double sum=0,peak=0; foreach(float sample in data) {sum+=sample*sample;peak=Math.Max(peak,Math.Abs(sample));}
            report.AppendLine($"{clip.name}: duration={clip.length:F3}s channels={clip.channels} peak={peak:F4} rms={Math.Sqrt(sum/data.Length):F4}");
        }
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/audio-analysis.txt")),report.ToString());
        Debug.Log("FORESTCRAWLER_AUDIO_ANALYZED");
    }
    private static void ValidateCloseApproach()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = Vector3.down * .5f; floor.transform.localScale = new Vector3(80, 1, 80);
        bool Accept(RaycastHit hit) => true;
        bool Ground(Vector3 point, out Vector3 ground) => Traversal.Support(point,1,Accept,out ground,out var normal) && Traversal.ClearBody(ground,normal,1);
        bool Segment(Vector3 a, Vector3 b) => Traversal.Segment(a,b,1,Accept);
        var route = new System.Collections.Generic.List<Vector3>(); Physics.SyncTransforms();
        var from = new Vector3(0,0,7); var target = Vector3.up * .1f;
        if (!ApproachPath.TryBuild(from,target,2.5f,Ground,Segment,route) || route.Count<2 || Vector3.Distance(route[route.Count-1],target)>2.5f)
            throw new Exception("Close approach requires no navigation tiles and ends inside catch range");
        if (!ApproachPath.TryBuild(new Vector3(0,0,30),target,2.5f,Ground,Segment,route,90))
            throw new Exception("Clear long chase unnecessarily depends on navmesh");
        ApproachPath.TryBuild(from,target,2.5f,Ground,Segment,route);
        var oldEnd=route[route.Count-1]; target+=Vector3.right*2;
        if (!ApproachPath.TryBuild(from,target,2.5f,Ground,Segment,route) || Vector3.Distance(oldEnd,route[route.Count-1])<.5f)
            throw new Exception("Approach failed to track moving target");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=new Vector3(0,1.5f,4); wall.transform.localScale=new Vector3(10,3,.2f); Physics.SyncTransforms();
        if (ApproachPath.TryBuild(from,Vector3.up*.1f,2.5f,Ground,Segment,route) || route.Count!=0)
            throw new Exception("Close approach crossed a wall or retained a partial path");
        UnityEngine.Object.DestroyImmediate(wall); floor.transform.rotation=Quaternion.Euler(-15,0,0); Physics.SyncTransforms();
        Ground(new Vector3(0,0,6),out var slopeFrom); Ground(Vector3.zero,out var slopeTarget);
        if(!ApproachPath.TryBuild(slopeFrom,slopeTarget+Vector3.up*.1f,2.5f,Ground,Segment,route)) throw new Exception("Close approach failed on a traversable slope");
        floor.transform.rotation=Quaternion.identity; Physics.SyncTransforms();
        if(ApproachPath.TryBuild(from,new Vector3(0,3,0),2.5f,Ground,Segment,route)) throw new Exception("Approach caught a player on an unreachable ledge");
        if(ApproachPath.TryBuild(new Vector3(0,0,20),Vector3.zero,2.5f,Ground,Segment,route)) throw new Exception("Close approach exceeded bounded distance");
        foreach(float angle in new[]{30f,45f,60f,85f})
        {
            floor.transform.rotation=Quaternion.Euler(-angle,0,0); Physics.SyncTransforms();
            var normal=floor.transform.up;
            var center=floor.transform.position+normal*.5f;
            var tangent=floor.transform.forward;
            var slopeStart=center-tangent*2; var slopeEnd=center+tangent*2;
            if(!Ground(slopeStart,out var groundedStart) || !Ground(slopeEnd,out var groundedEnd) || !Segment(groundedStart,groundedEnd))
                throw new Exception("Production traversal rejected slope "+angle);
            if(!ApproachPath.TryBuild(groundedStart,groundedEnd,2.5f,Ground,Segment,route))
                throw new Exception("Production direct route rejected slope "+angle);
        }
        floor.transform.rotation=Quaternion.identity; Physics.SyncTransforms();
        if(!Traversal.Support(new Vector3(0,2,0),1,Accept,out var jumpGround,out _,true) || jumpGround.y>.01f ||
            !ApproachPath.TryBuild(from,jumpGround,2.5f,Ground,Segment,route)) throw new Exception("Airborne target projection loses ground route");
        if(!Traversal.Support(new Vector3(2,2,0),1,Accept,out var landedGround,out _,true) ||
            !ApproachPath.TryBuild(from,landedGround,2.5f,Ground,Segment,route)) throw new Exception("Jump landing at new position loses route");
        // A persistent obstacle blocks actual physics, and removing it restores the same query.
        wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=new Vector3(0,1.5f,4); wall.transform.localScale=new Vector3(10,3,.2f); Physics.SyncTransforms();
        if(Segment(new Vector3(0,0,3),new Vector3(0,0,5))) throw new Exception("Production traversal penetrated temporary obstruction");
        UnityEngine.Object.DestroyImmediate(wall); Physics.SyncTransforms();
        if(!Segment(new Vector3(0,0,3),new Vector3(0,0,5))) throw new Exception("Production traversal failed to recover after obstruction removal");
        floor.transform.localScale=new Vector3(80,1,4); Physics.SyncTransforms();
        if(Segment(Vector3.zero,new Vector3(0,0,4))) throw new Exception("Production traversal crossed unsupported cliff");
        UnityEngine.Object.DestroyImmediate(floor); Physics.SyncTransforms();
        var ramp=new GameObject("ConnectedSlopeFixture");
        var vertices=new System.Collections.Generic.List<Vector3>();
        var triangles=new System.Collections.Generic.List<int>();
        var centers=new System.Collections.Generic.List<Vector3> { Vector3.zero };
        foreach(float angle in new[]{0f,15f,30f,45f,60f,75f,85f})
            centers.Add(centers[centers.Count-1]+new Vector3(0,Mathf.Sin(angle*Mathf.Deg2Rad),Mathf.Cos(angle*Mathf.Deg2Rad))*4);
        foreach(var center in centers) { vertices.Add(center+Vector3.left*5); vertices.Add(center+Vector3.right*5); }
        for(int i=0;i<centers.Count-1;i++) { int v=i*2; triangles.AddRange(new[]{v,v+2,v+1,v+1,v+2,v+3}); }
        var rampMesh=new Mesh(); rampMesh.SetVertices(vertices); rampMesh.SetTriangles(triangles,0); rampMesh.RecalculateNormals();
        ramp.AddComponent<MeshCollider>().sharedMesh=rampMesh; Physics.SyncTransforms();
        for(int i=1;i<centers.Count-1;i++)
        {
            var before=Vector3.MoveTowards(centers[i],centers[i-1],.2f);
            var after=Vector3.MoveTowards(centers[i],centers[i+1],.2f);
            if(!Segment(before,after)) throw new Exception("Connected slope transition rejected at index "+i);
        }
        UnityEngine.Object.DestroyImmediate(ramp); UnityEngine.Object.DestroyImmediate(rampMesh);
        Debug.Log("FORESTCRAWLER_APPROACH_OK: moving target, no navmesh, wall rejection, slope, ledge, bounded range, no partial paths");
    }
    private static void ValidateRockRoute()
    {
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position=Vector3.down*.5f; floor.transform.localScale=new Vector3(80,1,80);
        var rock=new GameObject("RockWithSteepFrontAndAccessibleBack");
        var mesh=new Mesh();
        mesh.vertices=new[]{new Vector3(-3,0,0),new Vector3(3,0,0),new Vector3(-3,3,0),new Vector3(3,3,0),new Vector3(-3,0,6),new Vector3(3,0,6)};
        mesh.triangles=new[]{0,2,1,1,2,3,2,4,3,3,4,5,0,4,2,1,3,5,0,1,4,1,5,4}; mesh.RecalculateNormals();
        rock.AddComponent<MeshCollider>().sharedMesh=mesh; Physics.SyncTransforms();
        bool Accept(RaycastHit hit)=>true;
        bool Ground(Vector3 point,out Vector3 ground)=>Traversal.Support(point,1,Accept,out ground,out var normal) && Traversal.ClearBody(ground,normal,1);
        bool Segment(Vector3 a,Vector3 b)=>Traversal.Segment(a,b,1,Accept);
        var from=new Vector3(0,0,-5); var target=new Vector3(0,2.5f,1);
        bool Goal(Vector3 point)=>!Physics.Linecast(point+Vector3.up*1.2f,target+Vector3.up*1.2f,1);
        var route=new System.Collections.Generic.List<Vector3>();
        if(ApproachPath.TryBuild(from,target,1.5f,Ground,Segment,route)) throw new Exception("Rock fixture must obstruct the direct approach");
        var timer=System.Diagnostics.Stopwatch.StartNew();
        if(!SurfacePath.TryBuild(from,target,1.5f,Ground,Segment,Goal,route)) throw new Exception("Local surface search did not route around the rock and climb its accessible back: "+Traversal.LastFailure);
        if(!route.Any(p=>p.z>3) || Vector3.Distance(route[route.Count-1],target)>1.5f) throw new Exception("Rock route did not reach the elevated target through the accessible side");
        for(int i=1;i<route.Count;i++) if(!Segment(route[i-1],route[i])) throw new Exception("Rock route contains an invalid edge");
        Debug.Log("FORESTCRAWLER_ROCK_OK: waypoints="+route.Count+", searchMs="+timer.ElapsedMilliseconds);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=new Vector3(0,2,-1); wall.transform.localScale=new Vector3(40,4,.5f); Physics.SyncTransforms();
        if(SurfacePath.TryBuild(from,target,1.5f,Ground,Segment,Goal,route)) throw new Exception("Surface search crossed a blocking wall");
        UnityEngine.Object.DestroyImmediate(wall); UnityEngine.Object.DestroyImmediate(rock); UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(floor);
    }
    private static void ValidateMusicSilence()
    {
        var owner=new GameObject("MusicMuteFixture"); var music=owner.AddComponent<AudioSource>();
        var effects=owner.AddComponent<AudioSource>(); var replacement=owner.AddComponent<AudioSource>();
        var silence=new MusicSilence(); music.volume=.37f;
        silence.Refresh(music);
        if(music.mute || silence.Active) throw new Exception("Inactive preview changed music");
        silence.Begin(music); silence.Begin(music);
        if(!music.mute || effects.mute || music.volume!=.37f) throw new Exception("Encounter did not exclusively mute music");
        music.volume=.19f; silence.Refresh(music); silence.Clear(); silence.Clear();
        if(music.mute || music.volume!=.19f) throw new Exception("Cleanup overwrote user volume or retained mute");
        music.mute=true; silence.Begin(music); silence.Clear();
        if(!music.mute) throw new Exception("Cleanup unmutes previously muted music");
        music.mute=false; silence.Begin(music); silence.Refresh(replacement);
        if(music.mute || !replacement.mute) throw new Exception("Music source replacement was not handled");
        UnityEngine.Object.DestroyImmediate(replacement); silence.Clear();
        silence.Begin(null); silence.Refresh(music);
        if(!music.mute) throw new Exception("Late music source escaped suppression");
        silence.Clear();
        if(music.mute) throw new Exception("Late source mute was not restored");
        UnityEngine.Object.DestroyImmediate(owner);
        Debug.Log("FORESTCRAWLER_MUSIC_OK: active mute, preview exclusion, volume preservation, prior mute, source replacement/destruction, late source, repeated cleanup");
    }
    public static void Validate()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ValidateCloseApproach();
        ValidateMusicSilence();
        ValidateRockRoute();
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/editor-preview")); Directory.CreateDirectory(output);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Crawler/ForestCrawler.prefab");
        var root = UnityEngine.Object.Instantiate(prefab); root.name = "PreviewCreature";
        var sensor=new GameObject("Probe"); sensor.layer=2; sensor.transform.SetParent(root.transform,false);
        var collider=sensor.AddComponent<CapsuleCollider>(); collider.isTrigger=true; collider.center=Vector3.up*1.25f; collider.height=2.5f; collider.radius=.5f;
        Physics.SyncTransforms();
        foreach(bool triggers in new[]{false,true})
        {
            Physics.queriesHitTriggers=triggers;
            Debug.Log("GAZE_PROBE triggers="+triggers+" hit="+collider.Raycast(new Ray(new Vector3(0,1.25f,20),Vector3.back),out _,35));
        }
        var probeCamera=new GameObject("GazeTestCamera").AddComponent<Camera>();
        probeCamera.transform.position=new Vector3(0,1.25f,20); probeCamera.transform.rotation=Quaternion.LookRotation(Vector3.back);
        var ownBody=GameObject.CreatePrimitive(PrimitiveType.Cube); ownBody.transform.position=new Vector3(0,1.25f,18);
        var sensors=new Collider[]{collider}; Physics.SyncTransforms();
        if(!GazeProbe.Hit(probeCamera,sensors,35,1,ownBody.transform,out _,out var reason)) throw new Exception("Third-person gaze should ignore own body: "+reason);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=new Vector3(0,1.25f,10); Physics.SyncTransforms();
        if(GazeProbe.Hit(probeCamera,sensors,35,1,ownBody.transform,out _,out _)) throw new Exception("Gaze crossed a solid wall");
        UnityEngine.Object.DestroyImmediate(wall); Physics.SyncTransforms();
        probeCamera.transform.rotation=Quaternion.LookRotation(new Vector3(.3f,0,-1));
        if(GazeProbe.Hit(probeCamera,sensors,35,1,ownBody.transform,out _,out _)) throw new Exception("Off-axis gaze incorrectly counted as discovery");
        probeCamera.transform.rotation=Quaternion.LookRotation(Vector3.back);
        if(GazeProbe.Hit(probeCamera,sensors,10,1,ownBody.transform,out _,out _)) throw new Exception("Gaze exceeded configured range");
        for(int i=0;i<40;i++)
        {
            var accessory=GameObject.CreatePrimitive(PrimitiveType.Cube); accessory.transform.position=new Vector3(0,1.25f,19-i*.1f);
            accessory.transform.localScale=Vector3.one*.05f; accessory.transform.SetParent(ownBody.transform,true);
        }
        wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=new Vector3(0,1.25f,5); Physics.SyncTransforms();
        if(GazeProbe.Hit(probeCamera,sensors,35,1,ownBody.transform,out _,out _)) throw new Exception("Saturated obstruction buffer missed a solid wall");
        UnityEngine.Object.DestroyImmediate(wall);
        UnityEngine.Object.DestroyImmediate(ownBody); UnityEngine.Object.DestroyImmediate(probeCamera.gameObject);
        Debug.Log("FORESTCRAWLER_GAZE_OK: own-body exclusion, solid obstruction, direct aim and distance");
        UnityEngine.Object.DestroyImmediate(sensor);
        var model = root.transform.GetChild(0).gameObject; var animator = root.GetComponentInChildren<Animator>(); animator.enabled = false;
        var clips = animator.runtimeAnimatorController.animationClips;
        clips.First(c=>c.name=="idle").SampleAnimation(model,0);
        foreach(var bone in root.GetComponentsInChildren<Transform>().Where(t=>new[]{"Head","HeadTip","Chest","Hips","HandL","HandR","UpperArmL","UpperArmR"}.Contains(t.name))) Debug.Log("GAZE_BONE "+bone.name+" "+bone.position.ToString("F3"));
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "ValidationGround";
        ground.transform.localScale = new Vector3(60, 1, 200); ground.transform.position = new Vector3(0, -.5f, 15);
        var camera = new GameObject("PreviewCamera").AddComponent<Camera>(); camera.transform.position = new Vector3(3.5f, 2.2f, 5);
        camera.transform.LookAt(new Vector3(0, 1.25f, 0)); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f,.07f,.08f);
        camera.nearClipPlane = .1f; camera.farClipPlane = 100;
        RenderSettings.ambientLight = new Color(.35f,.35f,.35f);
        var light = new GameObject("Key").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40, -35, 0); light.intensity = 1.3f;
        var charge = clips.First(c => c.name == "charge");
        charge.SampleAnimation(model,0); var feet = new FootSolver(root);
        Transform Find(string name) => root.GetComponentsInChildren<Transform>().First(t => t.name == name);
        float maxDrift = 0; var details = new System.Text.StringBuilder();
        foreach (float motorSpeed in new[] {8f,12f,18f})
        foreach (float slope in new[] {0f,15f,30f,45f,60f,85f})
        {
            ground.transform.rotation = Quaternion.Euler(-slope,0,0); ground.transform.position = -ground.transform.up*.5f; feet.Release();
            Vector3[] last = new Vector3[2]; bool[] wasContact = new bool[2];
            for(int frame=0;frame<168;frame++)
            {
                float travel=frame*motorSpeed/60f; float phase=Mathf.Repeat(travel/5.6f,1);
                var origin = ground.transform.forward*travel+Vector3.up*6;
                Physics.SyncTransforms();
                if(!Physics.Raycast(origin,Vector3.down,out var hit,12,1)) throw new Exception("Ground missing");
                root.transform.position=hit.point; root.transform.rotation=Quaternion.FromToRotation(Vector3.up,hit.normal);
                charge.SampleAnimation(model,phase*charge.length); Physics.SyncTransforms();
                feet.Solve(true,phase,1f/60f);
                for(int side=0;side<2;side++)
                {
                    bool contact=Mathf.Repeat(phase+side*.5f,1)<.20f;
                    var foot=Find(side==0?"FootL":"FootR");
                    if(contact && wasContact[side])
                    {
                        float drift=Vector3.Distance(foot.position,last[side]);
                        if(drift>.035f) details.AppendLine($"speed={motorSpeed} slope={slope} frame={frame} side={side} phase={phase} drift={drift} foot={foot.position} hips={Find("Hips").position}");
                        maxDrift=Mathf.Max(maxDrift,drift);
                    }
                    last[side]=foot.position; wasContact[side]=contact;
                    if(!float.IsFinite(foot.position.y)) throw new Exception("Non-finite foot pose");
                }
            }
        }
        ground.transform.rotation=Quaternion.identity; ground.transform.position=new Vector3(0,-.5f,15); root.transform.position=Vector3.zero; root.transform.rotation=Quaternion.identity;
        var pausedHips=Find("Hips").position; var pausedFoot=Find("FootL").position;
        for(int i=0;i<120;i++) feet.Solve(true,0,0);
        if(Vector3.Distance(pausedHips,Find("Hips").position)>.000001f || Vector3.Distance(pausedFoot,Find("FootL").position)>.000001f) throw new Exception("Paused IK accumulated a pose offset");
        ground.GetComponent<Renderer>().enabled=false;
        camera.transform.position=new Vector3(0,1.25f,4); camera.transform.LookAt(new Vector3(0,1.25f,0)); camera.fieldOfView=40; camera.backgroundColor=Color.black;
        foreach(string name in new[]{"idle","scream","charge","opacity"})
        {
            camera.backgroundColor=name=="opacity"?new Color(1,.2f,1):Color.black;
            var clip=clips.First(c=>c.name==(name=="opacity"?"idle":name)); feet.Release();
            clip.SampleAnimation(model, name=="scream"?.7f:name=="charge"?.1f:0); Physics.SyncTransforms();
            feet.Solve(name=="charge",name=="charge"?.1f/.7f:0,1f/60f);
            details.AppendLine(name+" head="+Find("Head").localRotation+" calf="+Find("CalfL").localRotation+" foot="+Find("FootL").position);
            // Explicitly bake the evaluated pose for deterministic editor camera capture.
            var bakedObjects=new System.Collections.Generic.List<GameObject>();
            foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(!skin.name.EndsWith("0")) { skin.enabled=false; continue; }
                var mesh=new Mesh(); skin.BakeMesh(mesh);
                var baked=new GameObject("BakedPose"); baked.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);
                baked.AddComponent<MeshFilter>().sharedMesh=mesh; baked.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                bakedObjects.Add(baked); skin.enabled=false;
            }
            // Batch preview requires a graphics device; the asset build itself remains headless.
            var target=new RenderTexture(900,900,24); camera.targetTexture=target; camera.Render();
            var previous=RenderTexture.active; RenderTexture.active=target; var image=new Texture2D(900,900,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,900,900),0,0); image.Apply(); File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
            RenderTexture.active=previous; camera.targetTexture=null; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            foreach(var baked in bakedObjects) { UnityEngine.Object.DestroyImmediate(baked.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(baked); }
        }
        File.WriteAllText(Path.Combine(output,"validation.json"),"{\"engine\":\""+Application.unityVersion+"\",\"maxStanceDriftMetres\":"+maxDrift.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"slopesDegrees\":[0,15,30,45,60,85],\"gameTested\":false}");
        File.WriteAllText(Path.Combine(output,"contact-details.txt"),details.ToString());
        if(maxDrift>.035f) throw new Exception("Runtime foot solver stance drift exceeded 3.5cm: "+maxDrift);
        Debug.Log("FORESTCRAWLER_PREVIEW_OK maxStanceDriftMetres="+maxDrift);
    }
}
