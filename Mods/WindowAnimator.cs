using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FLPTools.Mods;

/// <summary>
/// Fades FL Studio's windows in as they appear (and optionally lets floating ones rise into place).
/// Works from outside FL with a WinEvent hook and layered-window alpha: FL's files and memory are not
/// touched, the window's original style is put back as soon as each animation ends, and it only runs
/// while FLPTools is open.
/// </summary>
public sealed class WindowAnimator : IDisposable
{
    public int DurationMs = 220;
    public bool Rise = true;
    public bool InsideFl = false;                 // also animate windows inside FL's main window (child windows)
    public HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase) { "FL64", "FL" };

    const uint EVENT_OBJECT_SHOW = 0x8002, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    const int WS_CHILD = 0x40000000, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;
    const uint LWA_ALPHA = 2, SWP_NOSIZE = 1, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;
    const int RisePx = 14;

    static readonly HashSet<string> SkipClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "#32768", "tooltips_class32", "THintWindow", "TToolTip", "TFruityLoopsMainForm", "IME", "MSCTFIME UI",
        "TSplashForm", "TFLSplashForm", "SysShadow", "Shell_TrayWnd", "TApplication",
    };

    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    sealed class Anim
    {
        public IntPtr Hwnd;
        public int OrigEx;
        public long Start;
        public bool Rise;
        public int X, Y;
    }

    readonly WinEventProc proc;
    readonly Dictionary<uint, (string Name, long At)> procNames = new();
    readonly List<Anim> active = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    IntPtr hook;

    public WindowAnimator()
    {
        proc = OnShow;
        timer.Tick += (_, _) => Step();
    }

    public bool Running => hook != IntPtr.Zero;

    /// <summary>Must be called on a thread with a message loop (the UI thread).</summary>
    public void Start()
    {
        if (hook != IntPtr.Zero) return;
        hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public void Stop()
    {
        if (hook != IntPtr.Zero) { UnhookWinEvent(hook); hook = IntPtr.Zero; }
        foreach (var a in active.ToList()) Finish(a);
        active.Clear();
        timer.Stop();
    }

    public void Dispose() => Stop();

    string ProcessName(uint pid)
    {
        long now = Environment.TickCount64;
        if (procNames.TryGetValue(pid, out var hit) && now - hit.At < 30000) return hit.Name;
        string name = "";
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch { }
        procNames[pid] = (name, now);
        return name;
    }

    void OnShow(IntPtr hookHandle, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (!Targets.Contains(ProcessName(pid))) return;
        if (active.Any(a => a.Hwnd == hwnd)) return;

        var cls = new StringBuilder(128);
        GetClassName(hwnd, cls, 128);
        if (SkipClasses.Contains(cls.ToString())) return;

        int style = GetWindowLong(hwnd, GWL_STYLE), ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        bool child = (style & WS_CHILD) != 0;
        if (child && !InsideFl) return;
        if ((ex & (WS_EX_LAYERED | WS_EX_TRANSPARENT)) != 0) return;       // already uses its own transparency
        if (!GetWindowRect(hwnd, out var r) || r.R - r.L < 180 || r.B - r.T < 110) return;   // menus, popups, small controls

        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED);
        if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_LAYERED) == 0 || !SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA))
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);                              // this window can't be layered; leave it alone
            return;
        }
        var a = new Anim { Hwnd = hwnd, OrigEx = ex, Start = Environment.TickCount64, Rise = Rise && !child, X = r.L, Y = r.T };
        if (a.Rise) SetWindowPos(hwnd, IntPtr.Zero, r.L, r.T + RisePx, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        active.Add(a);
        timer.Start();
    }

    void Step()
    {
        long now = Environment.TickCount64;
        foreach (var a in active.ToList())
        {
            if (!IsWindow(a.Hwnd)) { active.Remove(a); continue; }
            float p = Math.Clamp((now - a.Start) / (float)Math.Max(1, DurationMs), 0f, 1f);
            float e = 1 - MathF.Pow(1 - p, 3);
            SetLayeredWindowAttributes(a.Hwnd, 0, (byte)(255 * e), LWA_ALPHA);
            if (a.Rise) SetWindowPos(a.Hwnd, IntPtr.Zero, a.X, a.Y + (int)MathF.Round(RisePx * (1 - e)), 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            if (p >= 1) { Finish(a); active.Remove(a); }
        }
        if (active.Count == 0) timer.Stop();
    }

    void Finish(Anim a)
    {
        if (!IsWindow(a.Hwnd)) return;
        SetLayeredWindowAttributes(a.Hwnd, 0, 255, LWA_ALPHA);
        if (a.Rise) SetWindowPos(a.Hwnd, IntPtr.Zero, a.X, a.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        SetWindowLong(a.Hwnd, GWL_EXSTYLE, a.OrigEx);                          // back to exactly how FL had it
        RedrawWindow(a.Hwnd, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0400);   // invalidate, erase, allchildren, frame
    }
}
