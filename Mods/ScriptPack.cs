using System.Reflection;
using System.Text;
using FLPTools.Flp;

namespace FLPTools.Mods;

/// <summary>The bundled piano roll and Edison scripts, and installing them into FL's user Settings folders.</summary>
public static class ScriptPack
{
    public const string Marker = "# FLPTools mod pack";

    public sealed record Script(string Kind, string Name, string Description, string Source)
    {
        public string TargetDir => Kind == "pianoroll" ? FlPaths.PianoRollScriptsDir : FlPaths.AudioScriptsDir;
        public string TargetPath => Path.Combine(TargetDir, Name + ".pyscript");
        public bool Installed => File.Exists(TargetPath);
    }

    static List<Script>? cache;

    public static List<Script> All
    {
        get
        {
            if (cache != null) return cache;
            var asm = Assembly.GetExecutingAssembly();
            cache = new();
            foreach (var res in asm.GetManifestResourceNames().Where(r => r.EndsWith(".pyscript")).OrderBy(r => r))
            {
                using var s = asm.GetManifestResourceStream(res)!;
                var text = new StreamReader(s, Encoding.UTF8).ReadToEnd();
                var kind = res.Split('/')[0];
                var name = Path.GetFileNameWithoutExtension(res.Split('/')[1]);
                cache.Add(new Script(kind, name, Describe(text), text));
            }
            return cache;
        }
    }

    static string Describe(string src)
    {
        int q = src.IndexOf("\"\"\"", StringComparison.Ordinal);
        if (q >= 0)
        {
            int e = src.IndexOf("\"\"\"", q + 3, StringComparison.Ordinal);
            if (e > q) return src[(q + 3)..e].Trim();
        }
        // Edison scripts: use the ScriptDialog description
        int d = src.IndexOf("ScriptDialog(", StringComparison.Ordinal);
        if (d >= 0)
        {
            var parts = src[d..].Split('\'');
            if (parts.Length > 3) return parts[3];
        }
        return "";
    }

    public static void Install(Script s)
    {
        Directory.CreateDirectory(s.TargetDir);
        File.WriteAllText(s.TargetPath, s.Source, new UTF8Encoding(false));
    }

    /// <summary>Removes a script only if it is ours (starts with the pack marker), never a user's own file.</summary>
    public static bool Uninstall(Script s)
    {
        if (!File.Exists(s.TargetPath)) return false;
        using (var r = new StreamReader(s.TargetPath))
            if (r.ReadLine()?.TrimStart('﻿') != Marker) return false;
        File.Delete(s.TargetPath);
        return true;
    }

    /// <summary>Copies a project into FL's user Templates folder so it shows under File > New from template.</summary>
    public static string InstallTemplate(string flp, string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var dir = Path.Combine(FlPaths.TemplatesDir, name);
        Directory.CreateDirectory(dir);
        var dst = Path.Combine(dir, name + ".flp");
        File.Copy(flp, dst, overwrite: true);
        return dst;
    }
}
