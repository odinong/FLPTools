using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using FLPTools.Flp;
using FLPTools.Launcher;

namespace FLPTools.Mods;

/// <summary>
/// Mods that change FL Studio itself:
///  - the Companion controller script (runs inside FL on a MIDI input)
///  - startup animations (FL plays a sprite strip named by the theme's SplashAnim / registry SplashImage)
/// </summary>
public static class FlMods
{
    // ------------------------------------------------------------------ companion script

    public const string CompanionName = "FLPTools Companion";
    public static string CompanionDir => Path.Combine(FlPaths.SettingsDir, "Hardware", CompanionName);
    static string CompanionScript => Path.Combine(CompanionDir, $"device_{CompanionName}.py");
    static string CompanionConfig => Path.Combine(CompanionDir, "flptools_companion.json");
    static string CompanionLog => Path.Combine(CompanionDir, "flptools_companion.log");

    static string CompanionSource()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("companion/device_FLPTools Companion.py")!;
        return new StreamReader(s, Encoding.UTF8).ReadToEnd();
    }

    public static JsonObject DefaultCompanionConfig() => new()
    {
        ["autoColor"] = true, ["colorChannels"] = true, ["colorMixer"] = true, ["colorPlaylist"] = true,
        ["mixerFollowsChannel"] = true, ["autosaveMinutes"] = 0, ["sessionHintMinutes"] = 30,
        ["rules"] = new JsonArray(
            Rule("#e5533d", "kick", "kik", "bd"),
            Rule("#f0a030", "snare", "clap", "rim", "snr"),
            Rule("#e8d44d", "hat", "hh", "hihat", "cymbal", "ride", "crash", "open"),
            Rule("#8bc34a", "perc", "shaker", "tom", "conga", "bongo", "tamb"),
            Rule("#9b59d0", "808", "bass", "sub"),
            Rule("#e85d9e", "vox", "vocal", "voc", "adlib", "voice", "chop"),
            Rule("#3d8fe0", "pad", "chord", "keys", "piano", "synth", "lead", "pluck", "bell", "melody", "arp", "guitar"),
            Rule("#3cc7c7", "fx", "riser", "impact", "sweep", "noise", "reverse", "transition", "drop"),
            Rule("#7f8c8d", "bus", "send", "reverb", "delay", "group")),
    };

    static JsonObject Rule(string color, params string[] words) =>
        new() { ["color"] = color, ["words"] = new JsonArray(words.Select(w => (JsonNode)w).ToArray()) };

    public static JsonObject CompanionStatus(IEnumerable<FlInstall> installs)
    {
        bool installed = File.Exists(CompanionScript);
        JsonNode? config = null;
        try { if (File.Exists(CompanionConfig)) config = JsonNode.Parse(File.ReadAllText(CompanionConfig)); } catch { }
        var assigned = new JsonArray();
        var inputs = new JsonArray();
        foreach (var i in installs)
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{FlInstalls.RegRoot}\{i.RegistryKey}\Devices\MIDI input");
            if (k == null) continue;
            foreach (var name in k.GetSubKeyNames())
            {
                using var d = k.OpenSubKey(name);
                var folder = d?.GetValue("ScriptFolder") as string ?? "";
                if (folder.Contains(CompanionName, StringComparison.OrdinalIgnoreCase)) assigned.Add($"FL Studio {i.Year}: {name}");
                if (i == installs.Last()) inputs.Add(name);
            }
        }
        var log = File.Exists(CompanionLog) ? File.ReadLines(CompanionLog).TakeLast(8).ToArray() : Array.Empty<string>();
        return new JsonObject
        {
            ["installed"] = installed,
            ["upToDate"] = installed && File.ReadAllText(CompanionScript) == CompanionSource(),
            ["config"] = config ?? DefaultCompanionConfig(),
            ["assigned"] = assigned,
            ["inputs"] = inputs,
            ["log"] = new JsonArray(log.Select(l => (JsonNode)l).ToArray()),
            ["folder"] = CompanionDir,
        };
    }

    public static void InstallCompanion()
    {
        Directory.CreateDirectory(CompanionDir);
        File.WriteAllText(CompanionScript, CompanionSource(), new UTF8Encoding(false));
        if (!File.Exists(CompanionConfig)) SaveCompanionConfig(DefaultCompanionConfig());
    }

    public static void UninstallCompanion()
    {
        if (!Directory.Exists(CompanionDir)) return;
        foreach (var f in new[] { CompanionScript, CompanionConfig, CompanionLog }) if (File.Exists(f)) File.Delete(f);
        if (!Directory.EnumerateFileSystemEntries(CompanionDir).Any(e => !Path.GetFileName(e).Equals("__pycache__", StringComparison.OrdinalIgnoreCase)))
            Directory.Delete(CompanionDir, true);
    }

    public static void SaveCompanionConfig(JsonNode config)
    {
        Directory.CreateDirectory(CompanionDir);
        File.WriteAllText(CompanionConfig, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    // ------------------------------------------------------------------ startup animations

    public sealed record SplashStyle(string Id, string Label);

    public static readonly SplashStyle[] SplashStyles =
    {
        new("rise", "Rise"), new("shockwave", "Shockwave"), new("spin", "Spin"), new("glitch", "Glitch"), new("orbit", "Orbit"),
    };

    public const int Frames = 48, FrameW = 400, FrameH = 569;
    const string SplashPrefix = "flptools_splash_";

    /// <summary>The sprite strip FL reads for a style (lives next to the themes, where FL looks for SplashAnim files).</summary>
    public static string SplashFile(string style) => Path.Combine(FlPaths.ThemesDir, SplashPrefix + style + ".png");

    /// <summary>Value FL keeps in General\SplashImage: "&lt;frames&gt;-&lt;full path&gt;".</summary>
    public static string SplashRegistryValue(string style) => $"{Frames}-{SplashFile(style)}";

    public static string EnsureSplash(string style, string flExe)
    {
        var path = SplashFile(style);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(FlPaths.ThemesDir);
            using var strip = RenderSplash(style, flExe, 1f);
            strip.Save(path, ImageFormat.Png);
        }
        return path;
    }

    static readonly Dictionary<string, string> previewCache = new();

    /// <summary>Half-size preview strip as a data URL for the UI.</summary>
    public static string SplashPreview(string style, string flExe)
    {
        if (previewCache.TryGetValue(style, out var hit)) return hit;
        using var strip = RenderSplash(style, flExe, 0.5f);
        using var ms = new MemoryStream();
        strip.Save(ms, ImageFormat.Png);
        return previewCache[style] = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>Use a startup animation in every FL install (style "" puts FL's own splash back).</summary>
    public static void ApplySplash(string style, IEnumerable<FlInstall> installs)
    {
        var flExe = installs.Last().Exe;
        if (style.Length > 0) EnsureSplash(style, flExe);
        foreach (var i in installs)
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{FlInstalls.RegRoot}\{i.RegistryKey}\General");
            if (style.Length > 0) k.SetValue("SplashImage", SplashRegistryValue(style), RegistryValueKind.String);
            else if ((k.GetValue("SplashImage") as string ?? "").Contains(SplashPrefix)) k.DeleteValue("SplashImage", false);
        }
        // FL re-reads the theme's SplashAnim when it saves settings, so keep the shared current theme in step.
        var theme = Path.Combine(FlPaths.ThemesDir, "__current.flstheme");
        if (File.Exists(theme))
        {
            var t = FlTheme.Load(theme);
            var cur = t["SplashAnim"] ?? "";
            if (style.Length > 0) t["SplashAnim"] = $"{Frames}-{Path.GetFileName(SplashFile(style))}";
            else if (cur.Contains(SplashPrefix)) t["SplashAnim"] = null;
            t.Save(theme);
        }
    }

    public static string CurrentSplash(IEnumerable<FlInstall> installs)
    {
        var i = installs.LastOrDefault();
        if (i == null) return "";
        var v = FlInstalls.GetGeneral(i, "SplashImage") ?? "";
        var name = Path.GetFileNameWithoutExtension(v);
        return name.StartsWith(SplashPrefix) ? name[SplashPrefix.Length..] : "";
    }

    static Bitmap LoadIcon(string flExe)
    {
        using var icon = Icon.ExtractIcon(flExe, 0, 256) ?? Icon.ExtractAssociatedIcon(flExe)!;
        return icon.ToBitmap();
    }

    static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
    static float EaseOut(float t) => 1 - MathF.Pow(1 - Clamp01(t), 3);
    static float EaseOutBack(float t) { t = Clamp01(t); const float c = 1.9f; return 1 + (c + 1) * MathF.Pow(t - 1, 3) + c * MathF.Pow(t - 1, 2); }

    public static Bitmap RenderSplash(string style, string flExe, float scale)
    {
        int fw = (int)(FrameW * scale), fh = (int)(FrameH * scale);
        var strip = new Bitmap(fw * Frames, fh, PixelFormat.Format32bppArgb);
        using var icon = LoadIcon(flExe);
        using var g = Graphics.FromImage(strip);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float baseSize = 230 * scale;
        var rnd = new Random(7);

        for (int f = 0; f < Frames; f++)
        {
            float t = f / (float)(Frames - 1);
            float cx = f * fw + fw / 2f, cy = fh / 2f;
            g.SetClip(new RectangleF(f * fw, 0, fw, fh));

            void DrawIcon(float size, float alpha, float angle = 0, float dx = 0, float dy = 0, float[]? tint = null)
            {
                if (size <= 1 || alpha <= 0.01f) return;
                var state = g.Save();
                g.TranslateTransform(cx + dx, cy + dy);
                if (angle != 0) g.RotateTransform(angle);
                var m = new ColorMatrix { Matrix33 = Clamp01(alpha) };
                if (tint != null) { m.Matrix00 = tint[0]; m.Matrix11 = tint[1]; m.Matrix22 = tint[2]; }
                using var ia = new ImageAttributes();
                ia.SetColorMatrix(m);
                g.DrawImage(icon, new Rectangle((int)(-size / 2), (int)(-size / 2), (int)size, (int)size), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                g.Restore(state);
            }
            void Ring(float radius, float width, float alpha, Color c)
            {
                if (radius <= 0 || alpha <= 0.01f) return;
                using var pen = new Pen(Color.FromArgb((int)(255 * Clamp01(alpha)), c), Math.Max(1, width));
                g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
            }
            void Glow(float radius, float alpha, Color c)
            {
                if (radius <= 1 || alpha <= 0.01f) return;
                using var path = new GraphicsPath();
                path.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
                using var br = new PathGradientBrush(path) { CenterColor = Color.FromArgb((int)(255 * Clamp01(alpha)), c), SurroundColors = new[] { Color.FromArgb(0, c) } };
                g.FillEllipse(br, cx - radius, cy - radius, radius * 2, radius * 2);
            }
            var orange = Color.FromArgb(255, 160, 60);
            var white = Color.White;

            switch (style)
            {
                case "shockwave":
                {
                    float pop = EaseOutBack(t / 0.3f);
                    Glow(baseSize * 0.9f * EaseOut(t / 0.4f), 0.35f * (1 - Clamp01((t - 0.4f) / 0.6f)) + 0.12f, orange);
                    for (int r = 0; r < 3; r++)
                    {
                        float rt = Clamp01((t - 0.12f - r * 0.12f) / 0.55f);
                        if (rt > 0 && rt < 1) Ring(baseSize * (0.45f + rt * 0.9f), 6 * scale * (1 - rt) + 1, (1 - rt) * 0.8f, r == 1 ? orange : white);
                    }
                    DrawIcon(baseSize * pop, Clamp01(t / 0.12f));
                    break;
                }
                case "spin":
                {
                    float p = EaseOut(t / 0.7f);
                    for (int k = 4; k >= 1; k--)
                    {
                        float pk = EaseOut((t - k * 0.035f) / 0.7f);
                        if (pk > 0 && p < 0.999f) DrawIcon(baseSize * pk, 0.18f * (1 - p) * (5 - k) / 4f, -540 * (1 - pk));
                    }
                    Glow(baseSize * 0.75f, 0.25f * Clamp01((t - 0.55f) / 0.3f), orange);
                    DrawIcon(baseSize * p, Clamp01(t / 0.2f), -540 * (1 - p));
                    break;
                }
                case "glitch":
                {
                    float settle = Clamp01((t - 0.15f) / 0.6f);
                    float jitter = (1 - settle) * 24 * scale;
                    float alpha = Clamp01(t / 0.1f);
                    bool off = f < 26 && rnd.NextDouble() < 0.25 * (1 - settle);
                    if (!off)
                    {
                        DrawIcon(baseSize, alpha * 0.85f * (1 - settle), 0, -jitter * (float)rnd.NextDouble() - jitter * 0.4f, 0, new[] { 1f, 0f, 0f });
                        DrawIcon(baseSize, alpha * 0.85f * (1 - settle), 0, jitter * (float)rnd.NextDouble() + jitter * 0.4f, 0, new[] { 0f, 0.6f, 1f });
                        DrawIcon(baseSize, alpha * (0.35f + 0.65f * settle), 0, (float)(rnd.NextDouble() - 0.5) * jitter * 0.5f, 0);
                        if (settle < 0.9f)
                            for (int s = 0; s < 4; s++)
                            {
                                float y = cy + (float)(rnd.NextDouble() - 0.5) * baseSize;
                                using var br = new SolidBrush(Color.FromArgb((int)(120 * (1 - settle)), rnd.Next(2) == 0 ? orange : white));
                                g.FillRectangle(br, cx - baseSize * 0.6f + (float)(rnd.NextDouble() - 0.5) * jitter * 3, y, baseSize * 1.2f, 2 * scale + (float)rnd.NextDouble() * 4 * scale);
                            }
                    }
                    break;
                }
                case "orbit":
                {
                    int dots = 16;
                    float collapse = EaseOut(t / 0.65f);
                    float radius = baseSize * (1.15f - collapse * 1.1f);
                    for (int d = 0; d < dots; d++)
                    {
                        float a = d / (float)dots * MathF.PI * 2 + t * 7f;
                        float dr = 7 * scale * (1 - collapse * 0.6f);
                        float alpha = Clamp01(t / 0.1f) * (1 - Clamp01((t - 0.6f) / 0.12f));
                        using var br = new SolidBrush(Color.FromArgb((int)(255 * alpha), d % 3 == 0 ? orange : white));
                        g.FillEllipse(br, cx + MathF.Cos(a) * radius - dr, cy + MathF.Sin(a) * radius - dr, dr * 2, dr * 2);
                    }
                    float show = EaseOutBack((t - 0.58f) / 0.3f);
                    Glow(baseSize * 0.8f, 0.4f * Clamp01((t - 0.55f) / 0.1f) * (1 - Clamp01((t - 0.75f) / 0.25f)) + 0.1f * Clamp01((t - 0.6f) / 0.2f), orange);
                    DrawIcon(baseSize * show, Clamp01((t - 0.58f) / 0.1f));
                    break;
                }
                default: // rise
                {
                    float up = EaseOutBack(t / 0.55f);
                    float alpha = Clamp01(t / 0.25f);
                    Glow(baseSize * 0.85f, 0.28f * alpha, orange);
                    float rt = Clamp01((t - 0.35f) / 0.5f);
                    if (rt > 0 && rt < 1) Ring(baseSize * (0.55f + rt * 0.6f), 4 * scale * (1 - rt) + 1, (1 - rt) * 0.7f, white);
                    DrawIcon(baseSize * (0.6f + 0.4f * up), alpha, 0, 0, (1 - up) * 80 * scale);
                    break;
                }
            }
            g.ResetClip();
        }
        return strip;
    }
}
