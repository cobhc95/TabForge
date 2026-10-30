using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Meters;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the proof that the engine's per-track levels reach the track rows' meters while a song plays, also after the rows show another song.
///     The engine is the real client on a shared block; the meters tick on the app's shared tick, as in the window.
/// Does not own: the drawn bars (TrackMeterHorizontalTests), the engine's own levels (EngineTrackMeterTests), the real window (TrackMeterAppWindowTests).
/// Tests: this class.
/// </summary>
public class TrackMeterFeedTests
{
    [AvaloniaFact]
    public void PlayingTracksFeedTheirRowMeters()
    {
        var doc = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var view = new ArrangementView(doc.Project, doc, meterTick: new SharedMeterTick()) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1300, Height = 520, Content = view, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        window.UpdateLayout();

        using var engine = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        using var shared = SharedBlock.Create("tf-meter-feed-" + Guid.NewGuid().ToString("N"));
        engine.AttachForTest(shared, new SynchronizationContext(), doc.Project.Tracks.Count);
        engine.Sync(doc.Project.Tracks, new PluginSettings(), project: doc.Project, owner: doc);
        Assert.NotNull(engine.ReadTrackMeter(doc.Project.Tracks[0], false));   // the sync gives the track its slot

        TrackMeterWiring.Attach(view.TrackMeters, engine, () => new AppSettings(), () => { });
        for (var i = 0; i < doc.Project.Tracks.Count; i++) shared.SetTrackLevels(i, 0.5f, 0.5f, 0.5f, 0.5f, 0.25f, 0.25f, 0, 0);

        AssertRowShowsALevel(view.TrackMeters, 0);
        window.Close();
    }

    [AvaloniaFact]
    public void ShowingAnotherSongFeedsItsRowMeters()
    {
        var first = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var view = new ArrangementView(first.Project, first, meterTick: new SharedMeterTick()) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1300, Height = 520, Content = view, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        window.UpdateLayout();

        using var engine = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        using var shared = SharedBlock.Create("tf-meter-shown-" + Guid.NewGuid().ToString("N"));
        engine.AttachForTest(shared, new SynchronizationContext(), 64);
        TrackMeterWiring.Attach(view.TrackMeters, engine, () => new AppSettings(), () => { });
        engine.Sync(first.Project.Tracks, new PluginSettings(), project: first.Project, owner: first);

        var second = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        view.Show(second);
        engine.Sync(second.Project.Tracks, new PluginSettings(), project: second.Project, owner: second);
        for (var i = 0; i < second.Project.Tracks.Count; i++) shared.SetTrackLevels(engine.SlotOf(second.Project.Tracks[i]), 0.5f, 0.5f, 0.5f, 0.5f, 0.25f, 0.25f, 0, 0);

        AssertRowShowsALevel(view.TrackMeters, 0);
        window.Close();
    }

    [AvaloniaFact]
    public void RowsReloadedOntoANewSongFeedTheirMeters()
    {
        // An undo or a reload puts a new project in the document and re-reads the rows; the arrangement is not shown again.
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var view = new ArrangementView(song.Project, song, meterTick: new SharedMeterTick()) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 1300, Height = 520, Content = view, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        window.UpdateLayout();

        using var engine = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        using var shared = SharedBlock.Create("tf-meter-rows-" + Guid.NewGuid().ToString("N"));
        engine.AttachForTest(shared, new SynchronizationContext(), 64);
        TrackMeterWiring.Attach(view.TrackMeters, engine, () => new AppSettings(), () => { });
        engine.Sync(song.Project.Tracks, new PluginSettings(), project: song.Project, owner: song);

        song.Project = DemoSongFactory.Create();
        view.ReloadRows(song.Project);
        engine.Sync(song.Project.Tracks, new PluginSettings(), project: song.Project, owner: song);
        for (var i = 0; i < song.Project.Tracks.Count; i++) shared.SetTrackLevels(engine.SlotOf(song.Project.Tracks[i]), 0.5f, 0.5f, 0.5f, 0.5f, 0.25f, 0.25f, 0, 0);

        AssertRowShowsALevel(view.TrackMeters, 0);
        window.Close();
    }

    /// <summary>Pumps the UI until the row's meter has taken a level from the engine, then asserts it is not silent.</summary>
    private static void AssertRowShowsALevel(TrackMeterColumn meters, int row)
    {
        var model = ModelAt(meters, row);
        for (var attempt = 0; attempt < 60 && (model is null || model.NumberText == "-inf"); attempt++)
        {
            Thread.Sleep(20);
            Dispatcher.UIThread.RunJobs();
            model = ModelAt(meters, row);
        }
        Assert.NotNull(model);
        Assert.NotEqual("-inf", model!.NumberText);
        Assert.True(model.LeftDb > MasterMeterModel.FloorDb);
    }

    /// <summary>The model of a row, through the column's own song (the one its rows are drawn from).</summary>
    private static MasterMeterModel? ModelAt(TrackMeterColumn column, int row)
    {
        var models = (Dictionary<TrackModel, MasterMeterModel>)typeof(TrackMeterColumn).GetField("_models", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(column)!;
        var song = (SongProject)typeof(TrackMeterColumn).GetField("_song", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(column)!;
        return row < song.Tracks.Count && models.TryGetValue(song.Tracks[row], out var model) ? model : null;
    }
}
