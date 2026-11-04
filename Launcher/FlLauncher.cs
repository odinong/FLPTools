using System.Diagnostics;
using Microsoft.Win32;
using FLPTools.Flp;
using FLPTools.Mods;

namespace FLPTools.Launcher;

/// <summary>Starts FL Studio with a launch config applied, and undoes the config's setting changes afterwards.</summary>
public sealed class FlLauncher
{
    readonly LauncherState state;
    readonly Action save;

    public FlLauncher(LauncherState state, Action save)
    {
        this.state = state;
        this.save = save;
    }

    public sealed record Result(Process Process, string? OpenedProject, List<string> Notes);

    public Result Launch(LaunchConfig cfg, FlInstall install, string? projectOverride)
    {
        var notes = new List<string>();
        var running = FlInstalls.Running(install);
        if (cfg.CloseOthers)
        {
            foreach (var p in Process.GetProcessesByName("FL64").Concat(Process.GetProcessesByName("FL")))
            {
                try
                {
                    if (p.CloseMainWindow() && !p.WaitForExit(15000)) notes.Add("Another FL did not close (unsaved changes?), launching anyway.");
                }
                catch { }
            }
            running = FlInstalls.Running(install);
        }

        string? project = string.IsNullOrWhiteSpace(projectOverride) ? (cfg.Project.Length > 0 ? cfg.Project : null) : projectOverride;
        if (project != null && !File.Exists(project)) { notes.Add($"Project not found: {project}"); project = null; }
        if (project != null && cfg.AutoConvert) project = MaybeConvert(project, install, notes);

        if (cfg.SyncMods)
        {
            foreach (var s in ScriptPack.All) ScriptPack.Install(s);
            notes.Add($"Synced {ScriptPack.All.Count} mod scripts.");
        }

        // Settings only apply on a fresh start; a running FL keeps its own.
        if (running.Count == 0) ApplySettings(cfg, install, notes);
        else notes.Add($"{install.Id} is already running, so only the project is passed to it.");

        var psi = new ProcessStartInfo(install.Exe) { UseShellExecute = false, WorkingDirectory = install.Dir };
        if (project != null) psi.ArgumentList.Add(project);
        foreach (var a in SplitArgs(cfg.Args)) psi.ArgumentList.Add(a);
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("FL Studio did not start.");

        try
        {
            proc.PriorityClass = cfg.Priority switch
            {
                "AboveNormal" => ProcessPriorityClass.AboveNormal,
                "High" => ProcessPriorityClass.High,
                "BelowNormal" => ProcessPriorityClass.BelowNormal,
                _ => ProcessPriorityClass.Normal,
            };
            if (cfg.Cores > 0 && cfg.Cores < Environment.ProcessorCount)
                proc.ProcessorAffinity = (IntPtr)((1L << cfg.Cores) - 1);
        }
        catch (Exception ex) { notes.Add("Could not set priority/cores: " + ex.Message); }

        cfg.LastPlayed = DateTime.Now;
        save();
        return new Result(proc, project, notes);
    }

    static IEnumerable<string> SplitArgs(string s)
    {
        var cur = new System.Text.StringBuilder();
        bool q = false;
        foreach (var ch in s)
        {
            if (ch == '"') { q = !q; continue; }
            if (char.IsWhiteSpace(ch) && !q) { if (cur.Length > 0) { yield return cur.ToString(); cur.Clear(); } continue; }
            cur.Append(ch);
        }
        if (cur.Length > 0) yield return cur.ToString();
    }

    /// <summary>FL 25 cannot open FL 26 projects; make (or reuse) a converted copy next to the original.</summary>
    static string MaybeConvert(string project, FlInstall install, List<string> notes)
    {
        try
        {
            var p = FlpProject.Load(project);
            if (p.VersionMajor <= install.Major) return project;
            if (p.IsLocked) { notes.Add("This project is locked by an unregistered FL save, so it can't be converted."); return project; }
            if (!(p.VersionMajor == 26 && install.Major == 25))
            {
                notes.Add($"Project was saved in FL {p.VersionMajor}; FL {install.Major} may not open it (only 26 to 25 conversion is supported).");
                return project;
            }
            var dst = Path.Combine(Path.GetDirectoryName(project)!, Path.GetFileNameWithoutExtension(project) + " (FL25).flp");
            if (!File.Exists(dst) || File.GetLastWriteTimeUtc(dst) < File.GetLastWriteTimeUtc(project))
            {
                Fl25Converter.Convert25(p, install.Version);
                p.Save(dst);
                notes.Add($"Converted FL {p.VersionMajor} project for FL {install.Major}: {Path.GetFileName(dst)}");
            }
            else notes.Add($"Using converted copy {Path.GetFileName(dst)}");
            return dst;
        }
        catch (Exception ex)
        {
            notes.Add("Could not check project version: " + ex.Message);
            return project;
        }
    }

    void ApplySettings(LaunchConfig cfg, FlInstall install, List<string> notes)
    {
        var pending = new PendingRestore { Install = install.Id };
        string key = $@"{FlInstalls.RegRoot}\{install.RegistryKey}\General";

        void SetReg(string name, string value)
        {
            using var k = Registry.CurrentUser.CreateSubKey(key);
            pending.Registry.Add(new RegBackup { Key = key, Name = name, Value = k.GetValue(name)?.ToString() });
            k.SetValue(name, value, RegistryValueKind.String);
        }

        if (cfg.Template.Length > 0) SetReg("Template", cfg.Template);
        SetReg("Fullscreen", cfg.Fullscreen ? "1" : "0");
        if (cfg.Wallpaper.Length > 0)
        {
            SetReg("BackPicFileName", cfg.Wallpaper);
            SetReg("BackMode", "2");
        }
        if (cfg.Splash.Length > 0)
        {
            FlMods.EnsureSplash(cfg.Splash, install.Exe);
            SetReg("SplashImage", FlMods.SplashRegistryValue(cfg.Splash));
        }
        bool themeChange = cfg.Theme.Length > 0 && File.Exists(cfg.Theme);
        if (themeChange || cfg.Splash.Length > 0)
        {
            var target = Path.Combine(FlPaths.ThemesDir, CurrentThemeFile(install));
            Directory.CreateDirectory(FlPaths.ThemesDir);
            if (File.Exists(target))
            {
                var backup = Path.Combine(LauncherStore.Dir, $"theme-backup-{install.Major}.flstheme");
                Directory.CreateDirectory(LauncherStore.Dir);
                File.Copy(target, backup, true);
                pending.ThemeBackup = backup;
            }
            pending.ThemeTarget = target;
            var theme = themeChange ? FlTheme.Load(cfg.Theme) : File.Exists(target) ? FlTheme.Load(target) : FlTheme.Default();
            if (cfg.Wallpaper.Length > 0) { theme["BackMode"] = "2"; theme["BackPicFilename"] = cfg.Wallpaper; }
            if (cfg.Splash.Length > 0) theme["SplashAnim"] = $"{FlMods.Frames}-{Path.GetFileName(FlMods.SplashFile(cfg.Splash))}";
            theme.Save(target);
            if (themeChange) notes.Add($"Theme: {Path.GetFileNameWithoutExtension(cfg.Theme)}");
        }

        if (cfg.RestoreAfter && (pending.Registry.Count > 0 || pending.ThemeTarget != null))
        {
            state.Pending.RemoveAll(p => p.Install == install.Id);
            state.Pending.Add(pending);
            save();
        }
    }

    static string CurrentThemeFile(FlInstall install)
    {
        using var k = Registry.CurrentUser.OpenSubKey($@"{FlInstalls.RegRoot}\{install.RegistryKey}\General\Theme");
        return k?.GetValue("ThemeFilename") as string ?? "__current.flstheme";
    }

    /// <summary>Undo the setting changes of every launch whose FL is no longer running.</summary>
    public List<string> RestoreFinished(List<FlInstall> installs)
    {
        var done = new List<string>();
        foreach (var p in state.Pending.ToList())
        {
            var inst = installs.FirstOrDefault(i => i.Id == p.Install);
            if (inst != null && FlInstalls.Running(inst).Count > 0) continue;
            foreach (var r in p.Registry.AsEnumerable().Reverse())
            {
                using var k = Registry.CurrentUser.CreateSubKey(r.Key);
                if (r.Value == null) k.DeleteValue(r.Name, false);
                else k.SetValue(r.Name, r.Value, RegistryValueKind.String);
            }
            if (p.ThemeTarget != null)
            {
                if (p.ThemeBackup != null && File.Exists(p.ThemeBackup)) File.Copy(p.ThemeBackup, p.ThemeTarget, true);
            }
            state.Pending.Remove(p);
            done.Add(p.Install);
        }
        if (done.Count > 0) save();
        return done;
    }
}
