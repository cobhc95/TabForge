using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks that every change in the FX chain window is one undo step on the song with the dirty mark and the engine call: Remove, Clear, the
/// bypass tick, Through chain, GM sound, the volume knob, and that the window follows its track when it changes elsewhere.
/// Does not own: the window, the host or the document (production classes).
/// Tests: this class.
/// </summary>
public class FxChainEditsTests
{
    private static (SongDocument Document, FxChainWindow Window, TrackModel Track, FxTestKit.FakeEngine Engine) Open()
    {
        var (document, host, track) = FxTestKit.Song();
        var engine = new FxTestKit.FakeEngine();
        var window = new FxChainWindow(new EngineHost(host, engine), track);
        window.Show();
        return (document, window, track, engine);
    }

    /// <summary>The song host with an engine that records: the edit path is the real one.</summary>
    private sealed class EngineHost : IFxChainHost
    {
        private readonly SongFxChainHost _inner;
        public EngineHost(SongFxChainHost inner, IFxChainEngine engine) { _inner = inner; Engine = engine; }
        public IReadOnlyList<TrackModel> Tracks => _inner.Tracks;
        public TabForge.Services.PluginSettings PluginSettings => _inner.PluginSettings;
        public TabForge.Services.HotkeySettings Hotkeys => _inner.Hotkeys;
        public IFxChainEngine? Engine { get; }
        public TabForge.Platform.IPluginFileTrust PluginFiles => _inner.PluginFiles;
        public TabForge.Plugins.PluginTrust Trust => _inner.Trust;
        public UndoSnapshot? Checkpoint() => _inner.Checkpoint();
        public void Edit(TrackModel track, System.Action change, UndoSnapshot? since = null) => _inner.Edit(track, change, since);
        public void SaveSettings() => _inner.SaveSettings();
        public void SaveSettingsQuietly() => _inner.SaveSettingsQuietly();
        public void OpenAudioSettings(Window owner) { }
        public bool MonitorUseGlobal => _inner.MonitorUseGlobal;
        public void SetMonitorUseGlobal(bool useGlobal) => _inner.SetMonitorUseGlobal(useGlobal);
        public void SyncEngine(TrackModel track) => _inner.SyncEngine(track);
        public string? AppWideChainTitle => null;
        public Control? AppWideChainNote => null;
    }

    [AvaloniaFact]
    public void RemoveIsOneUndoStepThatMarksTheSongChanged()
    {
        var (document, window, _, _) = Open();
        Assert.False(document.Undo.CanUndo);
        FxTestKit.List(window).Click(1);
        FxTestKit.PressButton(window, "RemoveButton");
        Assert.True(document.Undo.CanUndo);
        Assert.True(document.Project.IsDirty);
        Assert.Equal(2, document.Project.Tracks[0].Rig.Plugins.Count);
        DocumentEdits.Undo(document);
        Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample delay" }, document.Project.Tracks[0].Rig.Plugins.Select(p => p.Name));
        Assert.False(document.Undo.CanUndo);
        window.Close();
    }

    [AvaloniaFact]
    public void BypassTickIsUndoableAndTellsTheEngine()
    {
        var (document, window, track, engine) = Open();
        FxTestKit.List(window).Click(1);
        FxTestKit.Press(window, DialogTestKit.Named<CheckBox>(window, "BypassCheck"));
        Assert.False(track.Rig.Plugins[1].Enabled);
        Assert.Contains("enabled:Sample reverb:False", engine.Calls);
        Assert.True(document.Undo.CanUndo);
        DocumentEdits.Undo(document);
        Assert.True(document.Project.Tracks[0].Rig.Plugins[1].Enabled);
        window.Close();
    }

    [AvaloniaFact]
    public void ClearChainIsOneStepAndClosesTheEditors()
    {
        var (document, window, _, engine) = Open();
        FxTestKit.Choose(window, "Clear chain");
        Assert.Empty(document.Project.Tracks[0].Rig.Plugins);
        Assert.Equal(0, FxTestKit.List(window).Count);
        DocumentEdits.Undo(document);
        Assert.Equal(3, document.Project.Tracks[0].Rig.Plugins.Count);
        Assert.False(document.Undo.CanUndo);
        window.Close();
    }

    [AvaloniaFact]
    public void ThroughChainAndGmSoundWriteTheTrack()
    {
        var (document, window, track, _) = Open();
        var chain = DialogTestKit.Named<CheckBox>(window, "UseChainCheck");
        Assert.Equal(SoundSources.Midi, track.SoundSource);
        FxTestKit.Press(window, chain);
        Assert.Equal(SoundSources.Plugins, track.SoundSource);
        FxTestKit.Press(window, DialogTestKit.Named<CheckBox>(window, "GmSoundCheck"));
        Assert.False(track.MidiSound);
        Assert.True(track.MidiSoundManualOff);
        DocumentEdits.Undo(document);
        DocumentEdits.Undo(document);
        Assert.Equal(SoundSources.Midi, document.Project.Tracks[0].SoundSource);
        window.Close();
    }

    [AvaloniaFact]
    public void VolumeDragIsOneUndoStepWithLightEngineMessagesWhileDragging()
    {
        var (document, window, track, engine) = Open();
        FxTestKit.List(window).Click(1);
        var knob = DialogTestKit.Named<KnobControl>(window, "VolumeSlider");
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var start = knob.TranslatePoint(new Point(knob.Bounds.Width / 2, knob.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        // Small steps inside the knob: the travel drag (a long move would turn it by angle instead).
        for (var step = 1; step <= 4; step++) window.MouseMove(new Point(start.X, start.Y - 2 * step));
        Assert.False(document.Undo.CanUndo);                       // nothing on the undo stack while the knob is held
        Assert.Contains(engine.Calls, c => c.StartsWith("gain:Sample reverb"));
        window.MouseUp(new Point(start.X, start.Y - 8), MouseButton.Left);
        Assert.True(document.Undo.CanUndo);
        Assert.NotEqual(0, track.Rig.Plugins[1].OutputDb);
        DocumentEdits.Undo(document);
        Assert.False(document.Undo.CanUndo);
        Assert.Equal(0, document.Project.Tracks[0].Rig.Plugins[1].OutputDb);
        window.Close();
    }

    [AvaloniaFact]
    public void WindowFollowsItsTrackWhenTheSwitchChangesElsewhere()
    {
        var (_, window, track, _) = Open();
        var chain = DialogTestKit.Named<CheckBox>(window, "UseChainCheck");
        Assert.False(chain.IsChecked);
        track.SoundSource = SoundSources.Plugins;
        window.RefreshFromTrack();
        Assert.True(chain.IsChecked);
        track.Rig.Plugins.RemoveAt(0);
        window.RefreshFromTrack();
        Assert.Equal(2, FxTestKit.List(window).Count);
        window.Close();
    }

    [AvaloniaFact]
    public void EachEditReachesTheHostsEditPathOnce()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var edits = 0;
        var window = FxTestKit.Window(track, FxTestKit.Host(track, changed: _ => edits++));
        window.Show();
        FxTestKit.List(window).Click(0);
        FxTestKit.PressButton(window, "RemoveButton");
        Assert.Equal(1, edits);
        FxTestKit.Choose(window, "Clear chain");
        Assert.Equal(2, edits);
        window.Close();
    }
}
