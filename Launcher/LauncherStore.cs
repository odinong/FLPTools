using System.Text.Json;
using System.Text.Json.Serialization;

namespace FLPTools.Launcher;

/// <summary>One launch configuration (a tile on the home screen).</summary>
public sealed class LaunchConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "Default";
    public string Install { get; set; } = "";     // FlInstall.Id
    public string Accent { get; set; } = "";      // #rrggbb, empty = per-version default
    public string Image { get; set; } = "";       // custom tile art
    public string Background { get; set; } = "";  // home screen backdrop: a file path or an FL artwork URL
    public string Tag { get; set; } = "";         // short label the user picks, shown on the tile
    public string Splash { get; set; } = "";      // FLPTools startup animation style, empty = leave FL's own
    public string Project { get; set; } = "";     // project opened on launch
    public string Template { get; set; } = "";    // FL startup template (registry General\Template)
    public string Theme { get; set; } = "";       // .flstheme copied to __current before launch
    public string Wallpaper { get; set; } = "";   // background picture (registry BackPicFileName)
    public bool Fullscreen { get; set; }
    public string Priority { get; set; } = "Normal";
    public int Cores { get; set; }                // 0 = all
    public bool CloseOthers { get; set; }
    public bool AutoConvert { get; set; } = true;
    public bool SyncMods { get; set; }
    public bool RestoreAfter { get; set; } = true;
    public string Args { get; set; } = "";
    public long PlaySeconds { get; set; }
    public DateTime? LastPlayed { get; set; }
    public int Order { get; set; }
}

public sealed class LauncherSettings
{
    public bool Sounds { get; set; } = true;
    public bool Animations { get; set; } = true;
    public bool MinimizeOnLaunch { get; set; } = true;
    public bool FlWindowAnim { get; set; }                 // fade FL's windows in as they open
    public int FlWindowAnimMs { get; set; } = 220;
    public bool FlWindowAnimRise { get; set; } = true;
    public bool FlWindowAnimInside { get; set; }
    public string Theme { get; set; } = "dark";
}

/// <summary>A registry value or file that a launch changed and must be put back when FL exits.</summary>
public sealed class PendingRestore
{
    public string Install { get; set; } = "";
    public List<RegBackup> Registry { get; set; } = new();
    public string? ThemeBackup { get; set; }
    public string? ThemeTarget { get; set; }
}

public sealed class RegBackup
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Value { get; set; }   // null = value did not exist
}

public sealed class LauncherState
{
    public LauncherSettings Settings { get; set; } = new();
    public List<LaunchConfig> Configs { get; set; } = new();
    public List<PendingRestore> Pending { get; set; } = new();
}

public static class LauncherStore
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FLPTools");
    static string FilePath => Path.Combine(Dir, "launcher.json");

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static LauncherState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LauncherState>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch { File.Copy(FilePath, FilePath + ".broken", true); }
        return new();
    }

    public static void Save(LauncherState s)
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
        File.Move(tmp, FilePath, true);
    }

    /// <summary>Make sure every install has at least one config (configs for removed installs are kept).</summary>
    public static void Reconcile(LauncherState s, List<FlInstall> installs)
    {
        foreach (var i in installs)
            if (!s.Configs.Any(c => c.Install == i.Id))
                s.Configs.Add(new LaunchConfig { Name = "Default", Install = i.Id, Order = installs.IndexOf(i) * 100 });
    }
}
