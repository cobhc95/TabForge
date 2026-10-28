namespace TabForge.AudioEngine.Editors;

// Owns: the seam between the engine and the window system that shows plug-in editors (Windows: Win32 windows; Linux: X11 windows; macOS: not supported yet).
// Does not own: any one window system (EditorWindows is the Windows host, X11EditorHost the Linux one), what a plug-in draws.
// Tests: PluginEditorHostTests.

/// <summary>Shows plug-in editors in windows of the host system. One per message-loop owner (the engine session, the isolated plug-in host); main thread only.</summary>
public interface IPluginEditorHost
{
    /// <summary>False when this system has no editor host yet; <see cref="Show"/> then returns false and <see cref="UnsupportedReason"/> says why.</summary>
    bool IsSupported { get; }
    string UnsupportedReason { get; }

    /// <summary>Raised when the user closes an editor window.</summary>
    Action? UserClosed { get; set; }
    int OpenCount { get; }

    /// <summary>Diagnostics: how many times a plug-in has been processed so far (-1: not counted); set once by the owner of the audio side, read on the main thread.</summary>
    Func<Plugins.IPluginInstance, long>? ProcessCalls { get => null; set { } }

    /// <summary>Shows the plug-in editor (or brings it forward). False when the plug-in has no editor or it could not be shown.</summary>
    bool Show(Plugins.IPluginInstance plugin, string title, IntPtr owner, bool dark, bool docked = false, bool onTop = false, Action<int, int>? sized = null);
    void CloseFor(Plugins.IPluginInstance plugin);
    void CloseAll();
    void Idle();

    /// <summary>One turn of the hosting message loop; false once <paramref name="idle"/> says the loop should end.</summary>
    bool PumpOnce(ref long lastIdle, Func<bool>? idle);
}

/// <summary>
/// The editor host of macOS (until NSView parenting exists) and of Linux without an X11 display: it reports "editor not supported yet" cleanly,
/// never throws, and keeps the main loop ticking. Plug-ins still load, process and save their state; parameters are the way to edit them.
/// </summary>
public sealed class UnsupportedEditorHost : IPluginEditorHost
{
    public const string Reason = "Plug-in editors are not supported yet on this system.";
    private readonly EngineLogWriter? _log;
    private bool _reported;

    public UnsupportedEditorHost(EngineLogWriter? log = null) { _log = log; }

    public bool IsSupported => false;
    public string UnsupportedReason => Reason;
    public Action? UserClosed { get; set; }
    public int OpenCount => 0;

    public bool Show(Plugins.IPluginInstance plugin, string title, IntPtr owner, bool dark, bool docked = false, bool onTop = false, Action<int, int>? sized = null)
    {
        if (!_reported) { _reported = true; _log?.Write($"editor: {Reason}"); }
        return false;
    }

    public void CloseFor(Plugins.IPluginInstance plugin) { }
    public void CloseAll() { UserClosed = null; }
    public void Idle() { }

    public bool PumpOnce(ref long lastIdle, Func<bool>? idle)
    {
        if (System.Diagnostics.Stopwatch.GetElapsedTime(lastIdle).TotalMilliseconds >= 33)
        {
            lastIdle = System.Diagnostics.Stopwatch.GetTimestamp();
            if (idle is not null && !idle()) return false;
        }
        Thread.Sleep(10);
        return true;
    }
}

/// <summary>Picks the editor host of a system: the Win32 windows on Windows (exactly as before), X11 windows on Linux when an X server is named, else the "not supported yet" host.</summary>
public static class PluginEditorHosts
{
    public static IPluginEditorHost Create(EngineLogWriter log) =>
        OperatingSystem.IsWindows() ? new EditorWindows(log) : X11Native.Available() ? new X11EditorHost(log) : new UnsupportedEditorHost(log);
}
