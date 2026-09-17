using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ForestCrawler;

internal sealed class AssetStore : IDisposable
{
    private readonly Plugin plugin;
    private AssetBundle? bundle;
    private float retryAt;
    private Shader? horrorShader;
    internal Shader CloseupShader = null!;
    private readonly MaterialPropertyBlock properties = new();
    internal GameObject Prefab = null!, CloseupPrefab = null!;
    internal readonly Dictionary<string, AudioClip> Audio = new();
    internal float VoiceLength => Audio.TryGetValue("voice", out var clip) ? clip.length : 0;
    internal float ScreamLength => Audio.TryGetValue("attack", out var clip) ? clip.length : 0;
    internal float RevealLength { get; private set; }
    internal string Status { get; private set; } = "Assets not loaded";
    internal bool Ready { get; private set; }
    internal AssetStore(Plugin plugin) { this.plugin = plugin; }
    internal bool Ensure()
    {
        if (Ready) return true;
        if (ZNet.instance && ZNet.instance.IsDedicated()) { Status = "Headless authority: assets not required"; return false; }
        if (Time.realtimeSinceStartup < retryAt) return false;
        retryAt = Time.realtimeSinceStartup + 10;
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "forestcrawler.assets");
            if (!File.Exists(path)) { Status = "Missing forestcrawler.assets; runtime package is incomplete"; return false; }
            bundle ??= AssetBundle.LoadFromFile(path);
            if (!bundle) throw new InvalidDataException("AssetBundle could not be loaded");
            horrorShader = bundle.LoadAsset<Shader>("assets/crawler/forestcrawlerhorror.shader");
            if (!horrorShader || !horrorShader.isSupported) throw new InvalidDataException("Creature horror shader is missing or unsupported");
            Prefab = bundle.LoadAsset<GameObject>("assets/crawler/forestcrawler.prefab");
            if (!Prefab || Prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 3) throw new InvalidDataException("Prefab/LOD meshes missing");
            var animator = Prefab.GetComponentInChildren<Animator>(true);
            var clips = animator?.runtimeAnimatorController?.animationClips;
            foreach (string name in new[] { "idle", "scream", "charge" })
                if (clips == null || !clips.Any(c => c.name == name && c.length > .1f)) throw new InvalidDataException("Missing animation: " + name);
            RevealLength = clips!.First(c => c.name == "scream").length;
            foreach (var pair in new[] { ("whisper", "sound_scary_idle_female_whisper"), ("woo", "sound_scary_idle_woo_woo"), ("voice", "sound_scary_idle_i_see_you"), ("attack", "sound_scary_attack_scream"), ("caught1", "sound_scary_attack_caught_player_1"), ("caught2", "sound_scary_attack_caught_player_2") })
            {
                var clip = bundle.LoadAsset<AudioClip>("assets/crawler/" + pair.Item2 + ".mp3");
                if (!clip || clip.length <= 0) throw new InvalidDataException("Missing audio: " + pair.Item1);
                clip.LoadAudioData(); Audio[pair.Item1] = clip;
            }
            CloseupShader = bundle.LoadAsset<Shader>("assets/crawler/crawlercloseup.shader");
            if (!CloseupShader || !CloseupShader.isSupported) throw new InvalidDataException("Missing closeup shader");
            CloseupPrefab = bundle.LoadAsset<GameObject>("assets/crawler/crawlercloseup.prefab");
            if (!CloseupPrefab || CloseupPrefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh.blendShapeCount != 3)
                throw new InvalidDataException("Closeup facial morphs missing");
            foreach (string key in new[] { "heartbeat", "close" })
            {
                var processed = bundle.LoadAsset<AudioClip>("assets/crawler/" + key + ".wav");
                if (!processed || processed.length <= 0) throw new InvalidDataException("Missing processed audio " + key);
                Audio[key] = processed;
            }
            if (Audio["heartbeat"].length > .241f) throw new InvalidDataException("Heartbeat exceeds 230 BPM pulse budget");
            Ready = true; Status = "Assets ready: 3 LODs, idle/scream/charge, 8 audio assets, closeup with three facial morphs"; plugin.Log(Status);
        }
        catch (Exception e) { Status = "Asset validation failed: " + e.Message; plugin.Log(Status); }
        return Ready;
    }
    internal GameObject Instantiate(Vector3 position)
    {
        if (!Ensure()) throw new InvalidOperationException(Status);
        var obj = UnityEngine.Object.Instantiate(Prefab, position, Quaternion.identity); obj.name = "ForestCrawler_Local";
        var renderers = obj.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            // Preserve source texture assignments in the creature-only effect or ordinary game material.
            var shader = plugin.Settings.HorrorStrength > 0 ? horrorShader :
                Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(s => s.name == "Custom/Creature") ?? Shader.Find("Standard");
            foreach (var material in renderer.materials)
            {
                var texture = material.mainTexture;
                if (shader) material.shader = shader;
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
                if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .12f);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
                if (material.HasProperty("_HorrorStrength")) material.SetFloat("_HorrorStrength", plugin.Settings.HorrorStrength);
                if (material.HasProperty("_ChromaticPixels")) material.SetFloat("_ChromaticPixels", plugin.Settings.ChromaticPixels);
                if (material.HasProperty("_GrainPixels")) material.SetFloat("_GrainPixels", plugin.Settings.GrainPixels);
            }
        }
        UpdateOrigin(renderers, position.y); obj.SetActive(true); return obj;
    }
    internal void UpdateOrigin(Renderer[] renderers, float height)
    {
        properties.Clear(); properties.SetFloat("_CrawlerOriginY", height);
        foreach (var renderer in renderers) renderer.SetPropertyBlock(properties);
    }
    public void Dispose() { Audio.Clear(); if (bundle) bundle!.Unload(true); bundle = null; Ready = false; }
}
