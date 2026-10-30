using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Audio;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Meters;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the proof, in the real main window with the real engine process (Null driver), that a playing song's track rows show their levels:
///     while the demo song plays, some track row's number is not "-inf".
/// Does not own: the engine's own levels (EngineTrackMeterTests), the drawing of the bars (TrackMeterHorizontalTests), the rows following a new song (TrackMeterFeedTests).
/// Tests: this class.
/// </summary>
[Collection("EngineAudible")]
public class TrackMeterAppWindowTests
{
    [AvaloniaFact]
    public void PlayingSongShowsLevelsOnTheTrackRowMeters()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-meter-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var settingsPath = Path.Combine(folder, "settings.json");
        File.WriteAllText(settingsPath, "{\"Plugins\":{\"Driver\":\"Null\"}}");
        using var settings = MainWindowSettings.Open(settingsPath);
        var trust = new TabForge.Plugins.PluginTrust();
        using var client = new AudioEngineClient(trust) { EngineExeForTest = Path.Combine(AppContext.BaseDirectory, "TabForge.exe") };
        var routing = new RoutedMidiOutput(new NullMidiOutput(), client);
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(Dispatcher.UIThread), () => new PlaybackEngine(routing)) { Engine = client, Trust = trust };
        var song = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        var window = new MainWindow(settings, new TabWindowRegistry(new AppServices(trust)), first: song, playback: sessions) { Width = 1800, Height = 1100 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var arrangement = window.GetLogicalDescendants().OfType<ArrangementView>().First();
            arrangement.UpdateLayout();
            Assert.NotNull(arrangement.TrackMeters.Source);

            var session = sessions.For(song)!;
            session.PlayOrPause();
            var clock = Stopwatch.StartNew();
            var shown = false;
            while (clock.Elapsed < TimeSpan.FromSeconds(4) && !shown)
            {
                Thread.Sleep(20);
                Dispatcher.UIThread.RunJobs();
                shown = Enumerable.Range(0, song.Project.Tracks.Count).Any(row => ModelAt(arrangement.TrackMeters, song, row) is { } m && m.NumberText != "-inf");
            }
            var report = string.Join("\n", Enumerable.Range(0, song.Project.Tracks.Count).Select(row =>
                $"{song.Project.Tracks[row].Name}: {ModelAt(arrangement.TrackMeters, song, row)?.NumberText ?? "(no model)"}"));
            session.Stop();
            Assert.True(shown, "no track row shows a level while the song plays:\n" + report);
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    private static MasterMeterModel? ModelAt(TrackMeterColumn column, SongDocument song, int row)
    {
        var models = (Dictionary<TrackModel, MasterMeterModel>)typeof(TrackMeterColumn).GetField("_models", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(column)!;
        return row < song.Project.Tracks.Count && models.TryGetValue(song.Project.Tracks[row], out var model) ? model : null;
    }
}
