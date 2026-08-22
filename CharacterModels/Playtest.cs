using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CharacterModels;

/// <summary>
/// Scripted input for automated play-testing. A script is a ';'-separated timeline:
///   "w 1.5; w+shift 2; a+w 0.5; idle 0.8; q; idle 1.5; n; cam 40 15 4; wind 1.3"
/// Each step holds a key combination for a duration (seconds); a step with no duration is a 1-frame tap.
/// The script replaces Keyboard.GetState() so the rest of the game is exercised unchanged.
/// </summary>
public sealed class InputScript
{
    public sealed class Step
    {
        public Keys[] Keys = Array.Empty<Keys>();
        public float Duration;
        public string[]? Command;       // non-key command (cam, wind, ...), handled by the game when the step starts
        public string Text = "";
    }

    private static readonly Dictionary<string, Keys> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["w"] = Keys.W, ["a"] = Keys.A, ["s"] = Keys.S, ["d"] = Keys.D, ["shift"] = Keys.LeftShift, ["q"] = Keys.Q, ["e"] = Keys.E,
        ["x"] = Keys.X, ["h"] = Keys.H, ["f"] = Keys.F, ["tab"] = Keys.Tab, ["n"] = Keys.N, ["t"] = Keys.T, ["b"] = Keys.B, ["g"] = Keys.G,
        ["v"] = Keys.V, ["r"] = Keys.R, ["l"] = Keys.L, ["k"] = Keys.K, ["space"] = Keys.Space, ["left"] = Keys.Left, ["right"] = Keys.Right,
        ["up"] = Keys.Up, ["down"] = Keys.Down, ["1"] = Keys.D1, ["2"] = Keys.D2, ["3"] = Keys.D3, ["4"] = Keys.D4, ["5"] = Keys.D5,
        ["6"] = Keys.D6, ["7"] = Keys.D7
    };

    static InputScript()
    {
        // Every single letter maps to its key; the table above only needs the special names.
        for (char ch = 'a'; ch <= 'z'; ch++) KeyNames.TryAdd(ch.ToString(), (Keys)((int)Keys.A + (ch - 'a')));
    }

    public readonly List<Step> Steps = new();
    private int _index = -1;
    private float _stepTime;
    public float Time { get; private set; }
    public bool Done => _index >= Steps.Count;
    public Step? Current => _index >= 0 && _index < Steps.Count ? Steps[_index] : null;
    /// <summary>Raised when a non-key command step begins (args after the command word).</summary>
    public event Action<string[]>? Command;

    public static InputScript Parse(string text)
    {
        var script = new InputScript();
        foreach (var raw in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var step = new Step { Text = raw.Trim() };
            string head = parts[0];
            if (head.Equals("idle", StringComparison.OrdinalIgnoreCase) || head.Equals("wait", StringComparison.OrdinalIgnoreCase))
                step.Duration = parts.Length > 1 ? ParseF(parts[1]) : 1f;
            else if (KeyNames.ContainsKey(head) || head.Contains('+'))
            {
                var keys = new List<Keys>();
                foreach (var k in head.Split('+'))
                {
                    if (!KeyNames.TryGetValue(k, out var key)) throw new ArgumentException($"Unknown key '{k}' in script step '{raw}'");
                    keys.Add(key);
                }
                step.Keys = keys.ToArray();
                step.Duration = parts.Length > 1 ? ParseF(parts[1]) : 0f;        // 0 = one-frame tap
            }
            else step.Command = parts;                                            // e.g. "cam 40 15 4", "wind 1.3", "shot name"
            script.Steps.Add(step);
        }
        return script;
    }

    private static float ParseF(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Advances the timeline by dt and returns the synthetic keyboard state for this frame.</summary>
    public KeyboardState Advance(float dt)
    {
        Time += dt;
        if (_index < 0) Begin(0);
        while (!Done)
        {
            var step = Steps[_index];
            if (step.Command != null || step.Duration <= 0f)
            {
                // Commands are instantaneous; taps hold for exactly this one frame.
                var keys = step.Keys;
                Begin(_index + 1);
                return new KeyboardState(keys);
            }
            if (_stepTime < step.Duration) { _stepTime += dt; return new KeyboardState(step.Keys); }
            Begin(_index + 1);
        }
        return new KeyboardState();
    }

    private void Begin(int index)
    {
        _index = index; _stepTime = 0;
        while (_index < Steps.Count && Steps[_index].Command != null)
        {
            Command?.Invoke(Steps[_index].Command!);
            _index++;
        }
    }
}

/// <summary>Saves numbered PNG frames at an interval and a per-frame CSV of gameplay metrics.</summary>
public sealed class PlaytestRecorder : IDisposable
{
    private readonly string? _frameDir;
    private readonly float _every;
    private float _nextShot;
    private int _frameNo;
    private readonly StreamWriter? _log;
    public readonly List<string> Saved = new();

    public PlaytestRecorder(string? frameDir, float every, string? logPath)
    {
        _frameDir = frameDir; _every = Math.Max(every, 1f / 60f);
        if (frameDir != null) Directory.CreateDirectory(frameDir);
        if (logPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
            _log = new StreamWriter(logPath);
            _log.WriteLine("t,step,x,z,speed,yaw_deg,stride_phase,head_lat_cm,head_fwd_cm,clip,alloc_bytes,cpu_ms");
        }
    }

    public void Log(float t, string step, Character? c, long alloc, double cpuMs)
    {
        if (_log == null) return;
        if (c == null) { _log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{t:0.000},{step},,,,,,,,,{alloc},{cpuMs:0.00}")); return; }
        var hips = c.Skeleton["hips"].World.Translation; var head = c.Skeleton["head"].World.Translation;
        var rel = Vector3.TransformNormal(head - hips, Matrix.CreateRotationY(-c.Yaw));
        _log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{t:0.000},{step},{c.Position.X:0.000},{c.Position.Z:0.000},{c.Speed:0.00},{MathHelper.ToDegrees(c.Yaw):0.0},{c.StridePhase:0.00},{rel.X * 100:0.0},{rel.Z * 100:0.0},{c.Player.Current?.Name},{alloc},{cpuMs:0.00}"));
    }

    /// <summary>Call after the frame is rendered into target; saves when the interval has elapsed.</summary>
    public void MaybeSave(float t, RenderTarget2D target, string? label = null)
    {
        if (_frameDir == null) return;
        if (label == null && t < _nextShot) return;
        _nextShot = t + _every;
        string name = Path.Combine(_frameDir, label != null ? $"{label}.png" : $"f{_frameNo++:000}_{t:0.00}s.png");
        using var fs = File.Create(name);
        target.SaveAsPng(fs, target.Width, target.Height);
        Saved.Add(name);
    }

    public void Dispose() => _log?.Dispose();
}
