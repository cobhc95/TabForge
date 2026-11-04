using System;

namespace TabForge.Avalonia.Diagnostics;

// Owns: the visible owner window a native Windows editor needs during a matrix run (and the title the runner finds).
// Does not own: the editor window itself (Vst2Plugin/Vst3Plugin/the helper) or the screenshot (the runner).
// Tests: none; a missing owner is recorded as a readable editor failure.

#if WINDOWS
using System.Text;
using System.Threading;

internal sealed class MatrixOwner : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private IntPtr _handle;

    public MatrixOwner(string title)
    {
        _thread = new Thread(() =>
        {
            _handle = PluginMatrixNative.CreateWindowExW(0, "STATIC", title, WsOverlappedWindow | WsVisible, 80, 80, 1000, 760,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            _ready.Set();
            while (PluginMatrixNative.GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                PluginMatrixNative.TranslateMessage(ref message);
                PluginMatrixNative.DispatchMessageW(ref message);
            }
        })
        { IsBackground = true, Name = "TabForge matrix owner" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.WaitHandle.WaitOne(5000);
    }

    public IntPtr Handle => _handle;

    public void Dispose()
    {
        // Do not post WM_CLOSE/WM_QUIT or join the owner thread. The thread runs plug-in editor code, so its exit calls
        // DLL_THREAD_DETACH in every loaded plug-in. A plug-in whose DllMain waits for its own threads (Dexed 1.1.0/JUCE 8
        // is one) blocks there, holding the loader lock; the matrix then cannot even write result.json. The owner thread
        // is a background thread and the one-shot matrix process is about to terminate, so leave it running.
        _ready.Dispose();
    }

    private const int WsOverlappedWindow = 0x00CF0000, WsVisible = 0x10000000;
}
#else
internal sealed class MatrixOwner : IDisposable
{
    public MatrixOwner(string title) => Title = title;
    public string Title { get; }
    public IntPtr Handle => IntPtr.Zero;
    public void Dispose() { }
}
#endif
