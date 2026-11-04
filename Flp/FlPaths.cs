namespace FLPTools.Flp;

/// <summary>Where FL Studio keeps its install and user data on this machine.</summary>
public static class FlPaths
{
    public static string UserDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Image-Line", "FL Studio");

    public static string SettingsDir => Path.Combine(UserDataDir, "Settings");
    public static string PianoRollScriptsDir => Path.Combine(SettingsDir, "Piano roll scripts");
    public static string AudioScriptsDir => Path.Combine(SettingsDir, "Audio scripts");
    public static string ThemesDir => Path.Combine(SettingsDir, "Themes");
    public static string NoteColorPresetsDir => Path.Combine(SettingsDir, "Note color presets");
    public static string TemplatesDir => Path.Combine(UserDataDir, "Projects", "Templates");

    /// <summary>All FL Studio installs (newest first) that have FL64.exe.</summary>
    public static List<string> Installs
    {
        get
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            };
            return roots.Select(r => Path.Combine(r, "Image-Line"))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.GetDirectories(d, "FL Studio*"))
                .Where(d => File.Exists(Path.Combine(d, "FL64.exe")) || File.Exists(Path.Combine(d, "FL.exe")))
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public static string? InstallDir => Installs.FirstOrDefault();

    public static IEnumerable<string> BuiltInThemeDirs => Installs.Select(d => Path.Combine(d, "Artwork", "Themes")).Where(Directory.Exists);
}
