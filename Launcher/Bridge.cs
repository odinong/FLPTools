using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using FLPTools.Flp;
using FLPTools.Mods;

namespace FLPTools.Launcher;

/// <summary>
/// Handles requests from the web UI. Each request is {id, cmd, args}; the reply is {id, ok, result|error}.
/// Pushed notifications are {event, ...}.
/// </summary>
public sealed class Bridge
{
    readonly LauncherForm form;
    readonly LauncherState state;
    readonly FlLauncher launcher;
    List<FlInstall> installs;
    readonly Dictionary<string, Process> live = new();

    // Project editor session
    FlpProject? project;
    FlpModel? model;
    string? projectPath;

    public Bridge(LauncherForm form)
    {
        this.form = form;
        state = LauncherStore.Load();
        installs = FlInstalls.Probe();
        LauncherStore.Reconcile(state, installs);
        launcher = new FlLauncher(state, Save);
        launcher.RestoreFinished(installs);
        Save();
        form.ApplyWindowAnimations(state.Settings);
    }

    void Save() => LauncherStore.Save(state);

    public IReadOnlyList<FlInstall> Installs => installs;

    static readonly HashSet<string> UiThreadCommands = new()
    {
        "dialog.open", "dialog.folder", "dialog.save", "app.fullscreen", "app.minimize", "app.maximize", "app.drag", "app.close",
    };

    public async Task<string> Handle(string json)
    {
        JsonNode? id = null;
        try
        {
            var req = JsonNode.Parse(json)!.AsObject();
            id = req["id"]?.DeepClone();
            string cmd = req["cmd"]!.GetValue<string>();
            var args = req["args"] as JsonObject ?? new JsonObject();
            JsonNode? result = UiThreadCommands.Contains(cmd) ? Run(cmd, args) : await Task.Run(() => Run(cmd, args));
            return new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result }.ToJsonString();
        }
        catch (Exception ex)
        {
            var msg = ex is AggregateException ae ? ae.InnerException?.Message ?? ex.Message : ex.Message;
            return new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = msg }.ToJsonString();
        }
    }

    static string S(JsonObject a, string k, string def = "") => a[k]?.GetValue<string>() ?? def;
    static int I(JsonObject a, string k, int def = 0) => a[k] is JsonValue v && v.TryGetValue<double>(out var d) ? (int)d : def;
    static double D(JsonObject a, string k, double def = 0) => a[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : def;
    static bool B(JsonObject a, string k) => a[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    static JsonNode? ToNode<T>(T o) => JsonSerializer.SerializeToNode(o, LauncherStore.Json);

    JsonNode? Run(string cmd, JsonObject a)
    {
        switch (cmd)
        {
            // ---------------------------------------------------------- launcher
            case "init": return Init();
            case "probe":
                installs = FlInstalls.Probe();
                LauncherStore.Reconcile(state, installs);
                Save();
                return Init();
            case "config.save":
            {
                var cfg = a["config"].Deserialize<LaunchConfig>(LauncherStore.Json)!;
                int i = state.Configs.FindIndex(c => c.Id == cfg.Id);
                if (i >= 0)
                {
                    cfg.PlaySeconds = state.Configs[i].PlaySeconds;
                    cfg.LastPlayed = state.Configs[i].LastPlayed;
                    state.Configs[i] = cfg;
                }
                else
                {
                    cfg.Order = state.Configs.Count == 0 ? 0 : state.Configs.Max(c => c.Order) + 1;
                    state.Configs.Add(cfg);
                }
                Save();
                return ToNode(cfg);
            }
            case "config.delete":
            {
                var id = S(a, "id");
                var cfg = state.Configs.First(c => c.Id == id);
                if (state.Configs.Count(c => c.Install == cfg.Install) <= 1) throw new InvalidOperationException("Each FL version keeps at least one config.");
                state.Configs.Remove(cfg);
                Save();
                return true;
            }
            case "config.reorder":
            {
                var ids = a["ids"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
                foreach (var c in state.Configs) c.Order = ids.IndexOf(c.Id) is int k && k >= 0 ? k : 999;
                Save();
                return true;
            }
            case "settings.save":
                state.Settings = a["settings"].Deserialize<LauncherSettings>(LauncherStore.Json)!;
                Save();
                form.ApplyWindowAnimations(state.Settings);
                return true;
            case "winanim.try":
            {
                // Try the effect on another program (for checking it without FL): animates that process's windows for 30 s.
                var name = S(a, "process");
                form.ApplyWindowAnimations(state.Settings, name);
                _ = Task.Delay(30000).ContinueWith(_ => { form.RemoveAnimationTarget(name); form.ApplyWindowAnimations(state.Settings); });
                return true;
            }
            case "launch": return Launch(S(a, "id"), S(a, "project"));
            case "recent":
            {
                var inst = installs.FirstOrDefault(i => i.Id == S(a, "install"));
                return inst == null ? new JsonArray() : RecentJson(FlInstalls.RecentProjects(inst));
            }
            case "recent.all":
            {
                var all = new JsonArray();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var inst in installs.AsEnumerable().Reverse())
                    foreach (var p in FlInstalls.RecentProjects(inst))
                        if (seen.Add(p)) all.Add(RecentItem(p, inst.Id));
                return all;
            }
            case "image":
            {
                var path = S(a, "path");
                if (!File.Exists(path)) return "";
                var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                var mime = ext switch { "jpg" or "jpeg" => "image/jpeg", "gif" => "image/gif", "webp" => "image/webp", "bmp" => "image/bmp", _ => "image/png" };
                return $"data:{mime};base64," + Convert.ToBase64String(File.ReadAllBytes(path));
            }

            // ---------------------------------------------------------- window / dialogs
            case "dialog.open": return form.PickOpen(S(a, "filter", "All files|*.*"), B(a, "multi"));
            case "dialog.folder": return form.PickFolder();
            case "dialog.save": return form.PickSave(S(a, "filter", "All files|*.*"), S(a, "name"));
            case "app.fullscreen": form.ToggleFullscreen(); return true;
            case "app.minimize": form.WindowState = FormWindowState.Minimized; return true;
            case "app.maximize": form.ToggleMaximize(); return form.WindowState == FormWindowState.Maximized;
            case "app.drag": form.BeginDrag(); return true;
            case "app.close": form.Close(); return true;
            case "shell.run":
            {
                var file = S(a, "file");
                if (!file.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Not allowed.");
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                return true;
            }
            case "shell.reveal":
            {
                var p = S(a, "path");
                Process.Start("explorer.exe", File.Exists(p) ? $"/select,\"{p}\"" : $"\"{p}\"");
                return true;
            }

            // ---------------------------------------------------------- converter / JSON
            case "convert.fl25":
            {
                var p = FlpProject.Load(S(a, "src"));
                var from = p.VersionString;
                var log = Fl25Converter.Convert25(p, S(a, "version", Fl25Converter.DefaultVersion));
                p.Save(S(a, "dst"));
                return new JsonObject { ["from"] = from, ["log"] = new JsonArray(log.Select(l => (JsonNode)l).ToArray()) };
            }
            case "json.decompile":
            {
                var bytes = File.ReadAllBytes(S(a, "src"));
                var p = FlpProject.Parse(bytes);
                var json = FlpJson.Decompile(p);
                File.WriteAllText(S(a, "dst"), json, new System.Text.UTF8Encoding(false));
                bool same = FlpJson.Recompile(json).ToBytes().AsSpan().SequenceEqual(bytes);
                return new JsonObject { ["events"] = p.Events.Count, ["version"] = p.VersionString, ["identical"] = same };
            }
            case "json.recompile":
            {
                var p = FlpJson.Recompile(File.ReadAllText(S(a, "src")));
                var bytes = p.ToBytes();
                FlpProject.Parse(bytes);
                File.WriteAllBytes(S(a, "dst"), bytes);
                return new JsonObject { ["events"] = p.Events.Count, ["bytes"] = bytes.Length };
            }
            case "verify":
            {
                var arr = new JsonArray();
                foreach (var f in a["files"]!.AsArray().Select(n => n!.GetValue<string>()))
                {
                    try
                    {
                        var (ok, msg) = FlpJson.VerifyRoundTrip(File.ReadAllBytes(f));
                        arr.Add(new JsonObject { ["file"] = f, ["ok"] = ok, ["msg"] = msg });
                    }
                    catch (Exception ex) { arr.Add(new JsonObject { ["file"] = f, ["ok"] = false, ["msg"] = ex.Message }); }
                }
                return arr;
            }
            case "batch": return Batch(S(a, "folder"), S(a, "op"), S(a, "version", Fl25Converter.DefaultVersion));

            // ---------------------------------------------------------- project editor
            case "project.open":
                var loaded = FlpProject.Load(S(a, "path"));
                if (loaded.IsLocked) throw new InvalidOperationException(FlpProject.LockedMessage);
                projectPath = S(a, "path");
                project = loaded;
                model = new FlpModel(project);
                return ProjectJson();
            case "project.apply": ApplyEdits(a); return ProjectJson();
            case "project.notes":
            {
                var m = Need();
                var e = new FlpNotes.Edit
                {
                    Transpose = I(a, "transpose"),
                    VelocityScale = D(a, "velScale", 1),
                    VelocityAdd = I(a, "velAdd"),
                    QuantizeTicks = I(a, "quantize"),
                    QuantizeStrength = D(a, "strength", 1),
                    LengthScale = D(a, "lengthScale", 1),
                    OnlyChannel = a["channel"] is JsonValue cv && cv.TryGetValue<double>(out var ch) && ch >= 0 ? (int)ch : null,
                };
                var which = a["patterns"] is JsonArray pa ? pa.Select(n => (int)n!.GetValue<double>()).ToHashSet() : null;
                int n = FlpNotes.Apply(m.Patterns.Where(p => which == null || which.Contains(p.Index)), e);
                var r = ProjectJson();
                r["changed"] = n;
                return r;
            }
            case "project.save":
            {
                var m = Need();
                var dst = S(a, "path", projectPath!);
                if (File.Exists(dst) && B(a, "backup")) File.Copy(dst, dst + ".bak", true);
                project!.Save(dst);
                projectPath = dst;
                return dst;
            }
            case "project.report": return Need().Report(Path.GetFileName(projectPath) ?? "");
            case "project.midi":
            {
                var m = Need();
                if (B(a, "song")) return FlpNotes.ExportSong(m, S(a, "path"));
                var dir = S(a, "path");
                Directory.CreateDirectory(dir);
                int count = 0;
                foreach (var p in m.Patterns.Where(p => p.NoteCount > 0))
                {
                    var name = FlpModel.Text(p.NameEvent);
                    if (name.Length == 0) name = $"Pattern {p.Index}";
                    foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                    FlpNotes.ExportPattern(m, p, Path.Combine(dir, $"{p.Index:00} {name}.mid"));
                    count++;
                }
                return count;
            }
            case "samples.relink": { var log = Need().RelinkMissing(S(a, "folder")); var r = ProjectJson(); r["log"] = Arr(log); return r; }
            case "samples.collect": { var log = Need().CollectSamples(S(a, "folder")); var r = ProjectJson(); r["log"] = Arr(log); return r; }
            case "samples.replace": { int n = Need().ReplaceInSamplePaths(S(a, "find"), S(a, "replace")); var r = ProjectJson(); r["changed"] = n; return r; }

            // ---------------------------------------------------------- mods
            case "mods.list":
                return new JsonArray(ScriptPack.All.Select(s => (JsonNode)new JsonObject
                {
                    ["kind"] = s.Kind, ["name"] = s.Name, ["description"] = s.Description, ["installed"] = s.Installed,
                }).ToArray());
            case "mods.install":
            case "mods.uninstall":
            {
                var names = a["names"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet();
                int n = 0;
                foreach (var s in ScriptPack.All.Where(s => names.Contains(s.Kind + "/" + s.Name)))
                {
                    if (cmd == "mods.install") { ScriptPack.Install(s); n++; }
                    else if (ScriptPack.Uninstall(s)) n++;
                }
                return n;
            }
            case "template.install": return ScriptPack.InstallTemplate(S(a, "src"), S(a, "name"));

            // ---------------------------------------------------------- mods that change FL itself
            case "companion.status": return FlMods.CompanionStatus(installs);
            case "companion.install": FlMods.InstallCompanion(); return FlMods.CompanionStatus(installs);
            case "companion.uninstall": FlMods.UninstallCompanion(); return FlMods.CompanionStatus(installs);
            case "companion.config": FlMods.SaveCompanionConfig(a["config"]!); return true;
            case "splash.styles":
                return new JsonObject
                {
                    ["styles"] = new JsonArray(SplashRenderer.Styles.Select(x => (JsonNode)new JsonObject { ["id"] = x.Id, ["label"] = x.Label }).ToArray()),
                    ["accents"] = new JsonArray(FlMods.Accents.Select(x => (JsonNode)x).ToArray()),
                    ["current"] = FlMods.CurrentSplash(installs),
                    ["frames"] = FlMods.Frames, ["w"] = SplashRenderer.W / 2, ["h"] = SplashRenderer.H / 2,
                };
            case "splash.preview": return FlMods.SplashPreview(S(a, "spec"), installs.Last().Exe);
            case "splash.apply": FlMods.ApplySplash(S(a, "spec"), installs); return FlMods.CurrentSplash(installs);
            case "paths":
                return new JsonObject
                {
                    ["userData"] = FlPaths.UserDataDir, ["pianoRoll"] = FlPaths.PianoRollScriptsDir, ["audio"] = FlPaths.AudioScriptsDir,
                    ["themes"] = FlPaths.ThemesDir, ["palettes"] = FlPaths.NoteColorPresetsDir, ["templates"] = FlPaths.TemplatesDir,
                    ["config"] = LauncherStore.Dir,
                };

            // ---------------------------------------------------------- themes
            case "themes.list": return ThemesList();
            case "themes.load": return ThemeJson(FlTheme.Load(S(a, "path")));
            case "themes.generate":
            {
                var t = FlTheme.Generate(ColorTranslator.FromHtml(S(a, "base", "#ff5a36")),
                    Enum.Parse<FlTheme.Scheme>(S(a, "scheme", "Complementary")), B(a, "dark"), I(a, "seed", 1));
                return ThemeJson(t);
            }
            case "themes.save":
            {
                var t = new FlTheme();
                foreach (var kv in a["values"]!.AsArray())
                    t.Values.Add(new(kv!["k"]!.GetValue<string>(), kv["v"]!.GetValue<string>()));
                var name = S(a, "name", "FLPTools theme");
                foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                Directory.CreateDirectory(FlPaths.ThemesDir);
                var path = Path.Combine(FlPaths.ThemesDir, name + ".flstheme");
                t.Save(path);
                return path;
            }
            case "palette.save":
            {
                var colors = a["colors"]!.AsArray().Select(n => ColorTranslator.FromHtml(n!.GetValue<string>())).ToArray();
                var name = S(a, "name", "FLPTools palette");
                foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                Directory.CreateDirectory(FlPaths.NoteColorPresetsDir);
                var path = Path.Combine(FlPaths.NoteColorPresetsDir, name + ".ncp");
                FlTheme.SaveNotePalette(path, colors);
                return path;
            }
        }
        throw new InvalidOperationException($"Unknown command {cmd}");
    }

    static JsonArray Arr(IEnumerable<string> items) => new(items.Select(s => (JsonNode)s).ToArray());

    FlpModel Need() => model ?? throw new InvalidOperationException("Open a project first.");

    // ------------------------------------------------------------------ launcher helpers

    JsonObject Init()
    {
        launcher.RestoreFinished(installs);
        var inst = new JsonArray();
        foreach (var i in installs)
        {
            var sig = FileSignature.CheckInstall(i.Dir);
            inst.Add(new JsonObject
            {
                ["id"] = i.Id, ["year"] = i.Year, ["version"] = i.Version, ["major"] = i.Major, ["dir"] = i.Dir,
                ["exe"] = i.Exe, ["registryKey"] = i.RegistryKey, ["icon"] = FlInstalls.IconDataUrl(i.Exe),
                ["running"] = FlInstalls.Running(i).Count > 0,
                ["template"] = FlInstalls.GetGeneral(i, "Template") ?? "",
                ["background"] = FlInstalls.DefaultBackground(i),
                ["files"] = sig.Status, ["filesDetail"] = sig.Detail,
                ["art"] = new JsonArray(FlInstalls.Artwork(i).Select(x => (JsonNode)new JsonObject { ["name"] = x.Name, ["url"] = x.Url }).ToArray()),
            });
        }
        return new JsonObject
        {
            ["installs"] = inst,
            ["configs"] = ToNode(state.Configs.OrderBy(c => c.Order).ToList()),
            ["settings"] = ToNode(state.Settings),
            ["cores"] = Environment.ProcessorCount,
            ["machine"] = Environment.MachineName,
        };
    }

    static JsonObject RecentItem(string p, string install)
    {
        bool exists = File.Exists(p);
        return new JsonObject
        {
            ["path"] = p, ["name"] = Path.GetFileNameWithoutExtension(p), ["install"] = install, ["exists"] = exists,
            ["modified"] = exists ? File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm") : "",
        };
    }

    static JsonArray RecentJson(List<string> list) => new(list.Select(p => (JsonNode)RecentItem(p, "")).ToArray());

    JsonNode Launch(string id, string projectOverride)
    {
        var cfg = state.Configs.First(c => c.Id == id);
        var inst = installs.FirstOrDefault(i => i.Id == cfg.Install) ?? throw new InvalidOperationException($"{cfg.Install} is not installed.");
        var r = launcher.Launch(cfg, inst, projectOverride);
        var proc = r.Process;
        var started = DateTime.Now;
        live[cfg.Id] = proc;
        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) =>
        {
            cfg.PlaySeconds += (long)(DateTime.Now - started).TotalSeconds;
            live.Remove(cfg.Id);
            var restored = launcher.RestoreFinished(installs);
            Save();
            form.Post(new JsonObject
            {
                ["event"] = "exited", ["id"] = cfg.Id, ["playSeconds"] = cfg.PlaySeconds, ["restored"] = restored.Count > 0,
            });
        };
        _ = Task.Run(async () =>
        {
            for (int k = 0; k < 240 && !proc.HasExited; k++)
            {
                await Task.Delay(500);
                proc.Refresh();
                if (proc.MainWindowHandle != IntPtr.Zero && proc.MainWindowTitle.Contains("FL Studio"))
                {
                    form.Post(new JsonObject { ["event"] = "ready", ["id"] = cfg.Id });
                    return;
                }
            }
        });
        return new JsonObject
        {
            ["pid"] = proc.Id, ["project"] = r.OpenedProject, ["notes"] = Arr(r.Notes),
            ["lastPlayed"] = cfg.LastPlayed?.ToString("o"),
        };
    }

    // ------------------------------------------------------------------ project editor helpers

    static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    JsonObject ProjectJson()
    {
        var m = Need();
        var channels = new JsonArray();
        foreach (var c in m.Channels)
        {
            var sample = FlpModel.Text(c.SampleEvent);
            channels.Add(new JsonObject
            {
                ["index"] = c.Index, ["name"] = FlpModel.Text(c.NameEvent), ["type"] = c.TypeName,
                ["plugin"] = FlpModel.Text(c.PluginEvent),
                ["color"] = c.ColorEvent != null ? Hex(FlpModel.ToColor(c.ColorEvent.Value)) : null,
                ["sample"] = sample, ["sampleOk"] = sample.Length == 0 || File.Exists(FlpModel.ExpandFlPath(sample)),
            });
        }
        var patterns = new JsonArray();
        foreach (var p in m.Patterns)
            patterns.Add(new JsonObject
            {
                ["index"] = p.Index, ["name"] = FlpModel.Text(p.NameEvent), ["notes"] = p.NoteCount,
                ["color"] = p.ColorEvent != null ? Hex(FlpModel.ToColor(p.ColorEvent.Value)) : null,
            });
        var inserts = new JsonArray();
        foreach (var i in m.Inserts)
            inserts.Add(new JsonObject
            {
                ["index"] = i.Index, ["name"] = FlpModel.Text(i.NameEvent),
                ["color"] = i.ColorEvent != null ? Hex(FlpModel.ToColor(i.ColorEvent.Value)) : null,
                ["plugins"] = Arr(i.Plugins), ["used"] = i.NameEvent != null || i.Plugins.Count > 0 || i.ColorEvent != null,
                ["label"] = i.Index == 0 ? "Master" : i.Index == m.Inserts.Count - 1 ? "Current" : $"Insert {i.Index}",
            });
        int len = m.SongLengthTicks;
        return new JsonObject
        {
            ["path"] = projectPath, ["name"] = Path.GetFileNameWithoutExtension(projectPath),
            ["version"] = project!.VersionString, ["major"] = project.VersionMajor,
            ["info"] = new JsonObject
            {
                ["title"] = m.Title, ["artists"] = m.Artists, ["genre"] = m.Genre, ["comments"] = m.Comments, ["url"] = m.Url,
                ["tempo"] = m.Tempo, ["timeSig"] = $"{m.TimeSigNum}/{m.TimeSigBeat}", ["ppq"] = m.Ppq,
            },
            ["lengthBeats"] = m.Ppq > 0 ? (double)len / m.Ppq : 0,
            ["clips"] = m.Clips().Count,
            ["channels"] = channels, ["patterns"] = patterns, ["inserts"] = inserts,
        };
    }

    void ApplyEdits(JsonObject a)
    {
        var m = Need();
        if (a["info"] is JsonObject info)
        {
            if (info["title"] != null) m.Title = S(info, "title");
            if (info["artists"] != null) m.Artists = S(info, "artists");
            if (info["genre"] != null) m.Genre = S(info, "genre");
            if (info["comments"] != null) m.Comments = S(info, "comments");
            if (info["url"] != null) m.Url = S(info, "url");
            if (info["tempo"] != null) m.Tempo = D(info, "tempo", m.Tempo);
        }
        foreach (var e in (a["channels"] as JsonArray ?? new()).OfType<JsonObject>())
        {
            var c = m.Channels.First(x => x.Index == I(e, "index"));
            if (e["name"] != null) m.SetChannelName(c, S(e, "name"));
            if (e["color"] != null) m.SetChannelColor(c, ColorTranslator.FromHtml(S(e, "color")));
        }
        foreach (var e in (a["patterns"] as JsonArray ?? new()).OfType<JsonObject>())
        {
            var p = m.Patterns.First(x => x.Index == I(e, "index"));
            if (e["name"] != null) m.SetPatternName(p, S(e, "name"));
            if (e["color"] != null) m.SetPatternColor(p, ColorTranslator.FromHtml(S(e, "color")));
        }
        foreach (var e in (a["inserts"] as JsonArray ?? new()).OfType<JsonObject>())
        {
            var i = m.Inserts.First(x => x.Index == I(e, "index"));
            if (e["name"] != null) m.SetInsertName(i, S(e, "name"));
            if (e["color"] != null) m.SetInsertColor(i, ColorTranslator.FromHtml(S(e, "color")));
        }
        m.Rebuild();
    }

    JsonNode Batch(string folder, string op, string version)
    {
        var files = Directory.EnumerateFiles(folder, "*.flp", SearchOption.AllDirectories)
            .Where(f => !f.Contains("(FL25)", StringComparison.OrdinalIgnoreCase)).ToList();
        var arr = new JsonArray();
        foreach (var f in files)
        {
            string msg;
            bool ok = true;
            try
            {
                switch (op)
                {
                    case "convert":
                        var p = FlpProject.Load(f);
                        if (p.IsLocked) { ok = false; msg = "locked (saved by an unregistered FL)"; break; }
                        if (p.VersionMajor < 26) { msg = $"skipped (FL {p.VersionString})"; break; }
                        Fl25Converter.Convert25(p, version);
                        var dst = Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileNameWithoutExtension(f) + " (FL25).flp");
                        p.Save(dst);
                        msg = "converted -> " + Path.GetFileName(dst);
                        break;
                    case "report":
                        var rp = FlpProject.Load(f);
                        if (rp.IsLocked) { ok = false; msg = "locked (saved by an unregistered FL)"; break; }
                        var r = new FlpModel(rp).Report(Path.GetFileName(f));
                        File.WriteAllText(Path.ChangeExtension(f, ".report.txt"), r);
                        msg = "report written";
                        break;
                    default:
                        (ok, msg) = FlpJson.VerifyRoundTrip(File.ReadAllBytes(f));
                        break;
                }
            }
            catch (Exception ex) { ok = false; msg = ex.Message; }
            arr.Add(new JsonObject { ["file"] = f, ["ok"] = ok, ["msg"] = msg });
        }
        return arr;
    }

    // ------------------------------------------------------------------ theme helpers

    static JsonObject ThemeJson(FlTheme t) => new()
    {
        ["values"] = new JsonArray(t.Values.Select(kv => (JsonNode)new JsonObject { ["k"] = kv.Key, ["v"] = kv.Value }).ToArray()),
        ["colors"] = new JsonObject(t.Values.Where(kv => FlTheme.IsColorKey(kv.Key))
            .Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)(FlTheme.ToColor(kv.Value) is Color c ? Hex(c) : null)))),
    };

    static JsonArray ThemesList()
    {
        var arr = new JsonArray();
        void Add(string path, bool builtIn)
        {
            try
            {
                var t = FlTheme.Load(path);
                var swatch = new[] { "Selected", "Highlight", "StepEven", "StepOdd", "NoteColor0", "NoteColor5", "NoteColor8" }
                    .Select(k => FlTheme.ToColor(t[k]) is Color c ? (JsonNode)Hex(c) : null).Where(x => x != null).ToArray();
                arr.Add(new JsonObject
                {
                    ["name"] = Path.GetFileNameWithoutExtension(path), ["path"] = path, ["builtIn"] = builtIn,
                    ["hue"] = t["Hue"], ["swatch"] = new JsonArray(swatch),
                });
            }
            catch { }
        }
        if (Directory.Exists(FlPaths.ThemesDir))
            foreach (var f in Directory.EnumerateFiles(FlPaths.ThemesDir, "*.flstheme", SearchOption.AllDirectories)) Add(f, false);
        var seen = new HashSet<string>();
        foreach (var d in FlPaths.BuiltInThemeDirs)
            foreach (var f in Directory.EnumerateFiles(d, "*.flstheme", SearchOption.AllDirectories))
                if (seen.Add(Path.GetFileName(f))) Add(f, true);
        return arr;
    }
}
