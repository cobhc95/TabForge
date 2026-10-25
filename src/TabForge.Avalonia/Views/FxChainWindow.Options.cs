using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Audio;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the options of the FX chain window: for a track the Options dropdown of its strip (TrackFxStrip), for a bus the plain row: Through chain, GM sound, Auto-switch to GM sound, Auto-load for this instrument, Add as a track on
//   startup, Match pitch automatically with its measured octave and Re-measure, and the bus variants (Bus on, Monitor on, Use for all projects).
// Does not own: the chain list or the plug-in bar (FxChainWindow.Chain.cs, FxChainWindow.Bar.cs), the auto-load and startup rules (AutoChains,
//   StartupTracks), the pitch measuring (AutoPitchMatcher), the edit path (the host).
// Tests: TabForge.Avalonia.Tests.FxChainOptionsTests.
public sealed partial class FxChainWindow
{
    private const int StateCollectMs = 2000;
    private readonly CheckBox _useChain = new() { Content = "Through chain", Name = "UseChainCheck" };
    private readonly CheckBox _midiSound = new() { Content = "GM sound", Name = "GmSoundCheck" };
    private readonly CheckBox _autoGm = new() { Content = "Auto-switch to GM sound when no VST instrument plays", Name = "AutoGmCheck" };
    private readonly CheckBox _autoLoad = new() { Content = "Auto-load for this instrument", Name = "AutoLoadCheck" };
    private readonly CheckBox _startup = new() { Content = "Add as a track on startup", Name = "StartupCheck" };
    private readonly CheckBox _autoPitch = new() { Content = "Match pitch automatically", Name = "MatchPitchCheck" };
    private readonly TextBlock _autoPitchLabel = new() { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Name = "AutoPitchLabel", [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush") };
    private readonly Button _remeasure = Btn("Re-measure", "Measure the instruments' pitch again (silent; waits while the song plays)", "RemeasureButton");
    private readonly CheckBox _useGlobalMonitor = new() { Content = "Use for all projects", Name = "UseGlobalMonitorCheck", IsVisible = false, Margin = new Thickness(16, 0, 0, 0) };

    private readonly Button _optionsButton = Btn("Options (0 on)", "The track's sound options: chain, GM sound, auto-load, startup and pitch matching", "OptionsButton");
    private TrackFxStrip? _strip;
    private AutoPitchMatcher? _pitchMatcher;   // the one subscribed to, so the window leaves the same one it joined

    /// <summary>The strip a track's window shows (null for a bus or an app-wide chain).</summary>
    internal TrackFxStrip? Strip => _strip;

    private IEnumerable<CheckBox> OptionBoxes() => new[] { _useChain, _midiSound, _autoGm, _autoLoad, _startup, _autoPitch };

    private IEnumerable<Control> OptionControls() => new Control[] { _useChain, _midiSound, _autoGm, _autoLoad, _startup, _autoPitch, _autoPitchLabel, _remeasure, _useGlobalMonitor };

    private void BuildOptions()
    {
        ToolTip.SetTip(_useChain, "Play this track through the chain. Off: the track plays through Windows MIDI.");
        ToolTip.SetTip(_midiSound, "On: the track's General MIDI instrument sound plays; with effects and no VST instrument the effects shape it. Off: only a VST instrument sounds.");
        ToolTip.SetTip(_autoGm, "On: GM sound is ticked when no VST instrument plays this track (chain off, instrument bypassed or removed) and unticked when one plays again. A GM sound you untick yourself stays off. Same as the option in Settings > Audio & Plug-ins.");
        ToolTip.SetTip(_autoLoad, "Save this chain (plug-ins, their settings, each plug-in's MIDI processors, wiring and MIDI input) as the default for this instrument type. It is then applied automatically to any track of this instrument type without plug-ins, in any song when loaded, and to new tracks of this type. Untick to remove the default.");
        ToolTip.SetTip(_startup, "Off by default. On: this chain (plug-ins, their settings, wiring and MIDI configuration, volume, audio input and monitor setting) is added as a track, not armed, to every song you open or create. That track is not saved with the song until you untick this. Manage them in Settings > Audio & Plug-ins.");
        ToolTip.SetTip(_autoPitch, "On (default, see Settings > Audio & Plug-ins): each VST instrument's sounding octave is measured silently when it loads or its preset changes, and it is transposed to match the notes. A Transpose MIDI processor still applies on top. Drum tracks are never transposed.");
        ToolTip.SetTip(_useGlobalMonitor, "Ticked (default): this monitoring chain, saved with the app, is used for every project. Unticked: this project uses its own monitoring chain (empty until you add effects), saved with the project. Never rendered or exported either way.");
        _useChain.IsCheckedChanged += (_, _) => { if (!_building && _useChain.IsChecked == (_track.SoundSource != SoundSources.Plugins)) Edit(() => _track.SoundSource = _useChain.IsChecked == true ? SoundSources.Plugins : SoundSources.Midi, show: true); };
        _midiSound.IsCheckedChanged += (_, _) =>
        {
            if (_building || _midiSound.IsChecked == _track.MidiSound) return;
            Edit(() =>
            {
                _track.MidiSound = _midiSound.IsChecked == true;
                _track.MidiSoundAuto = false;
                _track.MidiSoundManualOff = !_track.MidiSound;   // a manual untick is respected by the automatic switch
            });
        };
        _autoGm.IsCheckedChanged += (_, _) =>
        {
            if (_building || _autoGm.IsChecked == _host.PluginSettings.AutoGmSound) return;
            _host.PluginSettings.AutoGmSound = _autoGm.IsChecked == true;
            _host.SaveSettings();
            Edit(() => { });   // applies the switch now
        };
        _autoLoad.IsCheckedChanged += async (_, _) => { if (!_building) await SaveDefaultAsync(_autoLoad, on => on ? AutoChainSave : AutoChainRemove, "auto-load chain"); };
        _startup.IsCheckedChanged += async (_, _) => { if (!_building) await SaveDefaultAsync(_startup, on => on ? StartupSave : StartupRemove, "startup track"); };
        _autoPitch.IsCheckedChanged += (_, _) => { if (!_building) SetPitchMatching(_autoPitch.IsChecked == true); };
        _remeasure.Click += (_, _) => Remeasure();
        _useGlobalMonitor.IsCheckedChanged += (_, _) => { if (!_building && _useGlobalMonitor.IsChecked != _host.MonitorUseGlobal) _host.SetMonitorUseGlobal(_useGlobalMonitor.IsChecked == true); };
        _pitchMatcher = _host.Engine?.AutoPitch;
        if (_pitchMatcher is not null) _pitchMatcher.Changed += OnPitchChanged;
        _host.SettingsChanged += OnSettingsChanged;   // Preferences can change Auto-GM, the defaults and the monitor choice while the window is open
        ApplyBusVariant();
    }

    /// <summary>A track's strip with its Options dropdown; null for a bus or an app-wide chain, which keep the plain row of options.</summary>
    private TrackFxStrip? BuildTrackStrip() =>
        !_track.IsBus && _host.AppWideChainTitle is null ? _strip = new TrackFxStrip(_host, _track, BuildOptionsDropdown()) : null;

    // The six boxes move into the dropdown's popup (changes apply at once, as in the row); the button counts the ticked ones.
    private Button BuildOptionsDropdown()
    {
        var popup = new StackPanel { Name = "OptionsPopup", Spacing = ChromeMetrics.LiveStripGap };
        foreach (var box in OptionBoxes())
        {
            box.IsCheckedChanged += (_, _) => ShowOptionCount();
            if (!ReferenceEquals(box, _autoPitch)) popup.Children.Add(box);
        }
        var pitch = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ChromeMetrics.LiveStripGap };
        foreach (var part in new Control[] { _autoPitch, _remeasure, _autoPitchLabel }) pitch.Children.Add(part);
        popup.Children.Add(pitch);
        _optionsButton.Flyout = new Flyout { Content = popup };
        _optionsButton.WithMargin(0, 0, 0, 0).WithPadding(ChromeMetrics.LiveStripGroupGap, 0);
        _optionsButton.VerticalAlignment = VerticalAlignment.Center;
        _optionsButton.VerticalContentAlignment = VerticalAlignment.Center;
        _optionsButton.FontSize = ChromeMetrics.LiveStripFontSize;
        _optionsButton.Height = _optionsButton.MinHeight = ChromeMetrics.LiveStripControlHeight;
        return _optionsButton;
    }

    private void ShowOptionCount() => _optionsButton.Content = $"Options ({OptionBoxes().Count(box => box.IsChecked == true)} on)";

    private void AutoChainSave() => AutoChains.Save(_host.PluginSettings, _track);
    private void AutoChainRemove() => AutoChains.Remove(_host.PluginSettings, _track);
    private void StartupSave() => StartupTracks.Save(_host.PluginSettings, _track);
    private void StartupRemove() => StartupTracks.Remove(_host.PluginSettings, _track);

    // Plug-in states are awaited (never a blocking wait on the UI thread); the box is re-read afterwards, so a quick untick wins.
    private async System.Threading.Tasks.Task SaveDefaultAsync(CheckBox box, Func<bool, Action> action, string what)
    {
        try
        {
            if (box.IsChecked == true)
            {
                if (_host.Engine is { } engine) await engine.CollectStatesAsync(new[] { _track }, StateCollectMs);
                if (_closed || box.IsChecked != true) return;
            }
            action(box.IsChecked == true)();
            _host.SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Trace.Error(Trace.Ui, what + ": save: " + ex.Message);
            _status.Text = $"The {what} could not be saved: {ex.Message}";
        }
    }

    // A bus (group, master, monitor) has effects on summed audio: no MIDI sound, no per-instrument defaults, its own wording for the chain switch.
    private void ApplyBusVariant()
    {
        if (!_track.IsBus && _host.AppWideChainTitle is null) return;
        foreach (var hidden in new Control[] { _startup, _autoPitch, _autoPitchLabel, _remeasure, _midiSound, _autoGm, _autoLoad }) hidden.IsVisible = false;
        _useChain.Content = FxChainPresenter.BusUseChainContent;
        ToolTip.SetTip(_useChain, FxChainPresenter.BusUseChainToolTip);
        if (MixerBuses.IsMonitor(_track))
        {
            Title = FxChainPresenter.MonitorTitle;
            _useChain.Content = FxChainPresenter.MonitorUseChainContent;
            ToolTip.SetTip(_useChain, FxChainPresenter.MonitorUseChainToolTip);
            _useGlobalMonitor.IsVisible = true;
        }
        if (_host.AppWideChainTitle is { } appWide)
        {
            Title = appWide;
            foreach (var control in OptionControls()) control.IsVisible = false;
        }
    }

    // ---------- pitch matching ----------

    private void SetPitchMatching(bool on)
    {
        var matcher = _host.Engine?.AutoPitch;
        Edit(() => _track.Rig.AutoPitchMatch = on == _host.PluginSettings.AutoPitchMatch ? null : on);
        if (matcher is null) return;
        matcher.Apply(_track);
        if (on) foreach (var slot in _track.Rig.Plugins) if (slot.Type == PluginSlotType.Instrument && slot.AutoPitchOffset is null) matcher.Measure(_track, slot);
    }

    private void Remeasure()
    {
        if (_host.Engine?.AutoPitch is not { } matcher) return;
        foreach (var slot in _track.Rig.Plugins) if (slot.Type == PluginSlotType.Instrument && slot.Plays) matcher.Measure(_track, slot);
    }

    private void OnSettingsChanged(AppSettings settings) { if (!_closed) ShowOptions(); }

    private void OnPitchChanged(TrackModel track) { if (ReferenceEquals(track, _track)) ShowPitchLabel(); }

    private void ShowPitchLabel() => _autoPitchLabel.Text = _host.Engine?.AutoPitch?.Describe(_track) ?? "";

    /// <summary>Reads every option box from the track and the settings (the track changed elsewhere: the power switch, an undo, a load).</summary>
    private void ShowOptions()
    {
        _building = true;
        try
        {
            _useChain.IsChecked = _track.SoundSource == SoundSources.Plugins;
            _midiSound.IsChecked = _track.MidiSound;
            _autoGm.IsChecked = _host.PluginSettings.AutoGmSound;
            _autoLoad.IsChecked = AutoChains.Has(_host.PluginSettings, _track);
            _startup.IsChecked = StartupTracks.Has(_host.PluginSettings, _track);
            var drums = _track.IsDrumTrack;
            _autoPitch.IsEnabled = !drums;
            _autoPitch.IsChecked = !drums && (_host.Engine?.AutoPitch?.Enabled(_track) ?? (_track.Rig.AutoPitchMatch ?? _host.PluginSettings.AutoPitchMatch));
            _remeasure.IsEnabled = _host.Engine?.AutoPitch is not null && !drums;
            _useGlobalMonitor.IsChecked = _host.MonitorUseGlobal;
        }
        finally { _building = false; }
        ShowPitchLabel();
        ShowOptionCount();
        _strip?.Show();
    }
}
