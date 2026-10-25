using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Undo of a plug-in chain edit made outside the score (the FX window's DocumentEdits call) still runs the window's after-restore pipeline, which syncs the engine.
/// Owns: this check. Does not own: the pipeline (MainWindow AfterUndoRedo, AfterRestorePipelineTests), the engine sync (AvaloniaPlaybackSessions).
/// Tests: this class.
/// </summary>
public class PluginUndoEngineSyncTests
{
    [AvaloniaFact]
    public void PluginChainUndoSyncsEngine()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new AvaloniaPlaybackSessionTests.FakeEngine()) { Engine = client };
        var window = new MainWindow(first: song, playback: sessions);
        window.Show();
        window.UpdateLayout();
        var armed = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        armed.RecordArm = true;
        song.Project.Tracks.Add(armed);
        var timeline = window.GetLogicalDescendants().OfType<ArrangementView>().First().Timeline;
        Assert.True(DocumentEdits.Run(song, project => { project.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "Plate", Type = PluginSlotType.Effect }); return true; }).Changed);
        timeline.SelectedClip = new TimelineClipSelection(song.Project.Tracks[0], new AudioClip { File = "gone.wav", Name = "gone" });
        var syncs = 0;
        client.ArmSentForTest = (_, _, _, _) => syncs++;   // a sync pushes the armed audio track's state to the engine
        Assert.NotNull(DocumentEdits.Undo(song));
        Assert.True(syncs > 0, "the undo synced the engine");
        Assert.Null(timeline.SelectedClip);   // the pipeline ran (it syncs the engine first, then clears the stale clip selection)
        Assert.Empty(song.Project.Tracks[0].Rig.Plugins);
        song.MarkClean();
        window.Close();
    }
}
