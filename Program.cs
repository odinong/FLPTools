using System.Runtime.InteropServices;
using System.Text;
using FLPTools.Flp;
using FLPTools.Launcher;

namespace FLPTools;

static class Program
{
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new LauncherForm());
            return 0;
        }

        AttachConsole(-1);
        Console.WriteLine();
        try
        {
            return RunCli(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 1;
        }
    }

    static int RunCli(string[] a)
    {
        switch (a[0].ToLowerInvariant())
        {
            case "decompile" when a.Length == 3:
                File.WriteAllText(a[2], FlpJson.Decompile(FlpProject.Load(a[1])), new UTF8Encoding(false));
                Console.WriteLine($"{a[1]} -> {a[2]}");
                return 0;
            case "recompile" when a.Length == 3:
                FlpJson.Recompile(File.ReadAllText(a[1])).Save(a[2]);
                Console.WriteLine($"{a[1]} -> {a[2]}");
                return 0;
            case "convert" when a.Length is 3 or 4:
                var p = FlpProject.Load(a[1]);
                foreach (var line in Fl25Converter.Convert25(p, a.Length == 4 ? a[3] : Fl25Converter.DefaultVersion))
                    Console.WriteLine("  " + line);
                p.Save(a[2]);
                Console.WriteLine($"{a[1]} -> {a[2]}");
                return 0;
            case "verify" when a.Length >= 2:
                int bad = 0;
                foreach (var f in a.Skip(1))
                {
                    try
                    {
                        var (ok, msg) = FlpJson.VerifyRoundTrip(File.ReadAllBytes(f));
                        Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {f}: {msg}");
                        if (!ok) bad++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"FAIL {f}: {ex.Message}");
                        bad++;
                    }
                }
                return bad == 0 ? 0 : 2;
            default:
                Console.WriteLine("""
                    FLPTools - FL Studio project decompiler/recompiler

                      FLPTools                                  open the window
                      FLPTools decompile <in.flp> <out.json>
                      FLPTools recompile <in.json> <out.flp>
                      FLPTools convert <in.flp> <out.flp> [version]   FL 26 -> FL 25 (default 25.2.5.5319)
                      FLPTools verify <file.flp>...             check FLP -> JSON -> FLP is byte-identical
                    """);
                return 1;
        }
    }
}
