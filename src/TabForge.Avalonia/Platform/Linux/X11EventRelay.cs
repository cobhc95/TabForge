using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace TabForge.Avalonia.Platform;

// Owns: handing the XDND client messages that arrive on Avalonia's own X connection to our drop target. A source sends them with XSendEvent to the
//   window it found, and X delivers such an event only to the client that created that window. GTK4 (4.14 gdk_x11_drag_find_surface) uses an
//   XdndProxy only for the first motion and then talks to the real window, so the main window must take the messages itself: this wraps Avalonia's
//   event handler of that window (AvaloniaX11Platform.Windows, Avalonia 11.3 internals, found by reflection) and re-sends each XDND message, unchanged,
//   to our window through Avalonia's display; the drop thread reads it there in arrival order. Every other event goes to Avalonia as before.
// Does not own: the protocol (XdndTarget), our X connection and thread (X11FileDrop).
// Tests: none (needs Avalonia's X11 backend); checked in the Linux VM with a GDK-style test source (the trace shows "relay: on").
internal static class X11EventRelay
{
    /// <summary>
    /// Wraps Avalonia's handler of <paramref name="handle"/>; <paramref name="destination"/> gets each client message type and returns the window to
    /// re-send it to (0: Avalonia keeps it). Null when installed, else why not (then the caller keeps the XdndProxy route). UI thread only.
    /// </summary>
    public static string? TryInstall(Window window, nint handle, Func<nint, nint> destination)
    {
        try
        {
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var impl = typeof(TopLevel).GetProperty("PlatformImpl", Any)?.GetValue(window);
            var platform = impl?.GetType().GetField("_platform", Any)?.GetValue(impl);
            if (platform?.GetType().GetProperty("Windows", Any)?.GetValue(platform) is not IDictionary windows) return "no Avalonia X11 window table";
            if (!windows.Contains(handle) || windows[handle] is not Delegate inner) return "window not in the X11 window table";
            var parameters = inner.GetType().GetMethod("Invoke")?.GetParameters();
            if (parameters is not { Length: 1 } || !parameters[0].ParameterType.IsByRef) return "unexpected handler shape " + inner.GetType().Name;
            var eventType = parameters[0].ParameterType.GetElementType()!;
            if (!eventType.IsValueType || Marshal.SizeOf(eventType) < X11Api.OffData + 40) return "unexpected event type " + eventType.Name;
            var relay = Activator.CreateInstance(typeof(Relay<>).MakeGenericType(eventType), inner, destination)!;
            windows[handle] = Delegate.CreateDelegate(inner.GetType(), relay, relay.GetType().GetMethod(nameof(Relay<int>.Handle))!);
            return null;
        }
        catch (Exception ex) { return $"{ex.GetType().Name}: {ex.Message}"; }
    }

    private sealed class Relay<T> where T : struct
    {
        private delegate void Handler(ref T ev);
        private readonly Handler _inner;
        private readonly Func<nint, nint> _destination;

        public Relay(Delegate inner, Func<nint, nint> destination)
        {
            _inner = (Handler)Delegate.CreateDelegate(typeof(Handler), inner.Target, inner.Method);
            _destination = destination;
        }

        public void Handle(ref T ev)
        {
            var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref ev, 1));
            if (MemoryMarshal.Read<int>(bytes) == X11Api.ClientMessage && _destination(MemoryMarshal.Read<nint>(bytes[X11Api.OffMessageType..])) is var to && to != 0)
            {
                try
                {
                    var display = MemoryMarshal.Read<nint>(bytes[X11Api.OffDisplay..]);   // Avalonia's connection, on its own thread
                    X11Api.XSendEvent(display, to, 0, 0, ref MemoryMarshal.GetReference(bytes));
                    X11Api.XFlush(display);
                }
                catch (Exception ex) { X11FileDrop.Log($"relay failed: {ex.GetType().Name}: {ex.Message}"); }
                return;   // an XDND message: Avalonia has no use for it
            }
            _inner(ref ev);
        }
    }
}
