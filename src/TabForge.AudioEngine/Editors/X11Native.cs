using System.Runtime.InteropServices;
using System.Text;

namespace TabForge.AudioEngine.Editors;

// Owns: the few libX11 calls the Linux plug-in editor windows need (libX11.so.6, present on every X11 and XWayland desktop; nothing to build against),
//   the XEvent reading they use, and the process-wide X error handler that keeps a bad request from ending the engine.
// Does not own: the windows (X11EditorHost), the plug-in's own X connection (the plug-in opens and runs it through the VST3 run loop).
// Tests: no dedicated test class (needs an X server).
// Threads: the engine's main thread, except OnXError, which Xlib may call on any thread that hits an error.
internal unsafe static class X11Native
{
    private const string Lib = "libX11.so.6";

    public const int ClientMessage = 33, DestroyNotify = 17;
    public const long StructureNotifyMask = 1L << 17, SubstructureNotifyMask = 1L << 19, SubstructureRedirectMask = 1L << 20;
    public const int PropModeReplace = 0, PMinSize = 1 << 4, PMaxSize = 1 << 5;
    public const nint XaAtom = 4, XaString = 31;

    [StructLayout(LayoutKind.Sequential)]
    public struct XSizeHints
    {
        public nint Flags;
        public int X, Y, Width, Height, MinWidth, MinHeight, MaxWidth, MaxHeight, WidthInc, HeightInc;
        public int MinAspectX, MinAspectY, MaxAspectX, MaxAspectY, BaseWidth, BaseHeight, WinGravity;
    }

    /// <summary>An XEvent: a union of 24 longs. On 64-bit Linux every field of the events read here sits in its own 8-byte slot.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XEvent { public fixed long Slots[24]; }

    [DllImport(Lib)] public static extern int XInitThreads();
    [DllImport(Lib)] public static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(Lib)] public static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] public static extern int XDefaultScreen(IntPtr display);
    [DllImport(Lib)] public static extern nint XRootWindow(IntPtr display, int screen);
    [DllImport(Lib)] public static extern nint XBlackPixel(IntPtr display, int screen);
    [DllImport(Lib)] public static extern nint XCreateSimpleWindow(IntPtr display, nint parent, int x, int y, uint width, uint height, uint border, nint borderPixel, nint background);
    [DllImport(Lib)] public static extern int XDestroyWindow(IntPtr display, nint window);
    [DllImport(Lib)] public static extern int XMapRaised(IntPtr display, nint window);
    [DllImport(Lib)] public static extern int XResizeWindow(IntPtr display, nint window, uint width, uint height);
    [DllImport(Lib)] public static extern int XSelectInput(IntPtr display, nint window, nint mask);
    [DllImport(Lib)] public static extern nint XInternAtom(IntPtr display, byte* name, int onlyIfExists);
    [DllImport(Lib)] public static extern int XSetWMProtocols(IntPtr display, nint window, nint* protocols, int count);
    [DllImport(Lib)] public static extern int XChangeProperty(IntPtr display, nint window, nint property, nint type, int format, int mode, byte* data, int elements);
    [DllImport(Lib)] public static extern void XSetWMNormalHints(IntPtr display, nint window, XSizeHints* hints);
    [DllImport(Lib)] public static extern int XSendEvent(IntPtr display, nint window, int propagate, nint mask, XEvent* ev);
    [DllImport(Lib)] public static extern int XPending(IntPtr display);
    [DllImport(Lib)] public static extern int XNextEvent(IntPtr display, XEvent* ev);
    [DllImport(Lib)] public static extern int XConnectionNumber(IntPtr display);
    [DllImport(Lib)] public static extern int XFlush(IntPtr display);
    [DllImport(Lib)] public static extern int XSync(IntPtr display, int discard);
    [DllImport(Lib)] public static extern IntPtr XSetErrorHandler(IntPtr handler);

    /// <summary>True when an X server is named (DISPLAY: X11, or XWayland on a Wayland desktop) and libX11 loads.</summary>
    public static bool Available()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return false;
        if (!NativeLibrary.TryLoad(Lib, out var handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    }

    public static nint Atom(IntPtr display, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name + "\0");
        fixed (byte* p = bytes) return XInternAtom(display, p, 0);
    }

    /// <summary>Sets a UTF8_STRING (or STRING when <paramref name="type"/> is <see cref="XaString"/>) property: window titles, WM_CLASS, the GTK theme variant.</summary>
    public static void SetText(IntPtr display, nint window, nint property, nint type, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        fixed (byte* p = bytes) XChangeProperty(display, window, property, type, 8, PropModeReplace, p, bytes.Length);
    }

    /// <summary>Sets a 32-bit ATOM list property (format 32 data is an array of C longs).</summary>
    public static void SetAtoms(IntPtr display, nint window, nint property, params nint[] atoms)
    {
        fixed (nint* p = atoms) XChangeProperty(display, window, property, XaAtom, 32, PropModeReplace, (byte*)p, atoms.Length);
    }

    /// <summary>Fixes the window to one size (the plug-in's): the window manager offers no resize border, as on Windows.</summary>
    public static void FixSize(IntPtr display, nint window, int width, int height)
    {
        var hints = new XSizeHints { Flags = PMinSize | PMaxSize, MinWidth = width, MinHeight = height, MaxWidth = width, MaxHeight = height };
        XSetWMNormalHints(display, window, &hints);
    }

    /// <summary>Asks the window manager to raise and focus the window (_NET_ACTIVE_WINDOW, as a pager would).</summary>
    public static void Activate(IntPtr display, nint root, nint window, nint activeAtom)
    {
        var ev = new XEvent();
        ev.Slots[0] = ClientMessage;   // type
        ev.Slots[2] = 1;               // send_event
        ev.Slots[3] = display;
        ev.Slots[4] = window;
        ev.Slots[5] = activeAtom;      // message_type
        ev.Slots[6] = 32;              // format
        ev.Slots[7] = 2;               // data.l[0]: source = pager
        XSendEvent(display, root, 0, (nint)(SubstructureRedirectMask | SubstructureNotifyMask), &ev);
    }

    /// <summary>Counts X errors instead of Xlib's default handler, which ends the process. Read by the host for its log.</summary>
    public sealed class ErrorTally { public int Count, LastCode, LastRequest; }
    public static readonly ErrorTally Errors = new();

    public static void InstallErrorHandler() => XSetErrorHandler((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int>)&OnXError);

    [UnmanagedCallersOnly]
    private static int OnXError(IntPtr display, IntPtr error)
    {
        // XErrorEvent on 64-bit: int type; Display*; XID resourceid; unsigned long serial; unsigned char error_code (offset 32), request_code (33).
        if (error != IntPtr.Zero) { Errors.LastCode = Marshal.ReadByte(error, 32); Errors.LastRequest = Marshal.ReadByte(error, 33); }
        Interlocked.Increment(ref Errors.Count);
        return 0;
    }
}
