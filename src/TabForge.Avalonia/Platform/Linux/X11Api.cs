using System;
using System.Runtime.InteropServices;

namespace TabForge.Avalonia.Platform;

// Owns: the libX11 / libc entry points the XDND drop target uses (our own connection; no new native dependency) and the XEvent byte offsets (LP64).
// Does not own: the protocol (XdndTarget) or the thread (X11FileDrop).
internal static class X11Api
{
    private const string Lib = "libX11.so.6";

    internal const int ClientMessage = 33, SelectionNotify = 31, PropertyNotify = 28;
    internal const nint XaAtom = 4, XaWindow = 33;
    internal const long PropertyChangeMask = 1L << 22;
    internal const int EventSize = 192;

    // XClientMessageEvent / XSelectionEvent / XPropertyEvent field offsets on LP64.
    internal const int OffDisplay = 24, OffWindow = 32, OffMessageType = 40, OffFormat = 48, OffData = 56;
    internal const int OffSelProperty = 56, OffPropAtom = 40, OffPropState = 56;

    [DllImport(Lib)] internal static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(Lib)] internal static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] internal static extern nint XDefaultRootWindow(IntPtr display);
    [DllImport(Lib)] internal static extern int XConnectionNumber(IntPtr display);
    [DllImport(Lib)] internal static extern nint XCreateSimpleWindow(IntPtr display, nint parent, int x, int y, uint width, uint height, uint border, nint borderColor, nint background);
    [DllImport(Lib)] internal static extern int XDestroyWindow(IntPtr display, nint window);
    [DllImport(Lib)] internal static extern nint XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyIfExists);
    [DllImport(Lib)] internal static extern IntPtr XGetAtomName(IntPtr display, nint atom);
    [DllImport(Lib)] internal static extern int XFree(IntPtr data);
    [DllImport(Lib)] internal static extern int XChangeProperty(IntPtr display, nint window, nint property, nint type, int format, int mode, ref nint data, int count);
    [DllImport(Lib)] internal static extern int XSelectInput(IntPtr display, nint window, long mask);
    [DllImport(Lib)] internal static extern int XPending(IntPtr display);
    [DllImport(Lib)] internal static extern int XNextEvent(IntPtr display, IntPtr ev);
    [DllImport(Lib)] internal static extern int XSendEvent(IntPtr display, nint window, int propagate, long mask, IntPtr ev);
    [DllImport(Lib)] internal static extern int XSendEvent(IntPtr display, nint window, int propagate, long mask, ref byte ev);
    [DllImport(Lib)] internal static extern int XFlush(IntPtr display);
    [DllImport(Lib)] internal static extern int XConvertSelection(IntPtr display, nint selection, nint target, nint property, nint requestor, nint time);
    [DllImport(Lib)] internal static extern int XGetWindowProperty(IntPtr display, nint window, nint property, nint offset, nint length, int delete, nint reqType,
        out nint actualType, out int actualFormat, out nint nitems, out nint bytesAfter, out IntPtr data);
    [DllImport(Lib)] internal static extern int XTranslateCoordinates(IntPtr display, nint src, nint dest, int x, int y, out int destX, out int destY, out nint child);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd { public int Fd; public short Events; public short Revents; }

    [DllImport("libc", SetLastError = true)] internal static extern int poll(ref PollFd fds, ulong count, int timeoutMs);
}
