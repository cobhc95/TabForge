using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.AudioEngine.Plugins.Clap;

// Owns: the CLAP editor (clap.gui, embedded in a window the caller hands over: HWND on Windows) and its resize handshake.
// Does not own: the window itself (IPluginEditorHost); macOS (cocoa) and Linux (x11) parents are passed through, but no editor host exists there yet, so no editor opens.
// Tests: ClapHostTests (fake gui extension).
public sealed unsafe partial class ClapPlugin
{
    private bool _editorOpen;
    private int _pendingWidth, _pendingHeight;
    private volatile bool _resizePending;
    private bool? _hasEditor;

    /// <summary>The window API name CLAP uses on this system.</summary>
    internal static string WindowApi => OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsMacOS() ? "cocoa" : "x11";

    public bool HasEditor
    {
        get
        {
            if (_disposed || _plugin == null || _gui == null) return false;
            byte* api = stackalloc byte[8];
            WriteApi(api);
            return _hasEditor ??= _gui->IsApiSupported(_plugin, api, 0) != 0;
        }
    }

    private static void WriteApi(byte* destination)
    {
        var name = Encoding.ASCII.GetBytes(WindowApi + "\0");
        for (var i = 0; i < name.Length; i++) destination[i] = name[i];
    }

    public event Action<int, int>? ResizeRequested;

    public (int Width, int Height)? OpenEditor(IntPtr parent)
    {
        if (_editorOpen || !HasEditor || parent == IntPtr.Zero) return null;
        PumpMainThread();
        uint width = 0, height = 0;
        byte* api = stackalloc byte[8];
        WriteApi(api);
        if (_gui->Create(_plugin, api, 0) == 0) return null;
        if (_gui->GetSize(_plugin, &width, &height) == 0) { width = 640; height = 480; }
        var window = new ClapWindow { Api = api, Handle = (void*)parent };
        if (_gui->SetParent(_plugin, &window) == 0) { _gui->Destroy(_plugin); return null; }
        _gui->Show(_plugin);
        _editorOpen = true;
        _resizePending = false;
        return (Math.Max(100, (int)Math.Min(width, 16384u)), Math.Max(60, (int)Math.Min(height, 16384u)));
    }

    public void CloseEditor()
    {
        if (!_editorOpen) return;
        _editorOpen = false;
        if (_disposed || _plugin == null || _gui == null) return;
        _gui->Hide(_plugin);
        _gui->Destroy(_plugin);
    }

    internal void NoteResizeRequested(int width, int height)
    {
        _pendingWidth = width;
        _pendingHeight = height;
        _resizePending = true;
    }

    /// <summary>Main thread, about 30 times a second while the editor is open.</summary>
    public void EditorIdle()
    {
        PumpMainThread();
        if (!_editorOpen || !_resizePending) return;
        _resizePending = false;
        ResizeRequested?.Invoke(Math.Max(100, _pendingWidth), Math.Max(60, _pendingHeight));
    }

    /// <summary>The window got this size (after a resize request or the user): tells the plug-in when it can resize.</summary>
    public void SetEditorSize(int width, int height)
    {
        if (!_editorOpen || _disposed || _plugin == null || _gui == null || _gui->CanResize(_plugin) == 0) return;
        uint w = (uint)Math.Max(1, width), h = (uint)Math.Max(1, height);
        if (_gui->AdjustSize(_plugin, &w, &h) == 0) { w = (uint)Math.Max(1, width); h = (uint)Math.Max(1, height); }
        _gui->SetSize(_plugin, w, h);
    }
}
