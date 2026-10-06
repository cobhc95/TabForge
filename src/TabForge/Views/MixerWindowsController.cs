using System.Windows;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the mixer and FX chain windows need from the main window.</summary>
internal interface IMixerWindowsHost : IPaneHost
{
    IMixerHost MixerHost { get; }
    IFxChainHost FxHost { get; }
    /// <summary>A mixer slider drag ended: the deferred engine sync and refresh follow.</summary>
    void MixerSliderDragEnded();
}

// Owns: the open Mixer window and the open FX chain window of each track: opening or raising them, keeping them in step,
//   closing the ones whose track left the song.
// Does not own: what the windows edit (IMixerHost / IFxChainHost on the window), plug-in hosting (the engine client).
// Tests: TestMixer, TestMixerSliders, TestTrackListFollowsMixerInPlace.
internal sealed class MixerWindowsController
{
    private readonly IMixerWindowsHost _host;
    private MixerWindow? _mixerWindow;
    private readonly Dictionary<TrackModel, FxChainWindow> _fxWindows = new(ReferenceEqualityComparer.Instance);

    public MixerWindowsController(IMixerWindowsHost host) => _host = host;

    /// <summary>The open Mixer window, or null.</summary>
    public MixerWindow? Mixer => _mixerWindow;

    /// <summary>Mixer button / hotkey: opens the mixer, or brings it forward.</summary>
    public void OpenMixer()
    {
        if (_mixerWindow is { IsLoaded: true })
        {
            _mixerWindow.Rebuild();
            _mixerWindow.Activate();
            return;
        }
        _mixerWindow = new MixerWindow(_host.MixerHost, _host.Window);
        _mixerWindow.Closed += (_, _) => _mixerWindow = null;
        _mixerWindow.SliderDragEnded += _host.MixerSliderDragEnded;
        _mixerWindow.Show();
    }

    /// <summary>FX button on a track row / mixer strip / hotkey: that track's chain window.</summary>
    public void OpenFxChain(TrackModel? track)
    {
        if (track is null) return;
        if (_fxWindows.TryGetValue(track, out var open) && open.IsLoaded) { open.Activate(); return; }
        var window = new FxChainWindow(_host.FxHost, track, _host.Window);
        window.Closed += (_, _) => _fxWindows.Remove(track);
        _fxWindows[track] = window;
        window.Show();
    }

    /// <summary>The open chain window of <paramref name="track"/>, or null.</summary>
    public FxChainWindow? FxWindowOf(TrackModel track) => _fxWindows.TryGetValue(track, out var window) ? window : null;

    /// <summary>Closes the chain windows of the tracks <paramref name="close"/> picks.</summary>
    public void CloseFxWindows(Func<TrackModel, bool> close)
    {
        foreach (var (track, window) in _fxWindows.ToList())
            if (close(track)) window.Close();
    }

    /// <summary>Closes FX windows for removed chains while keeping the shared monitor chain attached to the current song.</summary>
    public void CloseStaleFxWindows(SongProject project) => CloseFxWindows(track => !project.Tracks.Contains(track) && !MixerBuses.IsCurrent(project, track)
        && !(MixerBuses.IsMonitor(track) && ReferenceEquals(track.Bus, _host.MixerHost.MonitorChain)));
}
