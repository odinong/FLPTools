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
    // A startup animation is chosen as "style@rrggbb" (style id + accent colour). FL plays a horizontal
    // strip of frames named by the theme key SplashAnim=<frames>-<file> and keeps the registry value
    // General\SplashImage=<frames>-<full path>.

    public const int Frames = SplashRenderer.Frames;
    public const string DefaultAccent = "#ff8a1f";
    const string SplashPrefix = "flptools_splash_";
    const string SplashVersion = "v2";

    public static readonly string[] Accents = { "#ff8a1f", "#ff3d6e", "#16c7ff", "#3ee08b", "#a46bff", "#ffd23f", "#ffffff" };

    public static (string Style, Color Accent) ParseSpec(string spec)
    {
        var parts = spec.Split('@');
        var style = SplashRenderer.Styles.Any(x => x.Id == parts[0]) ? parts[0] : SplashRenderer.Styles[0].Id;
        Color accent;
        try { accent = ColorTranslator.FromHtml(parts.Length > 1 ? "#" + parts[1].TrimStart('#') : DefaultAccent); }
        catch { accent = ColorTranslator.FromHtml(DefaultAccent); }
        return (style, accent);
    }

    static string SpecKey(string spec)
    {
        var (style, c) = ParseSpec(spec);
        return $"{style}_{c.R:x2}{c.G:x2}{c.B:x2}_{SplashVersion}";
    }

    /// <summary>The sprite strip FL reads for a spec (lives next to the themes, where FL looks for SplashAnim files).</summary>
    public static string SplashFile(string spec) => Path.Combine(FlPaths.ThemesDir, SplashPrefix + SpecKey(spec) + ".png");

    /// <summary>Value FL keeps in General\SplashImage: "&lt;frames&gt;-&lt;full path&gt;".</summary>
    public static string SplashRegistryValue(string spec) => $"{Frames}-{SplashFile(spec)}";

    public static string EnsureSplash(string spec, string flExe)
    {
        var path = SplashFile(spec);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(FlPaths.ThemesDir);
            var (style, accent) = ParseSpec(spec);
            using var icon = LoadIcon(flExe);
            using var strip = SplashRenderer.Render(style, accent, icon, 1f);
            strip.Save(path, ImageFormat.Png);
        }
        return path;
    }

    static readonly Dictionary<string, string> previewCache = new();

    /// <summary>Half-size preview strip as a data URL for the UI.</summary>
    public static string SplashPreview(string spec, string flExe)
    {
        var key = SpecKey(spec);
        lock (previewCache)
            if (previewCache.TryGetValue(key, out var hit)) return hit;
        var (style, accent) = ParseSpec(spec);
        using var icon = LoadIcon(flExe);
        using var strip = SplashRenderer.Render(style, accent, icon, 0.5f);
        using var ms = new MemoryStream();
        strip.Save(ms, ImageFormat.Png);
        var url = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        lock (previewCache) previewCache[key] = url;
        return url;
    }

    /// <summary>Use a startup animation in every FL install (spec "" puts FL's own splash back).</summary>
    public static void ApplySplash(string spec, IEnumerable<FlInstall> installs)
    {
        var flExe = installs.Last().Exe;
        if (spec.Length > 0) EnsureSplash(spec, flExe);
        foreach (var i in installs)
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{FlInstalls.RegRoot}\{i.RegistryKey}\General");
            if (spec.Length > 0) k.SetValue("SplashImage", SplashRegistryValue(spec), RegistryValueKind.String);
            else if ((k.GetValue("SplashImage") as string ?? "").Contains(SplashPrefix)) k.DeleteValue("SplashImage", false);
        }
        // FL re-reads the theme's SplashAnim when it saves settings, so keep the shared current theme in step.
        var theme = Path.Combine(FlPaths.ThemesDir, "__current.flstheme");
        if (File.Exists(theme))
        {
            var t = FlTheme.Load(theme);
            var cur = t["SplashAnim"] ?? "";
            if (spec.Length > 0) t["SplashAnim"] = $"{Frames}-{Path.GetFileName(SplashFile(spec))}";
            else if (cur.Contains(SplashPrefix)) t["SplashAnim"] = null;
            t.Save(theme);
        }
        // drop strips no longer in use
        var keep = spec.Length > 0 ? Path.GetFileName(SplashFile(spec)) : "";
        if (Directory.Exists(FlPaths.ThemesDir))
            foreach (var old in Directory.EnumerateFiles(FlPaths.ThemesDir, SplashPrefix + "*.png"))
                if (!Path.GetFileName(old).Equals(keep, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(old); } catch { }
    }

    /// <summary>The spec currently set for the newest FL install, or "" for FL's own.</summary>
    public static string CurrentSplash(IEnumerable<FlInstall> installs)
    {
        var i = installs.LastOrDefault();
        if (i == null) return "";
        var name = Path.GetFileNameWithoutExtension(FlInstalls.GetGeneral(i, "SplashImage") ?? "");
        if (!name.StartsWith(SplashPrefix)) return "";
        var parts = name[SplashPrefix.Length..].Split('_');
        return parts.Length >= 2 ? $"{parts[0]}@{parts[1]}" : parts[0];
    }

    static Bitmap LoadIcon(string flExe)
    {
        using var icon = Icon.ExtractIcon(flExe, 0, 256) ?? Icon.ExtractAssociatedIcon(flExe)!;
        return icon.ToBitmap();
    }
}
