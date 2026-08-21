using System;
using System.Collections.Generic;

namespace CharacterModels;

public static class Program
{
    /// <summary>Startup options: --yaw deg --pitch deg --dist m --focus n --clip n --no-orbit --light deg --varied</summary>
    public static readonly Dictionary<string, string> Options = new(StringComparer.OrdinalIgnoreCase);

    public static float Opt(string key, float fallback) =>
        Options.TryGetValue(key, out var v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : fallback;

    public static bool Flag(string key) => Options.ContainsKey(key);

    [STAThread]
    public static void Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            string key = args[i][2..];
            string val = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "1";
            Options[key] = val;
        }
        using var game = new Game1();
        game.Run();
    }
}
