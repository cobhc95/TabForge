using System;
using System.Linq;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: floating plug-in windows belonging to their song: hidden when another tab shows, shown again, closed with the song and with a removed plug-in even when no FX window is open.
/// Does not own: the registry (production class).
/// Tests: this class.
/// </summary>
public class FloatingEditorsTests
{
    private static (SongDocument Song, TrackModel Track, PluginSlot Slot, FxTestKit.FakeEngine Engine) Floating()
    {
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var track = song.Project.Tracks[0];
        var slot = new PluginSlot { Name = "Drums" };
        track.Rig.Plugins = new System.Collections.Generic.List<PluginSlot> { slot };
        var engine = new FxTestKit.FakeEngine();
        FloatingEditors.Of(track).Add(slot, song, engine, false);
        return (song, track, slot, engine);
    }

    [Fact]
    public void SwitchingToAnotherSongHidesAndSwitchingBackShows()
    {
        var (a, _, _, engine) = Floating();
        var b = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        FloatingEditors.SongShown(new[] { a, b }, b, IntPtr.Zero, true);
        Assert.Equal(new[] { "close:Drums" }, engine.Calls.Where(c => c.StartsWith("close")));
        FloatingEditors.SongShown(new[] { a, b }, a, IntPtr.Zero, true);
        Assert.Contains("open:Drums:float:False", engine.Calls);
        FloatingEditors.CloseSong(a);
    }

    [Fact]
    public void ClosingTheSongClosesItsFloatingEditor()
    {
        var (a, _, _, engine) = Floating();
        FloatingEditors.CloseSong(a);
        Assert.Contains("close:Drums", engine.Calls);
    }

    [Fact]
    public void RemovingThePluginClosesItsFloatingEditorWithNoFxWindow()
    {
        var (a, track, slot, engine) = Floating();
        track.Rig.Plugins = new System.Collections.Generic.List<PluginSlot>();
        FloatingEditors.Prune(a);
        Assert.Contains("close:Drums", engine.Calls);
        Assert.False(FloatingEditors.Of(track).Contains(slot));
    }
}
