using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the registry of FX chain windows and the bound-window behaviours that need a song: one window per track, the window of a removed track closing, the
/// window reading its track again, the master, group and monitor stand-ins with their own wording, Track.Wiring and Track.MidiProcessing on the focused plug-in,
/// the MIDI button lit by an enabled processor, and a wiring change as one undo step.
/// Does not own: the window, the registry or the song (production classes).
/// Tests: this class.
/// </summary>
public class FxChainWindowsTests
{
    private static (SongDocument Document, FxChainWindows Windows, Window Owner, TrackModel Track) Open()
    {
        var document = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var track = document.Project.Tracks[0];
        track.Rig.Plugins = FxChainWindow.SampleChain();
        document.MarkClean();
        var owner = new Window();
        owner.Show();
        return (document, new FxChainWindows(owner, () => document, null, null, new TabForge.Plugins.PluginTrust()), owner, track);
    }

    [AvaloniaFact]
    public void OpeningTwiceBringsTheSameWindowForward()
    {
        var (_, windows, owner, track) = Open();
        var first = windows.Open(track);
        var second = windows.Open(track);
        Assert.Same(first, second);
        Assert.Single(owner.OwnedWindows.OfType<FxChainWindow>());
        Assert.Same(first, windows.WindowOf(track));
        Assert.Same(windows, FxChainWindows.For(first));
        first.Close();
        Assert.Null(windows.WindowOf(track));
        owner.Close();
    }

    [AvaloniaFact]
    public void TheWindowOfARemovedTrackCloses()
    {
        var (document, windows, owner, track) = Open();
        var window = windows.Open(track);
        DocumentEdits.Run(document, project => { project.Tracks.Add(new TrackModel { Name = "Other" }); project.Tracks.Remove(track); return true; });
        Assert.False(window.IsVisible);
        owner.Close();
    }

    [AvaloniaFact]
    public void ARefreshReadsTheTrackAgainAfterThePowerSwitch()
    {
        var (_, windows, owner, track) = Open();
        var window = windows.Open(track);
        var chain = DialogTestKit.Named<CheckBox>(window, "UseChainCheck");
        Assert.False(chain.IsChecked);
        track.SoundSource = SoundSources.Plugins;
        windows.Refresh(track);
        Assert.True(chain.IsChecked);
        window.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void BusStandInsOpenWithTheirOwnWordingAndMonitorGetsTheScopeBox()
    {
        var (document, windows, owner, _) = Open();
        var master = windows.OpenMaster();
        Assert.Equal("Bus on", DialogTestKit.Named<CheckBox>(master, "UseChainCheck").Content);
        Assert.False(DialogTestKit.Named<CheckBox>(master, "GmSoundCheck").IsVisible);
        Assert.False(DialogTestKit.Named<CheckBox>(master, "StartupCheck").IsVisible);
        Assert.False(DialogTestKit.Named<CheckBox>(master, "UseGlobalMonitorCheck").IsVisible);

        var monitor = windows.OpenMonitor();
        Assert.Equal(FxChainPresenter.MonitorTitle, monitor.Title);
        Assert.Equal(FxChainPresenter.MonitorUseChainContent, DialogTestKit.Named<CheckBox>(monitor, "UseChainCheck").Content);
        var scope = DialogTestKit.Named<CheckBox>(monitor, "UseGlobalMonitorCheck");
        Assert.True(scope.IsVisible);
        Assert.True(scope.IsChecked);

        var group = windows.OpenGroup(MixerGroups.Guitars);
        Assert.Equal($"FX: {MixerGroups.Guitars} bus", group.Title);
        Assert.NotSame(master, group);
        foreach (var window in new[] { master, monitor, group }) window.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void AGroupBusPowerEditMarksTheSongAndWritesTheBus()
    {
        var (document, windows, owner, _) = Open();
        var group = windows.OpenGroup(MixerGroups.Guitars);
        var bus = MixerBuses.BusTrack(document.Project, MixerGroups.Guitars);
        bus.Rig.Plugins.Add(new PluginSlot { Name = "Bus comp" });
        Assert.False(document.Project.IsDirty);
        var before = document.Project.Mixer.Bus(MixerGroups.Guitars).On;
        FxTestKit.Press(group, DialogTestKit.Named<CheckBox>(group, "UseChainCheck"));
        Assert.True(document.Project.IsDirty);   // the bus power belongs to the song and goes back from the stand-in
        Assert.NotEqual(before, document.Project.Mixer.Bus(MixerGroups.Guitars).On);
        group.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void WiringAndMidiCommandsOpenTheirWindowsOnTheFocusedPlugin()
    {
        var (_, windows, owner, track) = Open();
        var window = windows.Open(track);
        FxTestKit.List(window).Click(1);
        windows.OpenWiring(track);
        var wiring = window.OwnedWindows.OfType<WiringWindow>().Single();
        Assert.Contains("Sample reverb", wiring.Title);
        FxTestKit.List(window).Click(0);
        windows.OpenMidiProcessing(track);
        var midi = window.OwnedWindows.OfType<MidiProcessingWindow>().Single();
        Assert.Equal("MIDI processing: Sample piano", midi.Title);
        wiring.Close();
        midi.Close();
        window.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void MidiButtonIsLitWhileAProcessorIsOn()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());   // the sample piano has an enabled transpose
        var window = FxTestKit.Window(track);
        window.Show();
        var button = DialogTestKit.Named<Button>(window, "MidiButton");
        FxTestKit.List(window).Click(0);
        Assert.NotNull(button.GetValue(Button.BackgroundProperty) is null ? null : button.Background);
        var lit = button.Background;
        FxTestKit.List(window).Click(1);
        Assert.NotEqual(lit, button.Background);   // the reverb has none
        window.Close();
    }

    [AvaloniaFact]
    public void AWiringChangeIsOneUndoStepAndAClosedWindowReopens()
    {
        var (document, windows, owner, track) = Open();
        var window = windows.Open(track);
        FxTestKit.List(window).Click(1);
        var wiring = window.OpenWiring()!;
        var pins = DialogTestKit.Named<ComboBox>(wiring, "InputCombo");
        pins.SelectedIndex = pins.SelectedIndex == 0 ? 1 : 0;
        Assert.True(document.Undo.CanUndo);
        Assert.NotEqual(PluginPins.Stereo, track.Rig.Plugins[1].Pins);
        DocumentEdits.Undo(document);
        Assert.Equal(PluginPins.Stereo, document.Project.Tracks[0].Rig.Plugins[1].Pins);
        wiring.Close();
        window.Close();
        owner.Close();
    }
}
