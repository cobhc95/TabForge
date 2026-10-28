using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the Linux/X11 file drop target. Avalonia 11.3 has no XDND support, so the window is never offered a file drag. This opens our own Xlib connection
//   and a window P (XdndAware + XdndProxy = P), marks the main window XdndAware and has X11EventRelay pass the XDND messages Avalonia's connection gets
//   for it on to P; a background thread reads them there, answers (XdndStatus / XdndFinished, l[0] = the window the source addressed), and turns them
//   into dropped paths posted to the UI thread. Without the relay the main window's XdndProxy points at P instead (GTK4 sources then lose the drag).
// Does not own: the protocol state machine (XdndTarget), the Xlib declarations (X11Api), the relay (X11EventRelay), what a drop does (MainWindowFileDrop / MainWindowAudioDrop).
// Tests: TabForge.Avalonia.Tests.XdndTargetTests (the state machine; this class needs a real X server and is verified by hand).
/// <summary>The X11 drop target of one window. Create with <see cref="Attach"/>; every X call stays on its own thread.</summary>
internal sealed class X11FileDrop : IXdndHost, IDisposable
{
    private const int PollSliceMs = 100, SelectionTimeoutMs = 3000;

    private readonly nint _target;
    private readonly Action<IReadOnlyList<string>, int, int> _onDrop;
    private readonly X11DragPreview? _preview;
    private readonly Queue<XdndMessage> _deferred = new();
    private bool _nested;
    private readonly Thread _thread;
    private readonly Dictionary<nint, string> _names = new();
    private volatile bool _stop;
    private bool _relayed;
    private IntPtr _dpy, _ev, _out;
    private nint _root, _proxy, _relayTo;
    private nint _aware, _proxyProp, _enter, _position, _leave, _drop, _status, _finished, _selection, _typeList, _copy, _dataProp, _incr;

    private X11FileDrop(nint target, Action<IReadOnlyList<string>, int, int> onDrop, X11DragPreview? preview)
    {
        _target = target;
        _onDrop = onDrop;
        _preview = preview;
        _thread = new Thread(Run) { IsBackground = true, Name = "TabForge X11 drop" };
    }

    /// <summary>Starts the drop target for <paramref name="window"/> once it is open and stops it when it closes; <paramref name="drop"/> gets the paths and the window point (DIPs) on the UI thread; <paramref name="hover"/> (true: the window would take them) and <paramref name="clearHover"/> draw and clear the drop preview. Does nothing off Linux.</summary>
    public static void Attach(Window window, Action<IReadOnlyList<string>, Point> drop, Func<IReadOnlyList<string>, Point, bool>? hover = null, Action? clearHover = null)
    {
        if (!OperatingSystem.IsLinux()) return;
        X11FileDrop? target = null;
        window.Opened += (_, _) =>
        {
            try
            {
                var handle = window.TryGetPlatformHandle();
                if (handle is null || handle.Handle == IntPtr.Zero || handle.HandleDescriptor != "XID")
                {
                    Log($"no X11 window id ({handle?.HandleDescriptor ?? "no handle"}); file drop from the file manager is unavailable");
                    return;
                }
                var preview = hover is null || clearHover is null ? null : new X11DragPreview(window, hover, clearHover);
                target = new X11FileDrop(handle.Handle, (paths, x, y) =>
                {
                    var scale = window.RenderScaling <= 0 ? 1 : window.RenderScaling;
                    Log($"post to UI thread: {paths.Count} path(s) at window pixel ({x},{y}), scale {scale:0.##}");
                    AvaloniaUiThread.ForApplication().Post(() =>
                    {
                        try
                        {
                            Log($"UI thread: running drop handler at ({x / scale:0},{y / scale:0})");
                            drop(paths, new Point(x / scale, y / scale));
                        }
                        catch (Exception ex) { Log($"UI thread: drop handler failed: {ex.GetType().Name}: {ex.Message}"); }
                    });
                }, preview);
                var why = X11EventRelay.TryInstall(window, handle.Handle, target.RelayTarget);
                target._relayed = why is null;
                Log(why is null ? "relay: on (XDND messages for the window come through Avalonia's connection)" : $"relay: off ({why}); using XdndProxy only");
                target._thread.Start();
            }
            catch (Exception ex) { Log($"start failed: {ex.GetType().Name}: {ex.Message}"); }
        };
        window.Closed += (_, _) => target?.Dispose();
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(1000);
    }

    internal static void Log(string line)
    {
        Trace.Write(Trace.Ui, "x11drop: " + line);   // never throws
    }

    void IXdndHost.Trace(string line) => Log(line);

    // ---- the X thread ----

    private void Run()
    {
        try
        {
            _dpy = X11Api.XOpenDisplay(IntPtr.Zero);
            if (_dpy == IntPtr.Zero) { Log("XOpenDisplay failed (no X server / DISPLAY); file drop unavailable"); return; }
            _ev = Marshal.AllocHGlobal(X11Api.EventSize);
            _out = Marshal.AllocHGlobal(X11Api.EventSize);
            _root = X11Api.XDefaultRootWindow(_dpy);
            _proxy = X11Api.XCreateSimpleWindow(_dpy, _root, -10, -10, 1, 1, 0, 0, 0);
            InternAtoms();
            X11Api.XSelectInput(_dpy, _proxy, X11Api.PropertyChangeMask);
            Volatile.Write(ref _relayTo, _proxy);   // after the atoms: the relay reads both from the UI thread
            Announce(_proxy, true);
            // With the relay the window takes the messages itself; an XdndProxy would make a GTK4 source leave after its first motion.
            Announce(_target, !_relayed);
            X11Api.XFlush(_dpy);
            Log($"ready: proxy=0x{_proxy:x} window=0x{_target:x} window XdndProxy={(_relayed ? "none (relay)" : "proxy")}");

            var machine = new XdndTarget(this, _onDrop, _preview is null ? null : _preview.Hover, _preview is null ? null : _preview.Cancel, _preview is null ? null : () => _preview.Accepting);
            while (!_stop)
            {
                var message = _deferred.Count > 0 ? _deferred.Dequeue()
                    : NextEvent(Environment.TickCount64 + PollSliceMs) ? CurrentMessage() : null;
                if (message is null) continue;
                try { machine.Handle(message.Value); }
                catch (Exception ex) { Log($"message failed: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Log($"stopped: {ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            // The main window is not touched here: when it is closed its properties are gone with it (a request to a dead window is an X error).
            try
            {
                Volatile.Write(ref _relayTo, 0);   // the relay stops re-sending before the proxy goes away
                if (_dpy != IntPtr.Zero)
                {
                    if (_proxy != 0) X11Api.XDestroyWindow(_dpy, _proxy);
                    X11Api.XCloseDisplay(_dpy);
                }
                if (_ev != IntPtr.Zero) Marshal.FreeHGlobal(_ev);
                if (_out != IntPtr.Zero) Marshal.FreeHGlobal(_out);
            }
            catch (Exception ex) { Log($"cleanup failed: {ex.GetType().Name}"); }
        }
    }

    private void InternAtoms()
    {
        nint A(string name) => X11Api.XInternAtom(_dpy, name, 0);
        _aware = A("XdndAware"); _proxyProp = A("XdndProxy"); _enter = A("XdndEnter"); _position = A("XdndPosition"); _leave = A("XdndLeave");
        _drop = A("XdndDrop"); _status = A("XdndStatus"); _finished = A("XdndFinished"); _selection = A("XdndSelection"); _typeList = A("XdndTypeList");
        _copy = A("XdndActionCopy"); _dataProp = A("TABFORGE_XDND_DATA"); _incr = A("INCR");
    }

    // XdndAware = 5 on the given window, and with withProxy XdndProxy = the proxy window: sources address their messages to the proxy (our connection).
    private void Announce(nint window, bool withProxy)
    {
        nint version = 5, proxy = _proxy;
        X11Api.XChangeProperty(_dpy, window, _aware, X11Api.XaAtom, 32, 0, ref version, 1);
        if (withProxy) X11Api.XChangeProperty(_dpy, window, _proxyProp, X11Api.XaWindow, 32, 0, ref proxy, 1);
    }

    // The relay's question (UI thread): where an incoming client message of this type goes; 0 for anything that is not an XDND target message.
    private nint RelayTarget(nint messageType)
    {
        var to = Volatile.Read(ref _relayTo);
        return to != 0 && MessageKind(messageType) is not null ? to : 0;
    }

    // The XDND message in _ev, or null when _ev is another event.
    private XdndMessage? CurrentMessage()
    {
        if (Marshal.ReadInt32(_ev) != X11Api.ClientMessage || MessageKind(Marshal.ReadIntPtr(_ev, X11Api.OffMessageType)) is not { } kind) return null;
        var data = new nint[5];
        for (var i = 0; i < 5; i++) data[i] = Marshal.ReadIntPtr(_ev, X11Api.OffData + 8 * i);
        return new XdndMessage(kind, data, Marshal.ReadIntPtr(_ev, X11Api.OffWindow));
    }

    private XdndKind? MessageKind(nint type) =>
        type == _enter ? XdndKind.Enter : type == _position ? XdndKind.Position : type == _leave ? XdndKind.Leave : type == _drop ? XdndKind.Drop : null;

    // Next event into _ev, waiting at most until the deadline (tick count); false on timeout.
    private bool NextEvent(long deadline)
    {
        while (true)
        {
            if (X11Api.XPending(_dpy) > 0)
            {
                X11Api.XNextEvent(_dpy, _ev);
                // While a selection is being read the source keeps sending XDND messages: keep them for the main loop instead of losing them.
                if (_nested && CurrentMessage() is { } message) { _deferred.Enqueue(message); continue; }
                return true;
            }
            var left = deadline - Environment.TickCount64;
            if (left <= 0) return false;
            var fd = new X11Api.PollFd { Fd = X11Api.XConnectionNumber(_dpy), Events = 1 };
            X11Api.poll(ref fd, 1, (int)Math.Min(left, PollSliceMs));
            if (_stop && X11Api.XPending(_dpy) == 0) return false;
        }
    }

    private string NameOf(nint atom)
    {
        if (_names.TryGetValue(atom, out var known)) return known;
        var raw = X11Api.XGetAtomName(_dpy, atom);
        var name = raw == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(raw) ?? "";
        if (raw != IntPtr.Zero) X11Api.XFree(raw);
        return _names[atom] = name;
    }

    // ---- IXdndHost ----

    IReadOnlyList<string> IXdndHost.OfferedTypes(nint source, bool moreThanThree, IReadOnlyList<nint> enterAtoms)
    {
        var atoms = new List<nint>();
        if (moreThanThree)
        {
            if (X11Api.XGetWindowProperty(_dpy, source, _typeList, 0, 65536, 0, 0, out _, out var format, out var count, out _, out var data) == 0 && data != IntPtr.Zero)
            {
                if (format == 32) for (var i = 0; i < (int)count; i++) atoms.Add(Marshal.ReadIntPtr(data, i * 8));
                X11Api.XFree(data);
            }
        }
        else atoms.AddRange(enterAtoms.Where(atom => atom != 0));
        return atoms.Select(NameOf).ToList();
    }

    // l[0] is the window the source addressed (the window field of its message: GDK drops a reply whose l[0] differs), else the main window;
    // the flags ask for every motion (bit 1) with an empty rectangle.
    void IXdndHost.SendStatus(nint source, nint target, bool accept) => SendClient(source, _status, target != 0 ? target : _target, (accept ? 1 : 0) | 2, 0, 0, accept ? _copy : 0);

    void IXdndHost.SendFinished(nint source, nint target, bool accepted) => SendClient(source, _finished, target != 0 ? target : _target, accepted ? 1 : 0, accepted ? _copy : 0, 0, 0);

    (int X, int Y)? IXdndHost.ToWindow(int rootX, int rootY) =>
        X11Api.XTranslateCoordinates(_dpy, _root, _target, rootX, rootY, out var x, out var y, out _) != 0 ? (x, y) : null;

    private void SendClient(nint window, nint messageType, nint l0, nint l1, nint l2, nint l3, nint l4)
    {
        for (var i = 0; i < X11Api.EventSize; i += 8) Marshal.WriteInt64(_out, i, 0);
        Marshal.WriteInt32(_out, 0, X11Api.ClientMessage);
        Marshal.WriteIntPtr(_out, X11Api.OffWindow, window);
        Marshal.WriteIntPtr(_out, X11Api.OffMessageType, messageType);
        Marshal.WriteInt32(_out, X11Api.OffFormat, 32);
        var words = new[] { l0, l1, l2, l3, l4 };
        for (var i = 0; i < 5; i++) Marshal.WriteIntPtr(_out, X11Api.OffData + 8 * i, words[i]);
        X11Api.XSendEvent(_dpy, window, 0, 0, _out);
        X11Api.XFlush(_dpy);
    }

    string? IXdndHost.RequestData(nint source, string type, nint time, int timeoutMs)
    {
        _nested = true;
        try { return RequestDataCore(type, time, timeoutMs); }
        finally { _nested = false; }
    }

    private string? RequestDataCore(string type, nint time, int timeoutMs)
    {
        X11Api.XConvertSelection(_dpy, _selection, X11Api.XInternAtom(_dpy, type, 0), _dataProp, _proxy, (nint)(uint)(long)time);
        X11Api.XFlush(_dpy);
        var deadline = Environment.TickCount64 + timeoutMs;
        while (NextEvent(deadline))
        {
            if (Marshal.ReadInt32(_ev) != X11Api.SelectionNotify) continue;
            if (Marshal.ReadIntPtr(_ev, X11Api.OffSelProperty) == 0) { Log("selection refused by the source"); return null; }
            return ReadSelection();
        }
        Log("selection timed out");
        return null;
    }

    private string? ReadSelection()
    {
        var first = TakeProperty(out var type);
        if (type != _incr) return first is null ? null : System.Text.Encoding.UTF8.GetString(first);
        // INCR: the data arrives in chunks, each announced by a PropertyNotify (new value); an empty chunk ends it.
        using var all = new MemoryStream();
        var deadline = Environment.TickCount64 + SelectionTimeoutMs;
        while (NextEvent(deadline))
        {
            if (Marshal.ReadInt32(_ev) != X11Api.PropertyNotify
                || Marshal.ReadIntPtr(_ev, X11Api.OffWindow) != _proxy
                || Marshal.ReadIntPtr(_ev, X11Api.OffPropAtom) != _dataProp
                || Marshal.ReadInt32(_ev, X11Api.OffPropState) != 0) continue;
            var bytes = TakeProperty(out _);
            if (bytes is null || bytes.Length == 0) return System.Text.Encoding.UTF8.GetString(all.ToArray());
            all.Write(bytes);
            deadline = Environment.TickCount64 + SelectionTimeoutMs;
        }
        Log("INCR selection timed out");
        return null;
    }

    // Reads and deletes the data property on the proxy (8-bit data as bytes); null when it is absent.
    private byte[]? TakeProperty(out nint type)
    {
        if (X11Api.XGetWindowProperty(_dpy, _proxy, _dataProp, 0, 1 << 20, 1, 0, out type, out var format, out var count, out _, out var data) != 0 || data == IntPtr.Zero) return null;
        try
        {
            if (format != 8) return type == _incr ? Array.Empty<byte>() : null;
            var bytes = new byte[(int)count];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { X11Api.XFree(data); }
    }
}
