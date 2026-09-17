using System;
using System.Collections.Generic;
using System.IO;

namespace ForestCrawler;

internal sealed class Progression
{
    private readonly HashSet<string> teased = new();
    private readonly Cooldowns quiet = new();
    private string file = "";
    internal void Load(string directory, long world)
    {
        quiet.Load(directory, world, "quiet");
        file=Path.Combine(directory,"ForestCrawler","tease-"+world+".txt"); teased.Clear();
        if(File.Exists(file)) foreach(var line in File.ReadAllLines(file)) teased.Add(line);
    }
    internal bool HasTease(string identity) => teased.Contains(identity);
    internal double Remaining(string identity,double utc) => quiet.Remaining(identity,utc);
    internal void Start(string identity,double utc) => quiet.Start(identity,utc,15);
    internal void CompleteTease(string identity)
    {
        if(teased.Contains(identity)) return;
        var updated=new List<string>(teased) {identity};
        File.WriteAllLines(file+".tmp",updated);
        if(File.Exists(file)) File.Replace(file+".tmp",file,file+".previous"); else File.Move(file+".tmp",file);
        teased.Add(identity);
    }
}
