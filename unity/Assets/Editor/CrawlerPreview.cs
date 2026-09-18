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
    private static void ValidateArms(GameObject model)
    {
        var rig=new ExtendedArms(model);
        var transforms=model.GetComponentsInChildren<Transform>();
        var saved=transforms.Select(t=>(t.localPosition,t.localRotation,t.localScale)).ToArray();
        var left=transforms.First(t=>t.name=="UpperArmL"); var right=transforms.First(t=>t.name=="UpperArmR");
        float width=Mathf.Abs(Vector3.Dot(left.position-right.position,model.transform.right));
        float sideSign=Mathf.Sign(Vector3.Dot(left.position-right.position,model.transform.right));
        foreach(float distance in new[]{3f,12f,30f})
        foreach(float height in new[]{0f,6f,15f})
        foreach(float lateral in new[]{-5f,0f,5f})
        {
            var target=model.transform.position+Vector3.forward*distance+Vector3.up*height+Vector3.right*lateral;
            rig.Pose(target,1);
            var axis=Vector3.Cross(Vector3.up,target-(left.position+right.position)*.5f).normalized;
            foreach(string joint in new[]{"Forearm","Hand"})
            {
                var difference=transforms.First(t=>t.name==joint+"L").position-transforms.First(t=>t.name==joint+"R").position;
                if(Mathf.Abs(Vector3.Dot(difference,axis)-sideSign*width)>.03f) throw new Exception("Arms crossed or changed rail width at "+joint);
            }
            var skin=model.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(r=>r.sharedMesh.vertexCount).First();

            var baked=new Mesh(); skin.BakeMesh(baked, true);
            var points=baked.vertices.Select(v=>skin.localToWorldMatrix.MultiplyPoint3x4(v)).ToArray();
            if(points.Any(v=>float.IsNaN(v.x)||float.IsInfinity(v.x)) || points.Min(v=>Vector3.Distance(v,target))>1.2f)
                throw new Exception("Rendered skin does not reach arm target at "+distance+"m; nearest="+points.Min(v=>Vector3.Distance(v,target)));
            UnityEngine.Object.DestroyImmediate(baked); rig.Restore();
            for(int i=0;i<transforms.Length;i++)
                if(transforms[i].localPosition!=saved[i].Item1 || transforms[i].localRotation!=saved[i].Item2 || transforms[i].localScale!=saved[i].Item3)
                    throw new Exception("Arm restoration altered authored rig");
        }
        rig.Dispose(); Debug.Log("FORESTCRAWLER_ARMS_OK: parallel skin reach at 3/12/30m, height/side offsets, constant width and authored pose restoration");
    }
    public static void Validate()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ValidateMusicSilence();
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/editor-preview")); Directory.CreateDirectory(output);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Crawler/ForestCrawler.prefab");
        var root = UnityEngine.Object.Instantiate(prefab); root.name = "PreviewCreature";
        ValidateArms(root);
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
        foreach(string name in new[]{"idle","scream","charge","opacity","arms"})
        {
            camera.backgroundColor=name=="opacity"?new Color(1,.2f,1):Color.black;
            var clip=clips.First(c=>c.name==(name=="opacity" || name=="arms" ? "idle":name)); feet.Release();
            clip.SampleAnimation(model, name=="scream"?.7f:name=="charge"?.1f:0); Physics.SyncTransforms();
            feet.Solve(name=="charge",name=="charge"?.1f/.7f:0,1f/60f);
            ExtendedArms armPreview=null;
            if(name=="arms")
            {
                armPreview=new ExtendedArms(root); armPreview.Pose(new Vector3(0,6,12),1);
                camera.transform.position=new Vector3(13,8,6); camera.transform.LookAt(new Vector3(0,3,6)); camera.fieldOfView=65;
            }
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
            armPreview?.Dispose();
            foreach(var baked in bakedObjects) { UnityEngine.Object.DestroyImmediate(baked.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(baked); }
        }
        File.WriteAllText(Path.Combine(output,"validation.json"),"{\"engine\":\""+Application.unityVersion+"\",\"maxStanceDriftMetres\":"+maxDrift.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"slopesDegrees\":[0,15,30,45,60,85],\"gameTested\":false}");
        File.WriteAllText(Path.Combine(output,"contact-details.txt"),details.ToString());
        if(maxDrift>.035f) throw new Exception("Runtime foot solver stance drift exceeded 3.5cm: "+maxDrift);
        Debug.Log("FORESTCRAWLER_PREVIEW_OK maxStanceDriftMetres="+maxDrift);
    }
}
