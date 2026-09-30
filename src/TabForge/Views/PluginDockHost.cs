using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TabForge.Views;

/// <summary>
/// A plain native child window inside the FX chain window. The audio engine (another process) parents the selected
/// plug-in's editor into it, so the plug-in's controls appear docked. TabForge never
/// loads plug-in code: it only lends this window.
/// </summary>
public sealed class PluginDockHost : HwndHost
{
    private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int ex, string cls, string? title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);

    public IntPtr Area { get; private set; }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        Area = CreateWindowEx(0, "STATIC", null, WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN, 0, 0, 100, 100, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new HandleRef(this, Area);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
        Area = IntPtr.Zero;
    }
}
