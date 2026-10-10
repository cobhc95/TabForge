using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TabForge.Controllers;
using TabForge.Playback;

namespace TabForge.Views;

/// <summary>The controls of the metronome, count-in and loop settings popups and their toolbar buttons.</summary>
internal sealed record TransportPopupControls(
    Button MetronomeButton, MenuItem MetronomeMenu, Popup MetronomeSettingsPopup,
    Slider MetronomeVolumeSlider, Slider MetronomeAccentSlider, Slider MetronomeClickSlider,
    TextBlock MetronomeVolumeValue, TextBlock MetronomeAccentValue, TextBlock MetronomeClickValue,
    ComboBox MetronomeSoundCombo, ComboBox MetronomeSubdivisionCombo, CheckBox MetronomeBoostCheck,
    Button CountInButton, MenuItem CountInMenu, Popup CountInSettingsPopup,
    ComboBox CountInBarsCombo, ComboBox CountInSoundCombo, Slider CountInVolumeSlider, TextBlock CountInVolumeValue,
    CheckBox CountInSongStartCheck, CheckBox CountInSectionCheck, CheckBox CountInLoopCheck,
    Button LoopButton, Popup LoopSettingsPopup, TextBlock LoopRangeText, ComboBox LoopScopeCombo,
    CheckBox LoopClearOnDisableCheck, CheckBox LoopButtonSectionCheck, TextBlock LoopCountBadge,
    TextBox LoopCountBox, CheckBox LoopCountInCheck, RadioButton LoopTrainerRadio, RadioButton LoopSimpleRadio,
    TextBox LoopTrainerFromBox, TextBox LoopTrainerToBox, TextBox LoopTrainerStepBox);

/// <summary>What the transport settings popups need from their window.</summary>
internal interface ITransportSettingsHost : IPaneHost
{
    /// <summary>The window's controls exist and its start-up is done (a control's change before that is not the user's).</summary>
    bool IsInitialized { get; }
    /// <summary>The popup controls; read only once <see cref="IsInitialized"/>.</summary>
    TransportPopupControls Controls { get; }
    TransportControlsController Transport { get; }
    /// <summary>The playback engine of the song on show.</summary>
    PlaybackEngine Engine { get; }
}

// Owns: the metronome, count-in and loop settings popups (filling them from the settings, reading them back, saving after a pause),
//   the metronome and count-in toggles and the loops-left badge.
// Does not own: the transport state and what it sends to the engine (TransportControlsController), the loop range (SelectionLoopController),
//   the toggle buttons' look (TransportButtonStyle).
// Tests: TestWindowLifetime, TestPlayDomainFlows.
internal sealed class TransportSettingsController
{
    private readonly ITransportSettingsHost _host;

    public TransportSettingsController(ITransportSettingsHost host) => _host = host;

    private TransportPopupControls? _controls;
    // Null until the window is initialised: the handlers return early, and nothing is cached from the unfilled fields.
    private TransportPopupControls? Controls => _controls ??= _host.IsInitialized ? _host.Controls : null;
    private TransportControlsController Transport => _host.Transport;
    private TabForge.Services.AudioSettings Audio => _host.Settings.Audio;

    // ---- metronome ----

    public void ToggleMetronome()
    {
        if (Controls is not { } c) return;
        Transport.Metronome = !Transport.Metronome;
        c.MetronomeMenu.IsChecked = Transport.Metronome;
        TransportButtonStyle.Apply(c.MetronomeButton, Transport.Metronome);
        Transport.ApplyMetronomeSettingsToEngines();
        _host.SetStatus(Transport.Metronome ? "Metronome on" : "Metronome off");
        _host.SaveSettings();
    }

    public void OpenMetronomeSettings()
    {
        if (Controls is not { } c) return;
        SyncMetronomePopup();
        c.MetronomeSettingsPopup.IsOpen = true;
    }

    public void OnMetronomeSlider(Slider slider)
    {
        if (!_host.IsInitialized || Transport.SyncingMetronomeSettings) return;
        var value = Math.Clamp((int)Math.Round(slider.Value), 0, 100);
        switch (slider.Tag as string)
        {
            case "Master": Audio.MetronomeVolume = value; break;
            case "Accent": Audio.MetronomeAccentVolume = value; break;
            case "Click": Audio.MetronomeClickVolume = value; break;
            default: return;
        }
        SyncMetronomePopup();
        Transport.ApplyMetronomeSettingsToEngines();
        Transport.QueueSettingsSave();
    }

    public void OnMetronomeSound(ComboBox combo)
    {
        if (!_host.IsInitialized || Transport.SyncingMetronomeSettings || combo.SelectedItem is not ComboBoxItem { Tag: string preset }) return;
        if (!Transport.ApplyMetronomePreset(preset)) return;
        Transport.ApplyMetronomeSettingsToEngines();
        _host.SaveSettings();
    }

    public void OnMetronomeSubdivision(ComboBox combo)
    {
        if (!_host.IsInitialized || Transport.SyncingMetronomeSettings || combo.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !int.TryParse(tag, out var subdivisions)) return;
        Audio.MetronomeSubdivision = subdivisions is 1 or 2 or 3 or 4 ? subdivisions : 1;
        Transport.ApplyMetronomeSettingsToEngines();
        _host.SaveSettings();
    }

    public void OnMetronomeBoost(CheckBox box)
    {
        if (!_host.IsInitialized) return;
        Audio.MetronomeBoost = box.IsChecked == true;
        Transport.Preferences.MetronomeBoost = Audio.MetronomeBoost;
        _host.SaveSettings();
    }

    /// <summary>Fills the metronome popup from the audio settings (a no-op before the window's controls exist).</summary>
    public void SyncMetronomePopup()
    {
        if (Controls is not { } c) return;
        Transport.SyncingMetronomeSettings = true;
        var audio = Audio;
        audio.MetronomeSubdivision = audio.MetronomeSubdivision is 1 or 2 or 3 or 4 ? audio.MetronomeSubdivision : 1;
        c.MetronomeVolumeSlider.Value = audio.MetronomeVolume;
        c.MetronomeAccentSlider.Value = audio.MetronomeAccentVolume;
        c.MetronomeClickSlider.Value = audio.MetronomeClickVolume;
        c.MetronomeVolumeValue.Text = $"{audio.MetronomeVolume}%";
        c.MetronomeAccentValue.Text = $"{audio.MetronomeAccentVolume}%";
        c.MetronomeClickValue.Text = $"{audio.MetronomeClickVolume}%";
        c.MetronomeSoundCombo.SelectedIndex =
            audio.MetronomeAccent == 33 && audio.MetronomeClick == 34 ? 0 :
            audio.MetronomeAccent == 34 && audio.MetronomeClick == 33 ? 0 :
            audio.MetronomeAccent == 76 && audio.MetronomeClick == 77 ? 1 :
            audio.MetronomeAccent == 37 && audio.MetronomeClick == 37 ? 2 :
            audio.MetronomeAccent == 39 && audio.MetronomeClick == 39 ? 3 : 4;
        c.MetronomeSubdivisionCombo.SelectedIndex = audio.MetronomeSubdivision - 1;
        Transport.SyncingMetronomeSettings = false;
    }

    // ---- count-in ----

    public void ToggleCountIn()
    {
        if (Controls is not { } c) return;
        Transport.CountIn = !Transport.CountIn;
        c.CountInMenu.IsChecked = Transport.CountIn;
        TransportButtonStyle.Apply(c.CountInButton, Transport.CountIn);
        Transport.Preferences.CountInEachSection = Transport.CountIn && Audio.CountInEachSection;
        SendSectionStarts();
        // Takes effect the next time playback starts from silence; toggling it never interrupts playback.
        if (!_host.Engine.IsPlaying) _host.Engine.UpdateOptions(_host.Project, o => o.CountIn = Transport.CountIn);
    }

    private void SendSectionStarts() =>
        _host.Engine.SetSectionStarts(_host.Project.Markers.Select(marker => marker.MeasureIndex).Where(bar => bar > 0));

    public void OpenCountInSettings()
    {
        if (Controls is not { } c) return;
        Transport.SyncingCountIn = true;
        var audio = Audio;
        c.CountInBarsCombo.SelectedItem = c.CountInBarsCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == audio.CountInBars.ToString()) ?? c.CountInBarsCombo.Items[0];
        c.CountInSoundCombo.SelectedItem = c.CountInSoundCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == audio.CountInSound) ?? c.CountInSoundCombo.Items[0];
        c.CountInVolumeSlider.Value = audio.CountInVolume;
        c.CountInSongStartCheck.IsChecked = audio.CountInOnlyAtSongStart;
        c.CountInSectionCheck.IsChecked = audio.CountInEachSection;
        c.CountInLoopCheck.IsChecked = audio.LoopCountInEachLoop;
        Transport.SyncingCountIn = false;
        c.CountInSettingsPopup.IsOpen = true;
    }

    public void OnCountInVolume(double newValue)
    {
        if (Controls is not { } c) return;
        var value = (int)Math.Round(newValue);
        c.CountInVolumeValue.Text = $"{value}%";
        if (Transport.SyncingMetronomeSettings) return;
        Audio.CountInVolume = value;
        Transport.Preferences.CountInVolume = value;
        Transport.QueueSettingsSave();
    }

    public void OnCountInChecks()
    {
        if (Transport.SyncingCountIn || Controls is not { } c) return;
        Audio.CountInOnlyAtSongStart = c.CountInSongStartCheck.IsChecked == true;
        Audio.CountInEachSection = c.CountInSectionCheck.IsChecked == true;
        Audio.LoopCountInEachLoop = c.CountInLoopCheck.IsChecked == true;
        ApplyLoopBehaviour();
        Transport.Preferences.CountInEachSection = Transport.CountIn && Audio.CountInEachSection;
        _host.SaveSettings();
    }

    public void OnCountInOption()
    {
        if (Transport.SyncingCountIn || Controls is not { } c) return;
        var audio = Audio;
        if (c.CountInBarsCombo.SelectedItem is ComboBoxItem { Tag: string bars } && int.TryParse(bars, out var n))
            audio.CountInBars = n;
        if (c.CountInSoundCombo.SelectedItem is ComboBoxItem { Tag: string sound }) audio.CountInSound = sound;
        Transport.ApplyCountInSound();
        _host.Engine.UpdateOptions(_host.Project, o => o.CountInBars = audio.CountInBars);
        _host.SaveSettings();
    }


    // ---- loop ----
    /// <summary>Fills and opens the loop settings popup; <paramref name="rangeText"/> is the loop area line it shows.</summary>
    public void OpenLoopSettings(string rangeText)
    {
        if (Controls is not { } c) return;
        Transport.SyncingLoopSettings = true;
        c.LoopRangeText.Text = rangeText;
        c.LoopScopeCombo.SelectedItem = c.LoopScopeCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == Audio.LoopDefaultScope) ?? c.LoopScopeCombo.Items[0];
        c.LoopClearOnDisableCheck.IsChecked = Audio.LoopClearAreaOnDisable;
        c.LoopButtonSectionCheck.IsChecked = Audio.LoopButtonLoopsSection;
        SyncLoopControls(c);
        Transport.SyncingLoopSettings = false;
        c.LoopSettingsPopup.IsOpen = true;
    }

    public void OnLoopScope(ComboBox combo)
    {
        if (Transport.SyncingLoopSettings || combo.SelectedItem is not ComboBoxItem { Tag: string scope }) return;
        Audio.LoopDefaultScope = scope;
        _host.SaveSettings();
    }

    public void OnLoopClearOnDisable(CheckBox box)
    {
        Audio.LoopClearAreaOnDisable = box.IsChecked == true;
        _host.SaveSettings();
    }

    public void OnLoopButtonSection(CheckBox box)
    {
        Audio.LoopButtonLoopsSection = box.IsChecked == true;
        _host.SaveSettings();
    }

    /// <summary>A loop count or speed trainer box changed: read the popup, redraw it normalised and send it to the engine.</summary>
    public void OnLoopBehaviour()
    {
        if (Transport.SyncingLoopSettings || Controls is not { } c) return;
        Transport.ReadLoopBehaviour(c.LoopCountBox.Text, c.LoopCountInCheck.IsChecked == true, c.LoopTrainerRadio.IsChecked == true,
            c.LoopTrainerFromBox.Text, c.LoopTrainerToBox.Text, c.LoopTrainerStepBox.Text);
        SyncLoopControls(c);
        ApplyLoopBehaviour();
        _host.SaveSettings();
    }

    private void SyncLoopControls(TransportPopupControls c)
    {
        var audio = Audio;
        var was = Transport.SyncingLoopSettings;
        Transport.SyncingLoopSettings = true;
        c.LoopCountBox.Text = audio.LoopCount > 0 ? audio.LoopCount.ToString() : "∞";
        c.LoopCountInCheck.IsChecked = audio.LoopCountInEachLoop;
        c.LoopTrainerRadio.IsChecked = audio.LoopSpeedTrainer;
        c.LoopSimpleRadio.IsChecked = !audio.LoopSpeedTrainer;
        c.LoopTrainerFromBox.Text = audio.LoopTrainerFrom.ToString();
        c.LoopTrainerToBox.Text = audio.LoopTrainerTo.ToString();
        c.LoopTrainerStepBox.Text = audio.LoopTrainerStep.ToString();
        Transport.SyncingLoopSettings = was;
    }

    /// <summary>The loop settings go to the engine and the loops-left badge follows them.</summary>
    public void ApplyLoopBehaviour()
    {
        Transport.ApplyLoopBehaviour();
        ShowLoopCountBadge(Transport.LoopsCompleted);
    }

    /// <summary>Loops-left badge beside the loop button: it takes its own space, so neighbouring buttons move over.</summary>
    public void ShowLoopCountBadge(int completed)
    {
        Transport.LoopsCompleted = completed;
        if (Controls is not { } c) return;
        var badge = c.LoopCountBadge;
        var total = Audio.LoopCount;
        if (total <= 0) { badge.Visibility = Visibility.Collapsed; return; }
        var left = Math.Max(0, total - completed);
        badge.Text = left.ToString();
        badge.ToolTip = left == 1 ? "1 loop left" : $"{left} loops left (of {total})";
        badge.Visibility = Visibility.Visible;
    }
}
