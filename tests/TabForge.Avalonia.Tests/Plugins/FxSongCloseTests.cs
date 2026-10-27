using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The FX chain windows follow their song: a closed tab takes its track windows with it, and the monitor window reopens on the chain now in use
/// when the monitor switches between the app-wide chain and the song's own.
/// Owns: these checks. Does not own: the registry (FxChainWindows), the host (SongFxChainHost).
/// Tests: this class.
/// </summary>
public class FxSongCloseTests
{
    [AvaloniaFact]
    public void ClosingASongsTabClosesTheChainWindowsOfItsTracks()
    {
        var document = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var track = document.Project.Tracks[0];
        track.Rig.Plugins = FxChainWindow.SampleChain();
        document.MarkClean();
        var owner = new Window();
        owner.Show();
        var windows = new FxChainWindows(owner, () => document, null, null, new TabForge.Plugins.PluginTrust());
        windows.Open(track);
        windows.CloseSong(document);
        Assert.Null(windows.WindowOf(track));
        owner.Close();
    }

    [AvaloniaFact]
    public void TogglingTheMonitorChainReopensTheMonitorWindow()
    {
        var document = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        document.MarkClean();
        var owner = new Window();
        owner.Show();
        var windows = new FxChainWindows(owner, () => document, null, null, new TabForge.Plugins.PluginTrust());
        var before = windows.OpenMonitor();
        new SongFxChainHost(document, null, null, () => owner, null, new TabForge.Plugins.PluginTrust()).SetMonitorUseGlobal(!document.Project.Mixer.MonitorUseGlobal);
        var monitors = owner.OwnedWindows.OfType<FxChainWindow>().Where(w => MixerBuses.IsMonitor(w.Track)).ToList();
        Assert.Single(monitors);
        Assert.NotSame(before, monitors[0]);
        owner.Close();
    }
}
