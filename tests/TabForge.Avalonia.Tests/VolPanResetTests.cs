using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the slider defaults declared once (MixerStripPresenter) and read by the track list and the mixer, the track list's reset as one undo step with its live push,
///     a stopped change heard at the next play, and the one gain that CC7 and the engine synth's track level share.
/// Does not own: the live probes of a playing song (VolPanLiveProbeTests) or the mixer's layout (MixerWindowTests).
/// Tests: TimelineInputController.ResetSlider, TrackHeaderColumn.SliderDefaultOf, MixerWindow faders, PlaybackEngine.Start.
/// </summary>
public class VolPanResetTests
{
    [Fact]
    public void TheDeclaredDefaultsAreVolume100AndPanCentre()
    {
        Assert.Equal(100, TrackRowCells.SliderDefault(TrackRowCells.Cell.Volume));
        Assert.Equal(0, TrackRowCells.SliderDefault(TrackRowCells.Cell.Pan));
        Assert.Equal(MixerStripPresenter.MasterVolumeDefault, TrackHeaderColumn.SliderDefaultOf(TrackHeaderColumn.MasterRow, TrackRowCells.Cell.Volume));
        Assert.Equal(64, MixerStripPresenter.PanFromSlider(MixerStripPresenter.PanDefault));
    }

    [AvaloniaFact]
    public void ResettingATrackListSliderIsOneUndoStepAndReachesTheEngineAtOnce()
    {
        var document = Clean();
        var track = document.Project.Tracks[0];
        track.Volume = 40; track.Pan = 100;
        var view = new ArrangementView(document.Project, document);
        var pushes = 0;
        view.Input.TrackLevelChanged += () => pushes++;
        var undo = document.Undo.UndoCount;

        view.Input.ResetSlider(0, TrackRowCells.Cell.Volume);
        Assert.Equal(100, document.Project.Tracks[0].Volume);
        Assert.Equal(undo + 1, document.Undo.UndoCount);
        view.Input.ResetSlider(0, TrackRowCells.Cell.Pan);
        Assert.Equal(64, document.Project.Tracks[0].Pan);
        Assert.Equal(undo + 2, document.Undo.UndoCount);
        Assert.Equal(2, pushes);

        view.Input.ResetSlider(0, TrackRowCells.Cell.Pan);   // already at the default: no step, no push
        Assert.Equal(undo + 2, document.Undo.UndoCount);
        Assert.Equal(2, pushes);
    }

    [AvaloniaFact]
    public void AClickOnTheThumbOfATrackListSliderRecordsNoUndoStep()
    {
        var document = Clean();
        document.Project.Tracks[0].Volume = 40;
        var view = new ArrangementView(document.Project, document);
        var window = new Window { Width = 1300, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        var box = view.Header.CellRect(0, TrackRowCells.Cell.Volume);
        var thumb = new Point(TrackRowCells.SliderThumbCentre(box, 40, TrackHeaderColumn.SliderRangeOf(0, TrackRowCells.Cell.Volume)) + 3, box.Center.Y);
        var at = view.Header.TranslatePoint(thumb, window)!.Value;
        var undo = document.Undo.UndoCount;

        window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left);

        Assert.Equal(40, document.Project.Tracks[0].Volume);
        Assert.Equal(undo, document.Undo.UndoCount);
        window.Close();
    }

    [AvaloniaFact]
    public void ADoubleClickOnATrackListSliderResetsItWithoutStartingADrag()
    {
        var document = Clean();
        document.Project.Tracks[0].Volume = 40;
        var view = new ArrangementView(document.Project, document);
        var window = new Window { Width = 1300, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        var at = view.Header.TranslatePoint(view.Header.CellRect(0, TrackRowCells.Cell.Volume).Center, window)!.Value;

        window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left);

        Assert.Equal(100, document.Project.Tracks[0].Volume);
        window.Close();
    }

    [AvaloniaFact]
    public void ADoubleClickOnAMixerFaderResetsItToTheDeclaredDefault()
    {
        var document = Clean();
        document.Project.Tracks[0].Volume = 40; document.Project.Tracks[0].Pan = 100;
        var window = new MixerWindow(document);
        window.Show();
        window.UpdateLayout();
        var faders = window.GetVisualDescendants().OfType<Slider>().Where(s => s.Name == "Fader").ToList();
        var volume = faders.First(s => (global::Avalonia.Automation.AutomationProperties.GetName(s) ?? "").StartsWith("Track volume"));
        var pan = faders.First(s => (global::Avalonia.Automation.AutomationProperties.GetName(s) ?? "").StartsWith("Track pan"));

        foreach (var slider in new[] { volume, pan })
        {
            var at = slider.TranslatePoint(new Point(slider.Bounds.Width * 0.2, slider.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left);
            window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left);
        }

        Assert.Equal(MixerStripPresenter.VolumeDefault, document.Project.Tracks[0].Volume);
        Assert.Equal(MixerStripPresenter.PanFromSlider(MixerStripPresenter.PanDefault), document.Project.Tracks[0].Pan);
        window.Close();
    }

    [Fact]
    public void AChangeMadeWhileStoppedIsPlayedFromTheFirstNote()
    {
        var song = FullDemoSongFactory.Create();
        var output = new Recorder();
        using var engine = new PlaybackEngine(output);
        song.Tracks[0].Volume = 3; song.Tracks[0].Pan = 5;
        engine.Start(song, new PlaybackOptions(), _ => { }, () => { });
        var channel = ChannelAllocator.Assign(song)[0];
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 15000 && !output.Has(channel, 7, 3)) Thread.Sleep(50);
        engine.Stop();
        Assert.True(output.Has(channel, 7, 3) && output.Has(channel, 10, 5));
    }


    [Fact]
    public void TheChannelVolumeAndTheEngineTrackLevelAreOneGainNotTwo()
    {
        // The engine track chain keeps one level: SetMix (plug-in and audio tracks, and every engine-routed track) and the stream's CC7 write the same
        // value and CC7 is consumed there, never forwarded to the synth, so an engine-routed GM track is scaled once.
        var chain = new TabForge.AudioEngine.Mixing.TrackChain(0, 0, null, System.Array.Empty<TabForge.AudioEngine.Mixing.TrackChain.Effect>(), 512);
        chain.SetMix(100, 64);
        chain.AddEvent(0, 0xB0, 7, 50);
        Assert.Equal(50, chain.GetMix().Volume);
        chain.SetMix(100, 64);
        Assert.Equal(100, chain.GetMix().Volume);
    }

    private static SongDocument Clean()
    {
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        document.MarkClean();
        return document;
    }

    private sealed class Recorder : IMidiOutput
    {
        private readonly List<(int Ch, int Cc, int Value)> _cc = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = System.Array.Empty<MidiOutputDeviceInfo>();
        public bool Has(int ch, int cc, int value) { lock (_cc) return _cc.Contains((ch, cc, value)); }
        public void Send(int deviceId, int status, int data1, int data2)
        {
            if ((status & 0xF0) == 0xB0) lock (_cc) _cc.Add((status & 0x0F, data1, data2));
        }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }
}
