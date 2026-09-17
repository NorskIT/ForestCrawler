using System;
using BepInEx;
using UnityEngine;

namespace ForestCrawler;

[BepInPlugin(Id, "ForestCrawler", Version)]
[DefaultExecutionOrder(10000)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "norskit.ForestCrawler", Version = "0.2.6";
    internal static Plugin Instance = null!;
    internal Settings Settings = null!;
    internal AssetStore Assets = null!;
    internal Network Network = null!;
    internal Presentation View = null!;
    private bool commands;
    private void Awake()
    {
        Instance = this;
        Capture.Install();
        Settings = Settings.Bind(Config);
        Assets = new AssetStore(this);
        View = new Presentation(this);
        Network = new Network(this);
        Log("Loaded. Assets are checked when entering a world; dedicated servers never load the bundle.");
    }
    private void Update()
    {
        if (!commands && global::Console.instance) { global::Console.SetConsoleEnabledForThisSession(); RegisterCommands(); commands = true; }
        Network.Update();
        View.Update();
    }
    private void LateUpdate() { View.LateUpdate(); Capture.Current?.Render(); }
    private void OnGUI() => Capture.Draw();
    private void OnDestroy() { Network?.Dispose(); View?.Clear(); Assets?.Dispose(); Capture.Uninstall(); }
    internal void Log(string text) => Logger.LogInfo(text);
    internal void Notice(string text) { Log(text); if (global::Console.instance) global::Console.instance.AddString("ForestCrawler: " + text); }
    private void RegisterCommands()
    {
        void Command(string name, string help, Action<Terminal.ConsoleEventArgs> action) =>
            _ = new Terminal.ConsoleCommand(name, help, new Terminal.ConsoleEvent(a => action(a)));
        Command("crawler_spawn", "Spawn a persistent local idle preview on safe ground.", _ => Network.Debug("spawn"));
        Command("crawler_anim", "Preview animation: idle, scream, charge.", a => Network.Debug("anim " + (a.Args.Length > 1 ? a.Args[1] : "idle")));
        Command("crawler_encounter", "Admin: full encounter; bypass biome, time and cooldown, retain isolation.", a => Network.Debug(a.Args.Length > 1 && a.Args[1] == "tease" ? "encounter tease" : "encounter"));
        Command("crawler_start", "Admin: validate all criteria and advance shared world time to the midnight window.", _ => Network.Debug("start"));
        Command("crawler_clear", "Clear your ForestCrawler entities and audio; retain cooldown.", _ => { Network.ClearOwn(); View.Clear(); });
        Command("crawler_status", "Report assets, authority, phase, eligibility and scheduling.", _ => { Notice(Assets.Status + "; " + View.Status); Network.Debug("status"); });
    }
}
