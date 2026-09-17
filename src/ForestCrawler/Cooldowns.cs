using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ForestCrawler;

internal sealed class Cooldowns
{
    private readonly Dictionary<string, double> deadlines = new();
    private string file = "";
    internal void Load(string configDirectory, long world, string prefix = "cooldowns")
    {
        deadlines.Clear();
        string dir = Path.Combine(configDirectory, "ForestCrawler"); Directory.CreateDirectory(dir);
        file = Path.Combine(dir, prefix + "-" + world + ".txt");
        if (!File.Exists(file)) return;
        foreach (var line in File.ReadAllLines(file))
        {
            var parts = line.Split('\t');
            if (parts.Length == 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && Rules.Finite(value)) deadlines[parts[0]] = value;
        }
    }
    internal double Remaining(string identity, double utc) => deadlines.TryGetValue(identity, out double deadline) ? Math.Max(0, deadline - utc) : 0;
    internal void Start(string identity, double utc, float minutes)
    {
        deadlines[identity] = utc + minutes * 60;
        var lines = new List<string>();
        foreach (var item in deadlines) if (item.Value > utc) lines.Add(item.Key + "\t" + item.Value.ToString("R", CultureInfo.InvariantCulture));
        File.WriteAllLines(file + ".tmp", lines);
        if (File.Exists(file)) File.Replace(file + ".tmp", file, file + ".previous"); else File.Move(file + ".tmp", file);
    }
}
