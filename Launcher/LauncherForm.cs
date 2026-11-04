using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FLPTools.Launcher;

/// <summary>The app window: a WebView2 showing wwwroot, talking to <see cref="Bridge"/>.</summary>
public sealed class LauncherForm : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(18, 18, 22) };
    Bridge? bridge;
    readonly FLPTools.Mods.WindowAnimator animator = new();
    FormWindowState savedState;
    FormBorderStyle savedBorder;
    bool fullscreen;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
    const int HTCLIENT = 1, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    const int TopGrip = 5;   // strip along the top edge kept for resizing, since the page covers the rest

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    public LauncherForm()
    {
        Text = "FLPTools";
        Width = 1360;
        Height = 820;
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(12, 13, 17);
        KeyPreview = true;
        Padding = new Padding(0, TopGrip, 0, 0);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Controls.Add(web);
        Load += async (_, _) => await InitWeb();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));   // dark frame (Windows 10/11)
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027);      // SWP_FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER
    }

    // The window keeps its normal sizable frame (so snapping, resizing and minimize animations still work)
    // but the caption is folded into the client area; the page draws its own title bar.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero && FormBorderStyle != FormBorderStyle.None)
        {
            var r = Marshal.PtrToStructure<RECT>(m.LParam);
            int fx = GetSystemMetrics(32) + GetSystemMetrics(92);   // SM_CXSIZEFRAME + SM_CXPADDEDBORDER
            int fy = GetSystemMetrics(33) + GetSystemMetrics(92);
            r.Left += fx; r.Right -= fx; r.Bottom -= fy;
            if (WindowState == FormWindowState.Maximized) r.Top += fy;
            Marshal.StructureToPtr(r, m.LParam, false);
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WM_NCHITTEST && (int)m.Result == HTCLIENT && WindowState == FormWindowState.Normal)
        {
            var pt = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            if (pt.Y < TopGrip) m.Result = pt.X < 12 ? HTTOPLEFT : pt.X > ClientSize.Width - 12 ? HTTOPRIGHT : HTTOP;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Padding = new Padding(0, WindowState == FormWindowState.Normal ? TopGrip : 0, 0, 0);
        if (web.CoreWebView2 != null) Post(new JsonObject { ["event"] = "window", ["maximized"] = WindowState == FormWindowState.Maximized });
    }

    /// <summary>Fallback window drag for the page's title bar (used if CSS app-region is not available).</summary>
    public void BeginDrag()
    {
        if (!MouseButtons.HasFlag(MouseButtons.Left)) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);   // WM_NCLBUTTONDOWN, HTCAPTION
    }

    /// <summary>Turn FL window animations on/off to match the launcher settings (safe from any thread).</summary>
    public void ApplyWindowAnimations(LauncherSettings s, string? extraTarget = null)
    {
        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            animator.DurationMs = Math.Clamp(s.FlWindowAnimMs, 80, 1000);
            animator.Rise = s.FlWindowAnimRise;
            animator.InsideFl = s.FlWindowAnimInside;
            if (extraTarget != null) animator.Targets.Add(extraTarget);
            if (s.FlWindowAnim || extraTarget != null) animator.Start(); else animator.Stop();
        });
    }

    public void RemoveAnimationTarget(string name) => BeginInvoke(() => animator.Targets.Remove(name));

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        animator.Dispose();
        base.OnFormClosed(e);
    }

    public void ToggleMaximize() => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    async Task InitWeb()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FLPTools", "WebView2");
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show("FLPTools needs the Microsoft Edge WebView2 Runtime.\n\n" + ex.Message, "FLPTools", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        var core = web.CoreWebView2;
        var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        core.SetVirtualHostNameToFolderMapping("flptools.app", root, CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsNonClientRegionSupportEnabled = true;   // CSS app-region: drag on the page's title bar
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        bridge = new Bridge(this);
        foreach (var i in bridge.Installs)
        {
            var art = Path.Combine(i.Dir, "Artwork");
            if (Directory.Exists(art)) core.SetVirtualHostNameToFolderMapping(FlInstalls.ArtHost(i), art, CoreWebView2HostResourceAccessKind.Allow);
        }
        core.WebMessageReceived += async (_, e) =>
        {
            var reply = await bridge.Handle(e.TryGetWebMessageAsString());
            core.PostWebMessageAsJson(reply);
        };
        core.Navigate("https://flptools.app/index.html");
    }

    /// <summary>Push a notification to the page (safe to call from any thread).</summary>
    public void Post(JsonObject message)
    {
        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            if (message["event"]?.GetValue<string>() == "ready" && WindowState != FormWindowState.Minimized)
                Post(new JsonObject { ["event"] = "focus" });
            web.CoreWebView2?.PostWebMessageAsJson(message.ToJsonString());
        });
    }

    public void ToggleFullscreen()
    {
        if (!fullscreen)
        {
            savedState = WindowState;
            savedBorder = FormBorderStyle;
            FormBorderStyle = FormBorderStyle.None;
            Padding = Padding.Empty;
            WindowState = FormWindowState.Normal;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = savedBorder;
            WindowState = savedState;
        }
        fullscreen = !fullscreen;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11) { ToggleFullscreen(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public JsonNode? PickOpen(string filter, bool multi)
    {
        using var d = new OpenFileDialog { Filter = filter, Multiselect = multi };
        if (d.ShowDialog(this) != DialogResult.OK) return null;
        return multi ? new JsonArray(d.FileNames.Select(f => (JsonNode)f).ToArray()) : d.FileName;
    }

    public JsonNode? PickFolder()
    {
        using var d = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Choose a folder" };
        return d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null;
    }

    public JsonNode? PickSave(string filter, string suggested)
    {
        using var d = new SaveFileDialog { Filter = filter };
        if (suggested.Length > 0)
        {
            d.FileName = Path.GetFileName(suggested);
            var dir = Path.GetDirectoryName(suggested);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) d.InitialDirectory = dir;
        }
        return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
    }
}
