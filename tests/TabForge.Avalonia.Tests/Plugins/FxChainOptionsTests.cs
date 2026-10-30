using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the options row of the FX chain window: Auto-switch to GM sound (saved in the settings and applied), Auto-load for this instrument and Add as a track on
/// startup (saved with the plug-in states read first, removed when unticked), Match pitch automatically (the chain's own choice, stored against the global default),
/// the startup track saved again when the window closes, and the default chains applied to a song and a new track.
/// Does not own: the saved chains (AutoChains, StartupTracks), the pitch matcher or the settings file.
/// Tests: this class.
/// </summary>
public class FxChainOptionsTests
{
    private static (FxChainWindow Window, TrackModel Track, PluginSettings Settings, FxTestKit.FakeEngine Engine, List<string> Log) Open(bool autoGm = true)
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine();
        var settings = new PluginSettings { AutoGmSound = autoGm };
        var log = new List<string>();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine, settings, saved: () => log.Add("saved")));
        window.Show();
        return (window, track, settings, engine, log);
    }

    private static CheckBox Box(Window window, string name) => DialogTestKit.Named<CheckBox>(window, name);

    [AvaloniaFact]
    public void AutoGmSoundIsASettingNotAWindowChoice()
    {
        var (window, _, settings, _, log) = Open(autoGm: true);
        Assert.True(Box(window, "AutoGmCheck").IsChecked);
        FxTestKit.Press(window, Box(window, "AutoGmCheck"));
        Assert.False(settings.AutoGmSound);
        Assert.Contains("saved", log);
        window.Close();

        var (off, _, offSettings, _, _) = Open(autoGm: false);
        Assert.False(Box(off, "AutoGmCheck").IsChecked);
        off.Close();
    }

    [AvaloniaFact]
    public void AutoLoadSavesTheChainWithItsStatesAndUntickingRemovesIt()
    {
        var (window, track, settings, engine, log) = Open();
        Assert.False(Box(window, "AutoLoadCheck").IsChecked);
        FxTestKit.Press(window, Box(window, "AutoLoadCheck"));
        DialogTestKit.RunQueuedUiWork();
        Assert.Contains("collect", engine.Calls);
        Assert.True(AutoChains.Has(settings, track));
        Assert.Equal(3, AutoChains.Find(settings, track)!.Plugins.Count);
        Assert.NotNull(AutoChains.Find(settings, track)!.Plugins[0].State);   // read from the engine first
        Assert.Contains("saved", log);
        FxTestKit.Press(window, Box(window, "AutoLoadCheck"));
        DialogTestKit.RunQueuedUiWork();
        Assert.False(AutoChains.Has(settings, track));
        window.Close();
    }

    [AvaloniaFact]
    public void StartupTrackIsSavedWhileTickedAndAgainWhenTheWindowCloses()
    {
        var (window, track, settings, engine, _) = Open();
        FxTestKit.Press(window, Box(window, "StartupCheck"));
        DialogTestKit.RunQueuedUiWork();
        Assert.True(StartupTracks.Has(settings, track));
        track.Rig.Plugins.RemoveAt(2);
        engine.Calls.Clear();
        window.Close();
        DialogTestKit.RunQueuedUiWork();
        Assert.Contains("collect", engine.Calls);
        Assert.Equal(2, StartupTracks.Find(settings, track)!.Plugins.Count);   // the template follows the chain as it was left
    }

    [AvaloniaFact]
    public void MatchPitchStoresAnOverrideOnlyWhenItDiffersFromTheGlobalDefault()
    {
        var (window, track, settings, _, _) = Open();
        var box = Box(window, "MatchPitchCheck");
        Assert.True(box.IsChecked);   // the global default is on
        FxTestKit.Press(window, box);
        Assert.False(track.Rig.AutoPitchMatch);
        FxTestKit.Press(window, box);
        Assert.Null(track.Rig.AutoPitchMatch);   // back to the default: nothing stored
        window.Close();
    }

    [AvaloniaFact]
    public void MatchPitchIsOffAndDisabledOnADrumTrack()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        track.Kind = TrackKind.Drums;
        var window = FxTestKit.Window(track);
        window.Show();
        Assert.False(Box(window, "MatchPitchCheck").IsEnabled);
        Assert.False(Box(window, "MatchPitchCheck").IsChecked);
        Assert.False(DialogTestKit.Named<Button>(window, "RemeasureButton").IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void GmSoundIsShownUncheckedAndDisabledWhileSoundEngineIsNone()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        track.MidiSound = true;
        var settings = new PluginSettings { SoundEngine = SoundEngines.None };
        var window = FxTestKit.Window(track, FxTestKit.Host(track, new FxTestKit.FakeEngine(), settings));
        window.Show();
        var box = Box(window, "GmSoundCheck");
        Assert.False(box.IsEnabled);
        Assert.False(box.IsChecked);   // display only: the stored value is kept
        Assert.Equal(FxChainWindow.SoundNoneToolTip, ToolTip.GetTip(box));
        Assert.True(track.MidiSound);
        window.Close();
    }

    [AvaloniaFact]
    public void ADefaultChainIsGivenToASongAndANewTrackWithoutPlugins()
    {
        var settings = new PluginSettings();
        var donor = FxTestKit.Track(FxChainWindow.SampleChain());
        donor.MidiProgram = 29;
        AutoChains.Save(settings, donor);
        var song = new TabForge.Documents.SongDocument(() => null) { Project = TabForge.Documents.SongDocument.BlankProject() };
        var target = song.Project.Tracks[0];
        target.MidiProgram = 29;
        target.Rig.Plugins.Clear();
        song.MarkClean();
        Assert.True(FxDefaultChains.Apply(settings, song));
        Assert.Equal(3, target.Rig.Plugins.Count);
        Assert.Equal(SoundSources.Plugins, target.SoundSource);
        Assert.False(song.HasUnsavedChanges);   // opening a song with a default chain does not make it unsaved

        var fresh = new TrackModel { MidiProgram = 29 };
        Assert.True(AutoChains.Apply(settings, fresh));   // what a new track gets (the track menu and the Add track window call it through the registry)
        Assert.Equal(3, fresh.Rig.Plugins.Count);
    }

    [AvaloniaFact]
    public void StartupTracksAreAddedToASongOnceAndNotSaved()
    {
        var settings = new PluginSettings();
        var donor = FxTestKit.Track(FxChainWindow.SampleChain(), "Live guitar");
        StartupTracks.Save(settings, donor);
        var song = new TabForge.Documents.SongDocument(() => null) { Project = TabForge.Documents.SongDocument.BlankProject() };
        song.MarkClean();
        var before = song.Project.Tracks.Count;
        Assert.True(FxDefaultChains.Apply(settings, song));
        Assert.Equal(before + 1, song.Project.Tracks.Count);
        Assert.False(song.HasUnsavedChanges);
        Assert.False(FxDefaultChains.Apply(settings, song));   // a second time adds nothing
        Assert.Equal(before + 1, song.Project.Tracks.Count);
    }

    [AvaloniaFact]
    public void EngineSetupGivesTheCrashListAndOnePitchMatcher()
    {
        using var engine = new TabForge.Audio.AudioEngineClient(new TabForge.Plugins.PluginTrust());
        var settings = new PluginSettings { Quarantined = new List<string> { "a.dll" } };
        var ui = TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication();
        FxEngineSetup.Attach(engine, settings, ui);
        Assert.Contains("a.dll", engine.Quarantine!());
        var matcher = engine.AutoPitch;
        Assert.NotNull(matcher);
        FxEngineSetup.Attach(engine, settings, ui);
        Assert.Same(matcher, engine.AutoPitch);
    }
}
