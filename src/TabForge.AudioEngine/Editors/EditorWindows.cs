using System.Runtime.InteropServices;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Editors;

// Owns: the plug-in editor windows (plain Win32 top-level windows, one per plug-in, pumped on the main thread) and the registry of which window shows which
//   plug-in. One instance per message-loop owner (the engine session, the isolated plug-in host); a window finds its instance through a weak handle in its user data.
// Tests: no dedicated test class.
// Threads: the owner's main thread only.

/// <summary>
/// Plug-in editor windows: plain Win32 top-level windows owned by TabForge's main window (so they stay above it and
/// minimise with it), one per plug-in, created and pumped on the engine's main thread.
/// </summary>
public sealed unsafe class EditorWindows : IPluginEditorHost
{
    private const int WS_CHILD = 0x40000000, WS_OVERLAPPED = 0, WS_CAPTION = 0xC00000, WS_SYSMENU = 0x80000, WS_MINIMIZEBOX = 0x20000, WS_CLIPCHILDREN = 0x2000000;
    private const int WM_CLOSE = 0x10, WM_DESTROY = 2, SW_SHOW = 5, CW_USEDEFAULT = unchecked((int)0x80000000);
    private const uint SWP_NOMOVE = 2, SWP_NOZORDER = 4;
    private const int WS_POPUP = unchecked((int)0x80000000), WS_EX_TOOLWINDOW = 0x80, WS_EX_DLGMODALFRAME = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName, lpszClassName; public IntPtr hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    // Unicode (W) to match the Unicode window class: the ANSI version cut titles to their first letter.
    [DllImport("user32", EntryPoint = "DefWindowProcW")] private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32")] internal static extern bool IsWindow(IntPtr h);
    [DllImport("user32")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32")] private static extern bool AdjustWindowRectEx(ref RECT r, int style, bool menu, int ex);
    [DllImport("user32")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
    [DllImport("user32")] private static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr region, uint flags);
    [DllImport("user32")] private static extern IntPtr LoadCursor(IntPtr inst, int id);
    [DllImport("user32")] public static extern bool PeekMessage(out MSG msg, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32")] public static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32")] public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr handles, uint ms, uint mask, uint flags);
    [DllImport("dwmapi")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("kernel32")] private static extern IntPtr GetModuleHandle(IntPtr name);

    private const string ClassName = "TabForgePluginEditor";
    private const int GWLP_USERDATA = -21, WM_NCCREATE = 0x81, ERROR_CLASS_ALREADY_EXISTS = 1410;
    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);

    // Weak, so a forgotten instance is collected; the windows' user data holds its value.
    // While any editor window is open the instance is also held strongly here (a window's WM_CLOSE needs its owner to run Close/CloseEditor), and let go when the last one closes.
    private static readonly HashSet<EditorWindows> WithOpenWindows = new();
    private GCHandle _self;

    private readonly EngineLogWriter _log;

    public EditorWindows(EngineLogWriter log)
    {
        _log = log;
        _self = GCHandle.Alloc(this, GCHandleType.Weak);
    }

    ~EditorWindows() { if (_self.IsAllocated) _self.Free(); }

    /// <summary>Raised when the user closes a plug-in window (not when the app closes one).</summary>
    public Action? UserClosed { get; set; }
    private readonly Dictionary<IntPtr, IPluginInstance> Open = new();
    private readonly Dictionary<IPluginInstance, IntPtr> ByPlugin = new(ReferenceEqualityComparer.Instance);
    /// <summary>Named resize handlers per window so <see cref="Close(IntPtr)"/> can unsubscribe (anonymous lambdas leaked the plug-in).</summary>
    private readonly Dictionary<IntPtr, Action<int, int>> ResizeHandlers = new();
    /// <summary>The editor size each window last got (open or a plug-in request): a request within <see cref="SameSizePixels"/> of it is a repeat and is ignored.</summary>
    private readonly Dictionary<IntPtr, (int W, int H)> EditorSizes = new();
    private const int SameSizePixels = 1;

    /// <summary>How many plug-in windows are open now.</summary>
    public int OpenCount => Open.Count;
    public bool IsSupported => true;
    public Func<IPluginInstance, long>? ProcessCalls { get; set; }
    /// <summary>Diagnostics: per open window, its idle ticks and the plug-in's process calls, logged every ~2 s (frozen-meter hunting).</summary>
    private readonly Dictionary<IntPtr, EditorRate> Rates = new();
    public string UnsupportedReason => "";

    [UnmanagedCallersOnly]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            if (msg == WM_NCCREATE) SetWindowLongPtr(hwnd, GWLP_USERDATA, Marshal.ReadIntPtr(l));   // CREATESTRUCT.lpCreateParams: the owning instance's handle
            if (msg == WM_CLOSE)
            {
                var data = GetWindowLongPtr(hwnd, GWLP_USERDATA);
                if (data == IntPtr.Zero || GCHandle.FromIntPtr(data).Target is not EditorWindows owner) { DestroyWindow(hwnd); return 0; }
                owner.Close(hwnd);
                try { owner.UserClosed?.Invoke(); } catch { /* swallow: an editor-close handler must not break window teardown */ }
                return 0;
            }
        }
        catch { return 0; }
        return DefWindowProc(hwnd, msg, w, l);
    }

    private void Register()
    {
        var cls = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc,
            hInstance = GetModuleHandle(IntPtr.Zero), hCursor = LoadCursor(IntPtr.Zero, 32512), lpszClassName = ClassName,
            hbrBackground = (IntPtr)(1 + 15), // COLOR_BTNFACE
        };
        // The class is the process's: a second owner finds it registered already.
        if (RegisterClassEx(ref cls) == 0 && Marshal.GetLastWin32Error() is var error && error != ERROR_CLASS_ALREADY_EXISTS) _log.Write($"editor window class not registered (error {error})");
    }

    /// <summary>Shows the plug-in's editor (or brings it forward). False when the plug-in has no editor.</summary>
    public bool Show(IPluginInstance plugin, string title, IntPtr owner, bool dark, bool docked = false, bool onTop = false, Action<int, int>? sized = null)
    {
        if (ByPlugin.TryGetValue(plugin, out var existing))
        {
            // Same mode: bring it forward. Switching docked / floating: close and reopen in the new place.
            if (Docked.Contains(existing) == docked)
            {
                if (docked) { DockAreas[existing] = owner; PlaceDocked(existing, force: true); }   // re-opened over a (possibly new) host area: place and repaint now
                else ShowWindow(existing, SW_SHOW);
                if (!docked) SetForegroundWindow(existing);
                return true;
            }
            Close(existing);
        }
        if (!plugin.HasEditor) return false;
        Register();
        // Floating: a top-level window owned by TabForge's window (no title-bar icon), optionally always on top.
        // Docked: a borderless top-level window in this process, owned by the FX chain window and kept exactly over its
        // host area (see PlaceDocked). It is NOT a child of a window in TabForge's process: a cross-process child shares
        // input state with its parent, which made the plug-in's own drop-down menus close the moment they opened.
        var area = owner;
        var root = docked ? GetAncestor(area, 2 /*GA_ROOT*/) : owner;
        var style = docked ? WS_POPUP | WS_CLIPCHILDREN : WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_CLIPCHILDREN;
        // Docked: the popup opens at the host area's size when that is known, so the plug-in attaches to a window of its final size.
        var (startW, startH) = docked && TryDockRect(area, out var areaRect) ? (areaRect.Right - areaRect.Left, areaRect.Bottom - areaRect.Top) : (400, 300);
        var hwnd = CreateWindowEx(docked ? WS_EX_TOOLWINDOW : WS_EX_DLGMODALFRAME, ClassName, title, style, docked ? 0 : CW_USEDEFAULT, docked ? 0 : CW_USEDEFAULT, startW, startH, root, IntPtr.Zero, GetModuleHandle(IntPtr.Zero), GCHandle.ToIntPtr(_self));
        if (hwnd == IntPtr.Zero) { _log.Write($"editor window not created (error {Marshal.GetLastWin32Error()})"); return false; }
        var darkMode = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
        var size = plugin.OpenEditor(hwnd);
        if (size is null) { DestroyWindow(hwnd); return false; }
        Open[hwnd] = plugin;
        lock (WithOpenWindows) WithOpenWindows.Add(this);
        ByPlugin[plugin] = hwnd;
        if (docked) { Docked.Add(hwnd); DockAreas[hwnd] = area; Settle.Add(hwnd); }
        EditorSizes[hwnd] = size.Value;
        Resize(hwnd, size.Value.Width, size.Value.Height);
        sized?.Invoke(size.Value.Width, size.Value.Height);
        if (plugin is IEditorResizeSource source)
        {
            void OnResize(int w, int h)
            {
                if (!ByPlugin.TryGetValue(plugin, out var hw) || EditorSizes.TryGetValue(hw, out var last) && Math.Abs(last.W - w) <= SameSizePixels && Math.Abs(last.H - h) <= SameSizePixels) return;
                EditorSizes[hw] = (w, h);
                Resize(hw, w, h);
                sized?.Invoke(w, h);
            }
            source.ResizeRequested += OnResize;
            ResizeHandlers[hwnd] = OnResize;
        }
        if (onTop && !docked) SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | 1); // HWND_TOPMOST
        // The first ShowWindow of a process can take the (hidden) start-up setting instead: show until visible.
        ShowWindow(hwnd, SW_SHOW);
        if (!IsWindowVisible(hwnd)) ShowWindow(hwnd, SW_SHOW);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOZORDER | 1 /*NOSIZE*/ | 0x40 /*SHOWWINDOW*/);
        if (docked) PlaceDocked(hwnd, force: true); else SetForegroundWindow(hwnd);
        _log.Write($"editor window 0x{hwnd:X} '{title}' {size.Value.Width}x{size.Value.Height} visible={IsWindowVisible(hwnd)}");
        _log.Write($"editor open: '{title}' {EditorDiagnostics.Describe(plugin)} host={(docked ? "docked" : "floating")} runtime='{RuntimeInformation.FrameworkDescription}'");
        Rates[hwnd] = new EditorRate(title, plugin);
        return true;
    }

    private readonly HashSet<IntPtr> Docked = new();
    private readonly Dictionary<IntPtr, IntPtr> DockAreas = new();
    private readonly Dictionary<IntPtr, (int X, int Y, int W, int H)> DockedAt = new();
    /// <summary>Docked windows shown but not yet settled: the next idle tick places and repaints them once more and tells the plug-in its size.</summary>
    private readonly HashSet<IntPtr> Settle = new();

    /// <summary>The part of the host area inside the FX window's client area, in screen pixels; false when the area or the FX window is hidden or minimised, or nothing of it shows.</summary>
    private static bool TryDockRect(IntPtr area, out RECT r)
    {
        r = default;
        var root = GetAncestor(area, 2);
        if (!IsWindowVisible(area) || (root != IntPtr.Zero && IsIconic(root)) || !GetWindowRect(area, out r)) return false;
        // Only the part inside the FX window's client area shows (the host area can be larger than what is visible).
        if (root != IntPtr.Zero && GetClientRect(root, out var client))
        {
            var origin = new POINT();
            ClientToScreen(root, ref origin);
            r.Left = Math.Max(r.Left, origin.X); r.Top = Math.Max(r.Top, origin.Y);
            r.Right = Math.Min(r.Right, origin.X + client.Right); r.Bottom = Math.Min(r.Bottom, origin.Y + client.Bottom);
            if (r.Right <= r.Left || r.Bottom <= r.Top) return false;
        }
        return true;
    }

    /// <summary>Keeps a docked editor exactly over its host area in TabForge's FX window (hidden when that is hidden or minimised).</summary>
    private void PlaceDocked(IntPtr hwnd, bool force = false)
    {
        if (!DockAreas.TryGetValue(hwnd, out var area)) return;
        if (!IsWindow(area)) { Close(hwnd); return; }
        if (!TryDockRect(area, out var r))
        {
            if (IsWindowVisible(hwnd)) ShowWindow(hwnd, 0);
            DockedAt.Remove(hwnd);
            return;
        }
        var now = (r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));
        var wasVisible = IsWindowVisible(hwnd);
        if (!force && DockedAt.TryGetValue(hwnd, out var last) && last == now && wasVisible) return;
        var resized = !DockedAt.TryGetValue(hwnd, out var before) || before.W != now.Item3 || before.H != now.Item4;
        DockedAt[hwnd] = now;
        SetWindowPos(hwnd, IntPtr.Zero, now.Left, now.Top, now.Item3, now.Item4, SWP_NOZORDER | 0x10 /*NOACTIVATE*/ | 0x40 /*SHOWWINDOW*/);
        if (resized && !Settle.Contains(hwnd)) TellEditorSize(hwnd, now.Item3, now.Item4);
        // Shown again (host area was hidden): repaint the whole editor, never a blank box until the plug-in's next redraw. Asynchronous: no UPDATENOW inside the first show.
        if (!wasVisible || force) RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x80);   // INVALIDATE | ERASE | ALLCHILDREN
    }

    private void TellEditorSize(IntPtr hwnd, int width, int height)
    {
        if (Open.TryGetValue(hwnd, out var plugin) && plugin is IEditorSizeSink sink)
            try { sink.SetEditorSize(width, height); } catch (Exception ex) { _log.Write($"editor size failed: {ex.Message}"); }
    }

    /// <summary>Sizes the window to the plug-in's size. Docked: the popup stays over its host area (the area follows the size through the host's size event); the view is told the size it asked for.</summary>
    private void Resize(IntPtr hwnd, int width, int height)
    {
        if (Docked.Contains(hwnd))
        {
            if (!Settle.Contains(hwnd)) TellEditorSize(hwnd, width, height);
            PlaceDocked(hwnd);
            return;
        }
        var rect = new RECT { Right = width, Bottom = height };
        AdjustWindowRectEx(ref rect, WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX, false, 0);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top, SWP_NOMOVE | SWP_NOZORDER);
    }

    /// <summary>Closes the editor of a plug-in about to be removed.</summary>
    public void CloseFor(IPluginInstance plugin)
    {
        if (ByPlugin.TryGetValue(plugin, out var hwnd)) Close(hwnd);
    }

    private void Close(IntPtr hwnd)
    {
        if (Open.Remove(hwnd, out var plugin))
        {
            ByPlugin.Remove(plugin);
            if (plugin is IEditorResizeSource source && ResizeHandlers.Remove(hwnd, out var onResize))
            {
                try { source.ResizeRequested -= onResize; } catch (Exception ex) { _log.Write($"editor resize unsubscribe failed: {ex.Message}"); }
            }
            try { plugin.CloseEditor(); } catch (Exception ex) { _log.Write($"editor close failed: {ex.Message}"); }
        }
        else ResizeHandlers.Remove(hwnd);
        Docked.Remove(hwnd);
        DockAreas.Remove(hwnd);
        DockedAt.Remove(hwnd);
        Settle.Remove(hwnd);
        EditorSizes.Remove(hwnd);
        Rates.Remove(hwnd);
        DestroyWindow(hwnd);
        if (Open.Count == 0) lock (WithOpenWindows) WithOpenWindows.Remove(this);
    }

    /// <summary>Idle cadence of <see cref="PumpOnce"/> (~30 Hz: editor redraws, the loop owner's periodic checks).</summary>
    public const double IdleIntervalMs = 33;

    /// <summary>
    /// One turn of a plug-in-hosting message loop (the engine's and the isolated plug-in host's): dispatches every pending window
    /// message; every <see cref="IdleIntervalMs"/> runs <see cref="Idle"/> and then <paramref name="idle"/>; then waits up to 10 ms
    /// for the next message. Returns false once <paramref name="idle"/> does (the loop should end). Pass a delegate created once.
    /// </summary>
    public bool PumpOnce(ref long lastIdle, Func<bool>? idle)
    {
        while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (System.Diagnostics.Stopwatch.GetElapsedTime(lastIdle).TotalMilliseconds >= IdleIntervalMs)
        {
            lastIdle = System.Diagnostics.Stopwatch.GetTimestamp();
            Idle();
            if (idle is not null && !idle()) return false;
        }
        MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, 10, 0x04FF, 0x0004);
        return true;
    }

    /// <summary>Main thread, ~30 times a second: lets VST2 editors redraw.</summary>
    public void Idle()
    {
        if (Open.Count == 0) return;
        if (Docked.Count > 0)
            foreach (var hwnd in Docked.ToList())
            {
                if (Settle.Remove(hwnd)) DockedAt.Remove(hwnd);   // forgotten, so the plug-in is told its size now
                PlaceDocked(hwnd, force: !DockedAt.ContainsKey(hwnd));
            }
        foreach (var plugin in Open.Values.ToList())
            try { plugin.EditorIdle(); } catch (Exception ex) { _log.Write($"editor idle failed: {ex.Message}"); }
        foreach (var (hwnd, rate) in Rates)
            if (Open.TryGetValue(hwnd, out var shown) && rate.Tick(() => ProcessCalls?.Invoke(shown) ?? -1) is { } line) _log.Write(line);
    }

    public void CloseAll()
    {
        foreach (var hwnd in Open.Keys.ToList()) Close(hwnd);
        // The session's UserClosed closure (EngineSession.OpenEditor) must not outlive the run: a fresh session re-registers it.
        UserClosed = null;
    }
}
