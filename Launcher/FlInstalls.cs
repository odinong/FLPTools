using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Win32;
using FLPTools.Flp;

namespace FLPTools.Launcher;

public sealed class FlInstall
{
    public string Id = "";          // install folder name, e.g. "FL Studio 2025"
    public string Dir = "";
    public string Exe = "";
    public string Version = "";     // file version, e.g. 25.2.5.5319
    public int Major;
    public string RegistryKey = ""; // HKCU\Software\Image-Line\<this>
    public string Year => Id.Replace("FL Studio", "").Trim();
}

/// <summary>Finds FL Studio installs and reads per-version data (recent projects, running state, icon).</summary>
public static class FlInstalls
{
    public const string RegRoot = @"Software\Image-Line";

    public static List<FlInstall> Probe()
    {
        var list = new List<FlInstall>();
        foreach (var dir in FlPaths.Installs)
        {
            var exe = Path.Combine(dir, "FL64.exe");
            if (!File.Exists(exe)) exe = Path.Combine(dir, "FL.exe");
            var ver = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "";
            int.TryParse(ver.Split('.')[0], out int major);
            list.Add(new FlInstall
            {
                Id = Path.GetFileName(dir),
                Dir = dir,
                Exe = exe,
                Version = ver,
                Major = major,
                RegistryKey = RegistryKeyFor(major),
            });
        }
        return list.OrderBy(i => i.Major).ToList();
    }

    /// <summary>FL 21-24 share the "FL Studio 21" key; FL 25 and later get their own.</summary>
    static string RegistryKeyFor(int major)
    {
        using var root = Registry.CurrentUser.OpenSubKey(RegRoot);
        var names = root?.GetSubKeyNames() ?? Array.Empty<string>();
        if (names.Contains($"FL Studio {major}")) return $"FL Studio {major}";
        var older = names.Where(n => n.StartsWith("FL Studio ") && int.TryParse(n[10..], out int m) && m <= major)
            .OrderByDescending(n => int.Parse(n[10..])).FirstOrDefault();
        return older ?? $"FL Studio {major}";
    }

    public static string ArtHost(FlInstall i) => $"art{i.Major}.flptools";

    /// <summary>Wallpapers and theme pictures that ship with this FL install, as URLs the page can load.</summary>
    public static List<(string Name, string Url)> Artwork(FlInstall i)
    {
        var root = Path.Combine(i.Dir, "Artwork");
        if (!Directory.Exists(root)) return new();
        var exts = new[] { ".jpg", ".jpeg", ".png" };
        return new[] { "Wallpapers", "Themes" }
            .Select(d => Path.Combine(root, d)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()) && new FileInfo(f).Length > 60_000
                        && !Path.GetFileName(f).StartsWith("thm", StringComparison.OrdinalIgnoreCase)
                        && !f.Contains("splash", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path.GetFileNameWithoutExtension(f),
                          $"https://{ArtHost(i)}/" + Uri.EscapeDataString(Path.GetRelativePath(root, f)).Replace("%5C", "/")))
            .ToList();
    }

    /// <summary>A default backdrop per version so the tiles don't all look the same.</summary>
    public static string DefaultBackground(FlInstall i)
    {
        var art = Artwork(i);
        string[] prefer = i.Major switch
        {
            <= 24 => new[] { "Bot", "dedzii", "Sytrus" },
            25 => new[] { "dedzii", "Bot", "Sytrus" },
            _ => new[] { "Sytrus", "Bot", "dedzii" },
        };
        foreach (var p in prefer)
            if (art.FirstOrDefault(a => a.Name.Equals(p, StringComparison.OrdinalIgnoreCase)) is { Url: not null } hit) return hit.Url;
        return art.FirstOrDefault().Url ?? "";
    }

    public static List<string> RecentProjects(FlInstall i)
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"{RegRoot}\{i.RegistryKey}\MRU");
        if (k == null) return new();
        return k.GetValueNames()
            .Where(n => int.TryParse(n, out _)).OrderBy(n => int.Parse(n))
            .Select(n => k.GetValue(n) as string ?? "")
            .Where(p => p.Length > 0)
            .ToList();
    }

    public static string? GetGeneral(FlInstall i, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"{RegRoot}\{i.RegistryKey}\General");
        return k?.GetValue(name)?.ToString();
    }

    public static List<Process> Running(FlInstall i) =>
        Process.GetProcesses().Where(p =>
        {
            try { return string.Equals(p.MainModule?.FileName, i.Exe, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();

    static readonly Dictionary<string, string> iconCache = new();

    public static string IconDataUrl(string exe)
    {
        if (iconCache.TryGetValue(exe, out var cached)) return cached;
        string url = "";
        try
        {
            using var icon = Icon.ExtractIcon(exe, 0, 256) ?? Icon.ExtractAssociatedIcon(exe);
            if (icon != null)
            {
                using var bmp = icon.ToBitmap();
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                url = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }
        catch { }
        return iconCache[exe] = url;
    }
}
