using System.Runtime.InteropServices;

namespace TabForge.AudioEngine.Plugins;

// Owns: the native entry points of the VST3 shim (tfvst3.dll) and the two user32 DPI calls the editor uses.
// Does not own: when they are called (Vst3Plugin.cs).
public sealed unsafe partial class Vst3Plugin
{
    [DllImport(Lib, CharSet = CharSet.Unicode)] private static extern int tfv3_list_classes(string path, byte* names, int* isInstrument, int max);
    [DllImport(Lib, CharSet = CharSet.Unicode)] private static extern IntPtr tfv3_create(string path, int classIndex, double sampleRate, int maxBlock, byte* error);
    [DllImport(Lib)] private static extern int tfv3_process(IntPtr p, float** input, int inChannels, float** output, int outChannels, int frames, Midi* events, int count, double tempo, double ppq, int playing);
    [DllImport(Lib)] private static extern int tfv3_get_state(IntPtr p, byte* buffer, int capacity);
    [DllImport(Lib)] private static extern int tfv3_set_state(IntPtr p, byte* data, int size);
    [DllImport(Lib)] private static extern int tfv3_has_editor(IntPtr p);
    [DllImport(Lib)] private static extern int tfv3_open_editor(IntPtr p, IntPtr parent, int* width, int* height);
    [DllImport(Lib)] private static extern void tfv3_close_editor(IntPtr p);
    // Editor size handshake; an older tfvst3.dll lacks these (see HasEditorApi): the editor then opens as before and never resizes.
    [DllImport(Lib)] private static extern void tfv3_editor_set_scale(IntPtr p, float scale);
    [DllImport(Lib)] private static extern int tfv3_editor_poll_resize(IntPtr p, int* width, int* height);
    [DllImport(Lib)] private static extern void tfv3_editor_set_size(IntPtr p, int width, int height);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport(Lib)] private static extern int tfv3_latency(IntPtr p);
    [DllImport(Lib)] private static extern void tfv3_destroy(IntPtr p);
    [DllImport(Lib)] private static extern int tfv3_set_offline(IntPtr p, int offline);
    [DllImport(Lib)] private static extern int tfv3_tail(IntPtr p);
    // Newer exports (single-serialise state, live reconfigure, musical position); an older tfvst3.dll lacks them (see HasNewApi).
    [DllImport(Lib)] private static extern int tfv3_process_ex(IntPtr p, float** input, int inChannels, float** output, int outChannels, int frames, Midi* events, int count, double tempo, double ppq, int playing, int timeSigNum, int timeSigDen, double barPositionPpq);
    [DllImport(Lib)] private static extern int tfv3_state_begin(IntPtr p);
    [DllImport(Lib)] private static extern int tfv3_state_copy(IntPtr p, byte* buffer, int capacity);
    [DllImport(Lib)] private static extern int tfv3_set_processing(IntPtr p, double sampleRate, int maxBlock);
    // Live meter / display feed (processor output parameters and messages reach the controller); an older tfvst3.dll lacks these (see HasIdleApi).
    [DllImport(Lib)] private static extern int tfv3_editor_idle(IntPtr p);
    [DllImport(Lib)] private static extern int tfv3_selftest();
}
