using System;
using System.Collections.Generic;
using System.Linq;

namespace TabForge.Avalonia.Platform;

// Owns: the XDND (X11 drag and drop) target state machine: Enter / Position / Leave / Drop in, Status / Finished out, the offered-type check, and
//   the hand-over of the dropped paths. Pure: every X call goes through IXdndHost, so it is tested with a fake.
// Does not own: the Xlib calls and the thread (X11FileDrop, X11Api), what a drop does (the window's file drop).
// Tests: TabForge.Avalonia.Tests.XdndTargetTests.
internal enum XdndKind { Enter, Position, Leave, Drop }

/// <summary>One XDND client message: its kind, the five 32-bit data words (l[0]..l[4]) and the window the source addressed (the event's window field; 0: unknown).</summary>
internal readonly record struct XdndMessage(XdndKind Kind, nint[] Data, nint Window = 0);

/// <summary>What the state machine needs from X: type names, the status / finished replies, the selection read and the coordinate map.</summary>
internal interface IXdndHost
{
    /// <summary>The type names the source offers (the three in the Enter message, or its XdndTypeList when <paramref name="moreThanThree"/>).</summary>
    IReadOnlyList<string> OfferedTypes(nint source, bool moreThanThree, IReadOnlyList<nint> enterAtoms);
    /// <summary><paramref name="target"/> is the window the source addressed (sent back in l[0]: the source matches its replies on it); 0: the host's own window.</summary>
    void SendStatus(nint source, nint target, bool accept);
    void SendFinished(nint source, nint target, bool accepted);
    /// <summary>Asks the source for <paramref name="type"/> and waits for the data; null when it fails or times out.</summary>
    string? RequestData(nint source, string type, nint time, int timeoutMs);
    /// <summary>Root-screen pixels to window pixels; null when unavailable.</summary>
    (int X, int Y)? ToWindow(int rootX, int rootY);
    void Trace(string line);
}

/// <summary>One drop target. Thread: the X thread only.</summary>
internal sealed class XdndTarget
{
    internal static string[] AcceptedTypes => new[] { "text/uri-list", "x-special/gnome-copied-files" };

    private readonly IXdndHost _host;
    private readonly Action<IReadOnlyList<string>, int, int> _drop;
    private readonly Action<IReadOnlyList<string>, int, int>? _hover;
    private readonly Action? _cancel;
    private readonly Func<bool>? _accepting;
    private IReadOnlyList<string> _paths = Array.Empty<string>();
    private const int EarlyTimeoutMs = 800, DropTimeoutMs = 3000;
    private nint _source;
    private int _version;
    private string? _type;
    private (int X, int Y) _at;
    private int _positions;
    private bool _lastAccept;

    /// <param name="host">The X side.</param>
    /// <param name="drop">Called with the dropped local paths and the window pixel position of the last XdndPosition.</param>
    /// <param name="hover">Called on each XdndPosition once the paths are known (the drop preview); null: no preview.</param>
    /// <param name="cancel">Called when the drag ends without a drop, and before a drop (clears the preview).</param>
    /// <param name="accepting">Whether the window would take the drag at the last hovered point (the status reply); null: always.</param>
    public XdndTarget(IXdndHost host, Action<IReadOnlyList<string>, int, int> drop, Action<IReadOnlyList<string>, int, int>? hover = null, Action? cancel = null, Func<bool>? accepting = null)
    {
        _host = host;
        _drop = drop;
        _hover = hover;
        _cancel = cancel;
        _accepting = accepting;
    }

    /// <summary>The negotiated type of the drag in progress (null: not accepting).</summary>
    public string? AcceptedType => _type;

    public void Handle(XdndMessage message)
    {
        var d = message.Data;
        switch (message.Kind)
        {
            case XdndKind.Enter:
                _source = d[0];
                _version = (int)((d[1] >> 24) & 0xFF);
                var types = _host.OfferedTypes(_source, (d[1] & 1) != 0, new[] { d[2], d[3], d[4] });
                _type = AcceptedTypes.FirstOrDefault(t => types.Contains(t));
                _host.Trace($"xdnd enter source=0x{_source:x} target=0x{message.Window:x} v{_version} types=[{string.Join(",", types)}] -> {(_type is null ? "rejected: no file list offered" : "accepting " + _type)}");
                // Ask for the paths now so the hover can preview where the files land; a source that is slow to answer just means no preview.
                _paths = _type is null ? Array.Empty<string>() : DroppedFileUris.Parse(_host.RequestData(_source, _type, 0, EarlyTimeoutMs));
                _host.Trace($"xdnd early paths=[{string.Join(" | ", _paths)}]");
                break;
            case XdndKind.Position:
                if (d[0] != _source) return;
                var rootX = (int)(d[2] >> 16) & 0xFFFF;
                var rootY = (int)d[2] & 0xFFFF;
                if (_host.ToWindow(rootX, rootY) is { } at) _at = at;
                if (_type is not null && _paths.Count > 0) _hover?.Invoke(_paths, _at.X, _at.Y);
                var accept = _type is not null && (_accepting?.Invoke() ?? true);
                if (++_positions % 10 == 1 || accept != _lastAccept)
                    _host.Trace($"xdnd position #{_positions} root=({rootX},{rootY}) window=({_at.X},{_at.Y}) paths={_paths.Count} hover={(_hover is not null && _paths.Count > 0)} accept={accept} -> status to 0x{_source:x} target=0x{message.Window:x} (flags accept|all-motion, empty rect)");
                _lastAccept = accept;
                _host.SendStatus(_source, message.Window, accept);
                break;
            case XdndKind.Leave:
                _host.Trace("xdnd leave");
                if (_type is not null) _cancel?.Invoke();
                Reset();
                break;
            case XdndKind.Drop:
                OnDrop(d, message.Window);
                break;
        }
    }

    // Every path ends in XdndFinished (a source without it keeps its drag icon on screen).
    private void OnDrop(nint[] d, nint window)
    {
        var source = _source;
        var type = _type;
        var early = _paths;
        if (type is not null) _cancel?.Invoke();
        Reset();
        if (d[0] != source || type is null)
        {
            _host.Trace($"xdnd drop rejected (source match={d[0] == source}, type={type ?? "none"})");
            _host.SendFinished(d[0], window, false);
            return;
        }
        var paths = DroppedFileUris.Parse(_host.RequestData(source, type, d[2], DropTimeoutMs));
        if (paths.Count == 0) paths = early;
        _host.Trace($"xdnd drop paths=[{string.Join(" | ", paths)}] -> {(paths.Count > 0 ? "accepted" : "rejected: nothing readable")}; finished to 0x{source:x} target=0x{window:x}");
        _host.SendFinished(source, window, paths.Count > 0);
        if (paths.Count > 0) _drop(paths, _at.X, _at.Y);
    }

    private void Reset()
    {
        _source = 0;
        _positions = 0;
        _type = null;
        _paths = Array.Empty<string>();
    }
}
