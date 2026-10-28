using System.Runtime.InteropServices;
using TabForge.AudioEngine.Plugins;
using static TabForge.AudioEngine.Editors.X11Native;

namespace TabForge.AudioEngine.Editors;

// Owns: the plug-in editor windows on Linux: one plain top-level X11 window per plug-in, created by this (engine) process on its own X connection,
//   whose window id the plug-in attaches its view to (VST3 X11EmbedWindowID, CLAP x11), and the main-thread loop that runs them, including the
//   VST3 host run loop (the plug-in's timers and file descriptors, Vst3Plugin.RunLoopWait).
// Does not own: docking into TabForge's FX window (not offered on this system: an editor always gets its own window), the generic parameter panel.
// Tests: no dedicated test class (needs an X server); PluginPortabilityTests covers the host choice.
// Threads: the owner's main thread only (never the audio thread).
// Failure: Show returns false (the window is destroyed again) when the X server cannot be opened, the plug-in has no X11 view, or attaching fails.

/// <summary>Plug-in editor windows on Linux (X11 / XWayland): a top-level window per plug-in, pumped on the engine's main thread.</summary>
public sealed unsafe class X11EditorHost : IPluginEditorHost
{
    /// <summary>Logged once when the X server cannot be opened; Show then returns false for every plug-in.</summary>
    public const string NoDisplayReason = "Plug-in editors need an X11 display (DISPLAY), which could not be opened.";
    private const int WaitMs = 10;
    /// <summary>The window's size until the attached view reports its own (the same placeholder as the Windows host); it is resized to the plug-in's size before it is shown.</summary>
    private static readonly (uint W, uint H) StartSize = (400, 300);
    private const nint XaWmName = 39;

    private readonly EngineLogWriter _log;
    private IntPtr _display;
    private nint _root, _wmDelete, _wmProtocols, _netWmName, _utf8, _netActive, _netState, _netAbove, _gtkVariant, _wmClass;
    private bool _displayFailed, _dockedNoted;
    private int _reportedErrors;
    private readonly Dictionary<nint, IPluginInstance> _open = new();
    private readonly Dictionary<IPluginInstance, nint> _byPlugin = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<nint, Action<int, int>> _resizeHandlers = new();
    private readonly Dictionary<nint, (int W, int H)> _sizes = new();
    private readonly Dictionary<nint, EditorRate> _rates = new();
    private int _vst3Open;

    public X11EditorHost(EngineLogWriter log)
    {
        _log = log;
        // Before any plug-in loads (the session creates its editor host first): Xlib's thread support must come before every other Xlib call.
        try { XInitThreads(); } catch (Exception ex) { _log.Write($"editor: XInitThreads failed: {ex.Message}"); }
    }

    public bool IsSupported => true;
    public string UnsupportedReason => _displayFailed ? NoDisplayReason : "";
    public Action? UserClosed { get; set; }
    public int OpenCount => _open.Count;
    public Func<IPluginInstance, long>? ProcessCalls { get; set; }

    private bool EnsureDisplay()
    {
        if (_display != IntPtr.Zero) return true;
        if (_displayFailed) return false;
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            _displayFailed = true;
            _log.Write($"editor: {NoDisplayReason} DISPLAY='{Environment.GetEnvironmentVariable("DISPLAY")}'");
            return false;
        }
        InstallErrorHandler();
        _root = XRootWindow(_display, XDefaultScreen(_display));
        (_wmDelete, _wmProtocols, _netWmName, _utf8) = (Atom(_display, "WM_DELETE_WINDOW"), Atom(_display, "WM_PROTOCOLS"), Atom(_display, "_NET_WM_NAME"), Atom(_display, "UTF8_STRING"));
        (_netActive, _netState, _netAbove) = (Atom(_display, "_NET_ACTIVE_WINDOW"), Atom(_display, "_NET_WM_STATE"), Atom(_display, "_NET_WM_STATE_ABOVE"));
        (_gtkVariant, _wmClass) = (Atom(_display, "_GTK_THEME_VARIANT"), Atom(_display, "WM_CLASS"));
        _log.Write($"editor: X11 display open (fd {XConnectionNumber(_display)})");
        return true;
    }

    public bool Show(IPluginInstance plugin, string title, IntPtr owner, bool dark, bool docked = false, bool onTop = false, Action<int, int>? sized = null)
    {
        if (docked && !_dockedNoted) { _dockedNoted = true; _log.Write("editor: docking is not offered on this system; the editor opens in its own window"); }
        if (!EnsureDisplay()) return false;
        if (_byPlugin.TryGetValue(plugin, out var existing))
        {
            XMapRaised(_display, existing);
            Activate(_display, _root, existing, _netActive);
            XFlush(_display);
            return true;
        }
        if (!plugin.HasEditor) { _log.Write($"editor: {EditorDiagnostics.Describe(plugin)} offers no X11 editor view"); return false; }

        var screen = XDefaultScreen(_display);
        var window = XCreateSimpleWindow(_display, _root, 0, 0, StartSize.W, StartSize.H, 0, XBlackPixel(_display, screen), XBlackPixel(_display, screen));
        if (window == 0) { _log.Write("editor: X11 window not created"); return false; }
        XSelectInput(_display, window, (nint)StructureNotifyMask);
        var protocols = _wmDelete;
        XSetWMProtocols(_display, window, &protocols, 1);
        SetText(_display, window, _netWmName, _utf8, title);
        SetText(_display, window, XaWmName, XaString, Ascii(title));
        SetText(_display, window, _wmClass, XaString, "tabforge-plugin\0TabForge\0");
        if (dark) SetText(_display, window, _gtkVariant, _utf8, "dark");
        if (onTop) SetAtoms(_display, window, _netState, _netAbove);
        // The plug-in attaches from its own X connection: the window must exist on the server first (XSync is the round trip).
        XSync(_display, 0);

        var errorsBefore = Errors.Count;
        var size = plugin.OpenEditor((IntPtr)window);
        if (size is null)
        {
            _log.Write($"editor: {EditorDiagnostics.Describe(plugin)} did not attach to X11 window 0x{window:X}");
            XDestroyWindow(_display, window);
            XFlush(_display);
            return false;
        }
        _open[window] = plugin;
        _byPlugin[plugin] = window;
        if (plugin is Vst3Plugin) _vst3Open++;
        _sizes[window] = size.Value;
        Resize(window, size.Value.Width, size.Value.Height);
        sized?.Invoke(size.Value.Width, size.Value.Height);
        if (plugin is IEditorResizeSource source)
        {
            void OnResize(int w, int h)
            {
                if (!_byPlugin.TryGetValue(plugin, out var win) || _sizes.TryGetValue(win, out var last) && Math.Abs(last.W - w) <= 1 && Math.Abs(last.H - h) <= 1) return;
                _sizes[win] = (w, h);
                Resize(win, w, h);
                if (plugin is IEditorSizeSink sink)
                    try { sink.SetEditorSize(w, h); } catch (Exception ex) { _log.Write($"editor size failed: {ex.Message}"); }
                sized?.Invoke(w, h);
            }
            source.ResizeRequested += OnResize;
            _resizeHandlers[window] = OnResize;
        }
        XMapRaised(_display, window);
        XSync(_display, 0);
        _rates[window] = new EditorRate(title, plugin);
        _log.Write($"editor window x11 0x{window:X} '{title}' {size.Value.Width}x{size.Value.Height} X errors during open: {Errors.Count - errorsBefore}");
        _log.Write($"editor open: '{title}' {EditorDiagnostics.Describe(plugin)} host=x11-floating runtime='{RuntimeInformation.FrameworkDescription}'");
        return true;
    }

    private static string Ascii(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++) if (chars[i] > 126 || chars[i] < 32) chars[i] = '-';
        return new string(chars);
    }

    private void Resize(nint window, int width, int height)
    {
        FixSize(_display, window, width, height);
        XResizeWindow(_display, window, (uint)Math.Max(1, width), (uint)Math.Max(1, height));
        XFlush(_display);
    }

    public void CloseFor(IPluginInstance plugin)
    {
        if (_byPlugin.TryGetValue(plugin, out var window)) Close(window);
    }

    /// <summary>Detaches the view first (the plug-in takes its own window out of ours), then destroys our window. <paramref name="alreadyGone"/>: the server destroyed it already.</summary>
    private void Close(nint window, bool alreadyGone = false)
    {
        if (_open.Remove(window, out var plugin))
        {
            _byPlugin.Remove(plugin);
            if (plugin is Vst3Plugin) _vst3Open--;
            if (plugin is IEditorResizeSource source && _resizeHandlers.Remove(window, out var onResize))
                try { source.ResizeRequested -= onResize; } catch (Exception ex) { _log.Write($"editor resize unsubscribe failed: {ex.Message}"); }
            try { plugin.CloseEditor(); } catch (Exception ex) { _log.Write($"editor close failed: {ex.Message}"); }
        }
        _resizeHandlers.Remove(window);
        _sizes.Remove(window);
        _rates.Remove(window);
        if (_display == IntPtr.Zero || alreadyGone) return;
        XDestroyWindow(_display, window);
        XSync(_display, 0);
    }

    public void CloseAll()
    {
        foreach (var window in _open.Keys.ToList()) Close(window);
        UserClosed = null;
        if (_display != IntPtr.Zero) { XCloseDisplay(_display); _display = IntPtr.Zero; }
    }

    /// <summary>Main thread, ~30 times a second: the plug-ins' editor idle (meters, resize requests) and the diagnostics lines.</summary>
    public void Idle()
    {
        if (Errors.Count != _reportedErrors) { _reportedErrors = Errors.Count; _log.Write($"editor: X errors so far {_reportedErrors} (last code {Errors.LastCode}, request {Errors.LastRequest})"); }
        if (_open.Count == 0) return;
        foreach (var plugin in _open.Values.ToList())
            try { plugin.EditorIdle(); } catch (Exception ex) { _log.Write($"editor idle failed: {ex.Message}"); }
        foreach (var (window, rate) in _rates)
            if (_open.TryGetValue(window, out var shown) && rate.Tick(() => ProcessCalls?.Invoke(shown) ?? -1) is { } line) _log.Write(line);
    }

    /// <summary>One turn of the main loop: our window events, the ~30 Hz idle, then up to 10 ms waiting in the VST3 run loop (its timers and descriptors run there).</summary>
    public bool PumpOnce(ref long lastIdle, Func<bool>? idle)
    {
        DrainEvents();
        if (System.Diagnostics.Stopwatch.GetElapsedTime(lastIdle).TotalMilliseconds >= EditorWindows.IdleIntervalMs)
        {
            lastIdle = System.Diagnostics.Stopwatch.GetTimestamp();
            Idle();
            if (idle is not null && !idle()) return false;
        }
        if (_vst3Open > 0 && Vst3Plugin.RunLoopWait(_display == IntPtr.Zero ? -1 : XConnectionNumber(_display), WaitMs)) return true;
        Thread.Sleep(WaitMs);
        return true;
    }

    private void DrainEvents()
    {
        if (_display == IntPtr.Zero) return;
        XEvent ev;
        while (_display != IntPtr.Zero && XPending(_display) > 0)
        {
            XNextEvent(_display, &ev);
            var type = (int)ev.Slots[0];
            // ClientMessage: window in slot 4, message_type 5, data.l[0] 7. DestroyNotify: the destroyed window in slot 5.
            var window = type == DestroyNotify ? (nint)ev.Slots[5] : (nint)ev.Slots[4];
            if (!_open.ContainsKey(window)) continue;
            if (type == ClientMessage && ev.Slots[5] == _wmProtocols && ev.Slots[7] == _wmDelete) Close(window);
            else if (type == DestroyNotify) Close(window, alreadyGone: true);
            else continue;
            try { UserClosed?.Invoke(); } catch (Exception ex) { _log.Write($"editor closed handler failed: {ex.Message}"); }
        }
    }
}
