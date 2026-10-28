using System.Collections.Generic;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the XDND drop target state machine against a fake X host (offered types, status replies, the drop hand-over, rejections).
// Does not own: the Xlib thread (X11FileDrop; needs an X server).
// Tests: this file.
public sealed class XdndTargetTests
{
    private sealed class FakeHost : IXdndHost
    {
        public List<string> Types = new() { "text/plain", "text/uri-list" };
        public string? Data = "file:///home/u/My%20Song.gp5\r\n";
        public List<string> Sent = new();
        public (int X, int Y)? Mapped = (40, 50);
        public IReadOnlyList<string> OfferedTypes(nint source, bool moreThanThree, IReadOnlyList<nint> enterAtoms) => Types;
        public List<nint> Targets = new();
        public void SendStatus(nint source, nint target, bool accept) { Sent.Add("status:" + accept); Targets.Add(target); }
        public void SendFinished(nint source, nint target, bool accepted) { Sent.Add("finished:" + accepted); Targets.Add(target); }
        public string? RequestData(nint source, string type, nint time, int timeoutMs) => Data;
        public (int X, int Y)? ToWindow(int rootX, int rootY) => Mapped;
        public void Trace(string line) { }
    }

    private static XdndMessage Enter(nint source = 7) => new(XdndKind.Enter, new nint[] { source, 5 << 24, 0, 0, 0 });
    private static XdndMessage Position(nint source = 7) => new(XdndKind.Position, new nint[] { source, 0, (100 << 16) | 200, 0, 0 });
    private static XdndMessage Drop(nint source = 7) => new(XdndKind.Drop, new nint[] { source, 0, 123, 0, 0 });

    [Fact]
    public void FullDragAcceptsAndDeliversPathsWithWindowPoint()
    {
        var host = new FakeHost();
        IReadOnlyList<string>? got = null;
        var at = (0, 0);
        var target = new XdndTarget(host, (paths, x, y) => { got = paths; at = (x, y); });
        target.Handle(Enter());
        target.Handle(Position());
        target.Handle(Drop());
        Assert.Equal(new[] { "status:True", "finished:True" }, host.Sent);
        Assert.Equal(new[] { "/home/u/My Song.gp5" }, got);
        Assert.Equal((40, 50), at);
    }

    [Fact]
    public void DragWithoutAFileListIsRefusedAtPositionAndDrop()
    {
        var host = new FakeHost { Types = new() { "text/plain" } };
        var dropped = false;
        var target = new XdndTarget(host, (_, _, _) => dropped = true);
        target.Handle(Enter());
        target.Handle(Position());
        target.Handle(Drop());
        Assert.Equal(new[] { "status:False", "finished:False" }, host.Sent);
        Assert.False(dropped);
    }

    [Fact]
    public void LeaveCancelsAndUnreadableDataIsNotAccepted()
    {
        var host = new FakeHost();
        var dropped = false;
        var target = new XdndTarget(host, (_, _, _) => dropped = true);
        target.Handle(Enter());
        target.Handle(new XdndMessage(XdndKind.Leave, new nint[] { 7, 0, 0, 0, 0 }));
        Assert.Null(target.AcceptedType);
        host.Data = null;
        target.Handle(Enter());
        target.Handle(Drop());
        Assert.Equal(new[] { "finished:False" }, host.Sent);
        Assert.False(dropped);
    }

    [Fact]
    public void HoverGetsTheEarlyPathsAndCancelComesBeforeTheDrop()
    {
        var host = new FakeHost();
        var events = new List<string>();
        var target = new XdndTarget(host, (_, _, _) => events.Add("drop"), (paths, x, y) => events.Add($"hover:{paths[0]}@{x},{y}"), () => events.Add("cancel"), () => false);
        target.Handle(Enter());
        target.Handle(Position());
        target.Handle(Drop());
        Assert.Equal(new[] { "hover:/home/u/My Song.gp5@40,50", "cancel", "drop" }, events);
        Assert.Equal("status:False", host.Sent[0]);   // the window refused the hovered point
    }

    [Fact]
    public void RepliesNameTheWindowTheSourceAddressedAndEveryPositionGetsAStatus()
    {
        // GDK matches XdndStatus / XdndFinished on l[0] == the window it sent to; a mismatch leaves its drag waiting (a stuck drag icon).
        var host = new FakeHost();
        var target = new XdndTarget(host, (_, _, _) => { });
        target.Handle(Enter() with { Window = 0x42 });
        for (var i = 0; i < 12; i++) target.Handle(Position() with { Window = 0x42 });
        target.Handle(Drop() with { Window = 0x42 });
        Assert.Equal(13, host.Sent.Count);
        Assert.Equal("finished:True", host.Sent[^1]);
        Assert.All(host.Targets, t => Assert.Equal((nint)0x42, t));
    }

    [Fact]
    public void PositionFromAnotherSourceIsIgnored()
    {
        var host = new FakeHost();
        var target = new XdndTarget(host, (_, _, _) => { });
        target.Handle(Enter(7));
        target.Handle(Position(9));
        Assert.Empty(host.Sent);
    }
}
