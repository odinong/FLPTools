using System.Buffers.Binary;
using System.Drawing;
using System.Text;

namespace FLPTools.Flp;

public sealed class FlpChannel
{
    public int Index;
    public int Type;               // 0 sampler, 2 plugin, 4 audio clip, 5 automation, ...
    public FlpEvent? NameEvent;    // 203
    public FlpEvent? PluginEvent;  // 201 (default plugin name, empty for samplers)
    public FlpEvent? ColorEvent;   // 128
    public FlpEvent? SampleEvent;  // 196

    public string TypeName => Type switch
    {
        0 => "Sampler", 2 => "Plugin", 3 => "Layer", 4 => "Audio clip", 5 => "Automation", _ => $"Type {Type}",
    };
}

public sealed class FlpPattern
{
    public int Index;
    public FlpEvent? NotesEvent;   // 224
    public FlpEvent? NameEvent;    // 193
    public FlpEvent? ColorEvent;   // 150
    public FlpEvent? MetaHeader;   // the 65 event that starts the pattern's name/color block
    public int NoteCount => NotesEvent == null ? 0 : NotesEvent.Data.Length / FlpNotes.Size;
}

public sealed class FlpInsert
{
    public int Index;
    public FlpEvent Begin = null!;   // 42
    public FlpEvent Flags = null!;   // 236
    public FlpEvent? NameEvent;      // 204
    public FlpEvent? ColorEvent;     // 149
    public List<string> Plugins = new();
}

public sealed class FlpClip
{
    public int Position, Length, Track, ItemIndex;
    public float StartOffset, EndOffset;
    public bool IsPattern => ItemIndex > 0x5000;
    public int PatternIndex => ItemIndex - 0x5000;
}

/// <summary>Readable/editable view over an FlpProject's event list. Edits change the events in place.</summary>
public sealed class FlpModel
{
    public readonly FlpProject Project;
    public List<FlpChannel> Channels = new();
    public List<FlpPattern> Patterns = new();
    public List<FlpInsert> Inserts = new();

    List<FlpEvent> Ev => Project.Events;

    public FlpModel(FlpProject project)
    {
        Project = project;
        Rebuild();
    }

    public void Rebuild()
    {
        Channels.Clear();
        Patterns.Clear();
        Inserts.Clear();
        var ev = Ev;
        int mixerStart = ev.FindIndex(e => e.Id == FlpEventNames.InsertPrefix);
        if (mixerStart < 0) mixerStart = ev.Count;

        FlpChannel? ch = null;
        FlpPattern? pat = null;
        for (int i = 0; i < mixerStart; i++)
        {
            var e = ev[i];
            switch (e.Id)
            {
                case 64:
                    pat = null;
                    ch = new FlpChannel { Index = (int)e.Value };
                    Channels.Add(ch);
                    break;
                case 65:
                    ch = null;
                    int idx = (int)e.Value;
                    pat = Patterns.FirstOrDefault(p => p.Index == idx);
                    if (pat == null) Patterns.Add(pat = new FlpPattern { Index = idx });
                    if (i + 1 < ev.Count && ev[i + 1].Id is 193 or 150) pat.MetaHeader = e;
                    break;
                case 99: case 233: case 238:
                    ch = null; pat = null;
                    break;
                case 21 when ch != null: ch.Type = (int)e.Value; break;
                case 203 when ch != null: ch.NameEvent ??= e; break;
                case 201 when ch != null: ch.PluginEvent ??= e; break;
                case 128 when ch != null: ch.ColorEvent ??= e; break;
                case 196 when ch != null: ch.SampleEvent ??= e; break;
                case 224 when pat != null: pat.NotesEvent ??= e; break;
                case 193 when pat != null: pat.NameEvent ??= e; break;
                case 150 when pat != null: pat.ColorEvent ??= e; break;
            }
        }
        Patterns.Sort((a, b) => a.Index.CompareTo(b.Index));

        // Mixer: each insert is [149 color] 42 [95] [204 name] 236 ... 147
        FlpInsert? ins = null;
        for (int i = mixerStart; i < ev.Count; i++)
        {
            var e = ev[i];
            if (e.Id == FlpEventNames.InsertPrefix)
            {
                ins = new FlpInsert { Index = Inserts.Count, Begin = e };
                if (i > 0 && ev[i - 1].Id == 149) ins.ColorEvent = ev[i - 1];
                Inserts.Add(ins);
            }
            else if (ins == null) continue;
            else if (e.Id == FlpEventNames.InsertFlags) ins.Flags = e;
            else if (e.Id == 204) ins.NameEvent ??= e;
            else if (e.Id == 203) ins.Plugins.Add(Text(e));
            else if (e.Id == 147) ins = null;
        }
    }

    // ------------------------------------------------------------ text/colour helpers

    public static string Text(FlpEvent? e) =>
        e == null || e.Data.Length < 2 ? "" : Encoding.Unicode.GetString(e.Data).TrimEnd('\0');

    public static byte[] TextBytes(string s) => Encoding.Unicode.GetBytes(s + "\0");

    /// <summary>FLP colours are stored as Delphi TColor (0x00BBGGRR).</summary>
    public static Color ToColor(uint v) => Color.FromArgb((int)(v & 0xFF), (int)((v >> 8) & 0xFF), (int)((v >> 16) & 0xFF));

    public static uint FromColor(Color c) => (uint)(c.R | (c.G << 8) | (c.B << 16));

    FlpEvent? First(byte id) => Ev.FirstOrDefault(e => e.Id == id);

    int HeaderInsertPoint()
    {
        int i = Ev.FindIndex(e => e.Id is 237 or 231 or 216 or 65 or 64);
        return i < 0 ? Ev.Count : i;
    }

    string GetText(byte id) => Text(First(id));

    void SetText(byte id, string value)
    {
        var e = First(id);
        if (e == null)
        {
            if (value.Length == 0) return;
            Ev.Insert(HeaderInsertPoint(), new FlpEvent(id, TextBytes(value)));
        }
        else e.Data = TextBytes(value);
    }

    // ------------------------------------------------------------ project info

    public string Title { get => GetText(194); set => SetText(194, value); }
    public string Artists { get => GetText(207); set => SetText(207, value); }
    public string Genre { get => GetText(206); set => SetText(206, value); }
    public string Comments { get => GetText(195); set => SetText(195, value); }
    public string Url { get => GetText(197); set => SetText(197, value); }

    public double Tempo
    {
        get => First(156) is { } e ? e.Value / 1000.0 : 140;
        set
        {
            uint v = (uint)Math.Round(Math.Clamp(value, 10, 999) * 1000);
            if (First(156) is { } e) e.Data = BitConverter.GetBytes(v);
            else Ev.Insert(HeaderInsertPoint(), FlpEvent.FromValue(156, v));
        }
    }

    public int TimeSigNum
    {
        get => First(17) is { } e ? (int)e.Value : 4;
        set { if (First(17) is { } e) e.Data = new[] { (byte)Math.Clamp(value, 1, 16) }; }
    }

    public int TimeSigBeat
    {
        get => First(18) is { } e ? (int)e.Value : 4;
        set { if (First(18) is { } e) e.Data = new[] { (byte)Math.Clamp(value, 1, 16) }; }
    }

    public int Ppq => Project.Ppq;

    // ------------------------------------------------------------ names & colours

    public void SetChannelName(FlpChannel c, string name)
    {
        if (c.NameEvent != null) { c.NameEvent.Data = TextBytes(name); return; }
        // samplers without a display name: add one after the plugin info (212) like FL does
        int start = Ev.FindIndex(e => e.Id == 64 && e.Value == (uint)c.Index);
        int at = Ev.FindIndex(start + 1, e => e.Id == 212 || e.Id == 64);
        if (start < 0 || at < 0 || Ev[at].Id != 212) throw new InvalidOperationException($"Channel {c.Index} has no name slot.");
        c.NameEvent = new FlpEvent(203, TextBytes(name));
        Ev.Insert(at + 1, c.NameEvent);
    }

    public void SetChannelColor(FlpChannel c, Color color)
    {
        if (c.ColorEvent == null) throw new InvalidOperationException($"Channel {c.Index} has no colour slot.");
        c.ColorEvent.Data = BitConverter.GetBytes(FromColor(color));
    }

    public void SetPatternName(FlpPattern p, string name)
    {
        if (p.NameEvent != null) { p.NameEvent.Data = TextBytes(name); return; }
        p.NameEvent = new FlpEvent(193, TextBytes(name));
        InsertPatternMeta(p, p.NameEvent, first: true);
    }

    public void SetPatternColor(FlpPattern p, Color color)
    {
        var data = BitConverter.GetBytes(FromColor(color));
        if (p.ColorEvent != null) { p.ColorEvent.Data = data; return; }
        p.ColorEvent = new FlpEvent(150, data);
        InsertPatternMeta(p, p.ColorEvent, first: false);
    }

    void InsertPatternMeta(FlpPattern p, FlpEvent e, bool first)
    {
        if (p.MetaHeader == null)
        {
            // no name/colour block yet: start one right before the first channel
            int at = Ev.FindIndex(x => x.Id == 64);
            if (at < 0) throw new InvalidOperationException("Project has no channels to anchor pattern info.");
            p.MetaHeader = FlpEvent.FromValue(65, (uint)p.Index);
            Ev.InsertRange(at, new[] { p.MetaHeader, e });
            return;
        }
        int h = Ev.IndexOf(p.MetaHeader);
        int pos = h + 1;
        if (!first && p.NameEvent != null) pos = Ev.IndexOf(p.NameEvent) + 1;
        Ev.Insert(pos, e);
    }

    public void SetInsertName(FlpInsert ins, string name)
    {
        if (ins.NameEvent != null) { ins.NameEvent.Data = TextBytes(name); return; }
        ins.NameEvent = new FlpEvent(204, TextBytes(name));
        Ev.Insert(Ev.IndexOf(ins.Flags), ins.NameEvent);
    }

    public void SetInsertColor(FlpInsert ins, Color color)
    {
        var data = BitConverter.GetBytes(FromColor(color));
        if (ins.ColorEvent != null) { ins.ColorEvent.Data = data; return; }
        ins.ColorEvent = new FlpEvent(149, data);
        Ev.Insert(Ev.IndexOf(ins.Begin), ins.ColorEvent);
        ins.Begin.Data = new byte[] { 1 };   // FL marks inserts that carry a colour with 1
    }

    // ------------------------------------------------------------ samples

    public static string ExpandFlPath(string path)
    {
        string factory = FlPaths.InstallDir ?? "";
        string user = FlPaths.UserDataDir;
        return path.Replace("%FLStudioFactoryData%", factory, StringComparison.OrdinalIgnoreCase)
                   .Replace("%FLStudioUserData%", user, StringComparison.OrdinalIgnoreCase);
    }

    public IEnumerable<FlpChannel> SampleChannels => Channels.Where(c => c.SampleEvent != null && Text(c.SampleEvent).Length > 0);

    public static void SetSample(FlpChannel c, string path) => c.SampleEvent!.Data = TextBytes(path);

    /// <summary>Point missing samples at files with the same name found under <paramref name="folder"/>.</summary>
    public List<string> RelinkMissing(string folder)
    {
        var log = new List<string>();
        var index = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var c in SampleChannels)
        {
            var path = Text(c.SampleEvent);
            if (File.Exists(ExpandFlPath(path))) continue;
            if (index.TryGetValue(Path.GetFileName(path), out var found))
            {
                SetSample(c, found);
                log.Add($"relinked ch {c.Index}: {Path.GetFileName(path)} -> {found}");
            }
            else log.Add($"still missing ch {c.Index}: {path}");
        }
        return log;
    }

    /// <summary>Copy every existing sample into <paramref name="folder"/> and point the project at the copies.</summary>
    public List<string> CollectSamples(string folder)
    {
        var log = new List<string>();
        Directory.CreateDirectory(folder);
        var done = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in SampleChannels)
        {
            var src = ExpandFlPath(Text(c.SampleEvent));
            if (!File.Exists(src)) { log.Add($"missing, skipped ch {c.Index}: {src}"); continue; }
            if (!done.TryGetValue(src, out var dst))
            {
                dst = Path.Combine(folder, Path.GetFileName(src));
                for (int n = 2; File.Exists(dst) && !SameFile(src, dst); n++)
                    dst = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(src)} ({n}){Path.GetExtension(src)}");
                if (!File.Exists(dst)) File.Copy(src, dst);
                done[src] = dst;
                log.Add($"copied {Path.GetFileName(src)}");
            }
            SetSample(c, dst);
        }
        return log;
    }

    static bool SameFile(string a, string b) =>
        Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase)
        || (new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b)));

    public int ReplaceInSamplePaths(string find, string replace)
    {
        int n = 0;
        foreach (var c in SampleChannels)
        {
            var p = Text(c.SampleEvent);
            var q = p.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
            if (q != p) { SetSample(c, q); n++; }
        }
        return n;
    }

    // ------------------------------------------------------------ arrangement

    public List<FlpClip> Clips(int arrangement = 0)
    {
        var list = new List<FlpClip>();
        var items = Ev.Where(e => e.Id == FlpEventNames.PlaylistItems).ElementAtOrDefault(arrangement);
        if (items == null || items.Data.Length == 0) return list;
        int size = FlpNotes.ClipSize(items.Data, Project.VersionMajor);
        if (size == 0) return list;
        for (int j = 0; j < items.Data.Length; j += size)
        {
            var r = items.Data.AsSpan(j);
            list.Add(new FlpClip
            {
                Position = (int)BinaryPrimitives.ReadUInt32LittleEndian(r),
                ItemIndex = BinaryPrimitives.ReadUInt16LittleEndian(r[6..]),
                Length = (int)BinaryPrimitives.ReadUInt32LittleEndian(r[8..]),
                Track = 499 - BinaryPrimitives.ReadUInt16LittleEndian(r[12..]),
                StartOffset = BinaryPrimitives.ReadSingleLittleEndian(r[24..]),
                EndOffset = BinaryPrimitives.ReadSingleLittleEndian(r[28..]),
            });
        }
        return list;
    }

    public int SongLengthTicks => Clips().Select(c => c.Position + c.Length).DefaultIfEmpty(0).Max();

    // ------------------------------------------------------------ report

    public string Report(string fileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Project: {fileName}");
        sb.AppendLine($"FL version: {Project.VersionString}   tempo: {Tempo:0.###} BPM   time sig: {TimeSigNum}/{TimeSigBeat}   PPQ: {Ppq}");
        if (Title.Length > 0) sb.AppendLine($"Title: {Title}");
        if (Artists.Length > 0) sb.AppendLine($"Artists: {Artists}");
        if (Genre.Length > 0) sb.AppendLine($"Genre: {Genre}");
        int len = SongLengthTicks;
        if (len > 0 && Ppq > 0)
        {
            double beats = (double)len / Ppq;
            var t = TimeSpan.FromSeconds(beats * 60 / Tempo);
            sb.AppendLine($"Song length: {beats / Math.Max(1, TimeSigNum):0.#} bars, {t:m\\:ss} at {Tempo:0.##} BPM, {Clips().Count} playlist clips");
        }
        sb.AppendLine();
        sb.AppendLine($"Channels ({Channels.Count}):");
        foreach (var c in Channels)
        {
            string plugin = Text(c.PluginEvent);
            sb.AppendLine($"  {c.Index,3}  {Text(c.NameEvent),-40} {c.TypeName}{(plugin.Length > 0 ? " / " + plugin : "")}");
        }
        sb.AppendLine();
        sb.AppendLine($"Patterns ({Patterns.Count}):");
        foreach (var p in Patterns) sb.AppendLine($"  {p.Index,3}  {Text(p.NameEvent),-40} {p.NoteCount} notes");
        sb.AppendLine();
        var used = Inserts.Where(i => i.NameEvent != null || i.Plugins.Count > 0).ToList();
        sb.AppendLine($"Mixer inserts in use ({used.Count} of {Inserts.Count}):");
        foreach (var i in used)
            sb.AppendLine($"  {(i.Index == Inserts.Count - 1 ? "cur" : i.Index == 0 ? "M" : i.Index.ToString()),3}  {Text(i.NameEvent),-30} {string.Join(", ", i.Plugins)}");
        sb.AppendLine();
        var plugins = Channels.Select(c => Text(c.PluginEvent)).Concat(Inserts.SelectMany(i => i.Plugins))
            .Where(s => s.Length > 0).GroupBy(s => s).OrderByDescending(g => g.Count());
        sb.AppendLine("Plugins used:");
        foreach (var g in plugins) sb.AppendLine($"  {g.Key} x{g.Count()}");
        sb.AppendLine();
        sb.AppendLine("Samples:");
        foreach (var c in SampleChannels)
        {
            var path = Text(c.SampleEvent);
            sb.AppendLine($"  {(File.Exists(ExpandFlPath(path)) ? "ok     " : "MISSING")}  {path}");
        }
        return sb.ToString();
    }
}
