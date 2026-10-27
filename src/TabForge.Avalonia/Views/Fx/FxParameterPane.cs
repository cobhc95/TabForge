using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the generic parameter panel of one FX chain window as a whole: the panel with its rules, the engine's parameter list asked for while the panel is open (at
//   ParamPollMs, one question at a time, so a slow plug-in is never asked faster than it answers), and the changes sent back to the plug-in.
// Does not own: when the panel shows (FxChainWindow), the controls (FxParameterPanel), the rules (FxParameterModel), the engine (IFxChainEngine).
// Tests: TabForge.Avalonia.Tests.FxParameterTests.
// Threads: UI thread only; the engine events arrive on it.
public sealed class FxParameterPane : IDisposable
{
    /// <summary>Ticks after which an unanswered question is asked again (the answer was lost, or the plug-in is busy).</summary>
    public const int LostAfterTicks = 6;

    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly FxParameterPanel _panel;
    private readonly IUiTimer _timer;
    private PluginSlot? _slot;
    private int _waiting;

    public FxParameterPane(IFxChainHost host, TrackModel track, IUiThread ui)
    {
        (_host, _track) = (host, track);
        _panel = new FxParameterPanel(new FxParameterModel(), (id, value) => { if (_slot is { } slot) _host.Engine?.SetParameter(_track, slot, id, value); });
        _timer = ui.CreateTimer(TimeSpan.FromMilliseconds(ChromeMetrics.ParamPollMs));
        _timer.Tick += (_, _) => Ask();
        if (host.Engine is { } engine) engine.ParametersReceived += OnParameters;
    }

    /// <summary>The panel to place in the window.</summary>
    public FxParameterPanel Panel => _panel;
    /// <summary>The plug-in the panel shows, or null when it is closed.</summary>
    public PluginSlot? Slot => _slot;
    /// <summary>Whether the panel is polling the plug-in.</summary>
    public bool IsPolling => _timer.IsEnabled;

    /// <summary>Shows the parameters of <paramref name="slot"/>: asks for them now, then at the poll rate until <see cref="Close"/>.</summary>
    public void Open(PluginSlot slot)
    {
        if (!ReferenceEquals(_slot, slot)) { _panel.Clear(); _slot = slot; }
        _waiting = 0;
        Ask();
        _timer.Start();
    }

    /// <summary>Stops asking (another row, or the panel was closed); the panel keeps what it shows.</summary>
    public void Close()
    {
        _timer.Stop();
        _slot = null;
    }

    private void Ask()
    {
        if (_slot is not { } slot || _host.Engine is not { IsRunning: true } engine) return;
        if (_waiting > 0 && ++_waiting <= LostAfterTicks) return;   // the last question is still out
        _waiting = 1;
        engine.RequestParameters(_track, slot);
    }

    private void OnParameters(int engineSlot, int index, IReadOnlyList<PluginParameterInfo> list)
    {
        if (_slot is not { } slot || _host.Engine is not { } engine || engineSlot != engine.SlotOf(_track) || index != _track.Rig.Plugins.IndexOf(slot)) return;
        _waiting = 0;
        _panel.Update(list);
    }

    public void Dispose()
    {
        Close();
        if (_host.Engine is { } engine) engine.ParametersReceived -= OnParameters;
    }
}
