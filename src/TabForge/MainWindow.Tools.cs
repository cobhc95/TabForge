using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TabForge.Controllers;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// MainWindow, tools: check bars, transpose, tuning reference, metronome / count-in / loop settings, chords and scales.
public partial class MainWindow
{
    // ---------- tools ----------

    private void CheckBars_Click(object sender, RoutedEventArgs e)
    {
        // The standard F4. Same rule as the red bar tint (MusicTime.AnalyzeBar over every track), so the report
        // and the score can never disagree.
        var issues = MusicTime.FindBarProblems(_project);
        if (issues.Count == 0)
        {
            MessageBox.Show(this, "Every bar adds up to its time signature.", "Check bars", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "Bars OK";
            return;
        }
        var lines = issues.Take(20).Select(issue => issue.Describe());
        var more = issues.Count > 20 ? $"\n…and {issues.Count - 20} more" : "";
        MessageBox.Show(this, string.Join("\n", lines) + more, "Check bars", MessageBoxButton.OK, MessageBoxImage.Warning);
        StatusText.Text = $"{issues.Count} bar{(issues.Count == 1 ? "" : "s")} to check";
    }

    private void Transpose_Click(object sender, RoutedEventArgs e)
    {
        var t = SelectedTrack; if (t is null) return;
        if (t.IsAudio) { StatusText.Text = Services.EditorGuard.Hint; return; }
        // Percussion numbers are instruments, not pitches: a drum track is never transposed.
        if (t.Kind == TrackKind.Drums || t.MidiChannel == 9) { StatusText.Text = $"{t.Name} is a drum track: drums are not transposed"; return; }
        // With bars/beats selected only those move; otherwise the whole track (both voices).
        var hasSelection = Editor.HasSelection;
        var txt = GpDialogs.Prompt("Transpose", hasSelection ? "Semitones (-12..12) for the selection:" : "Semitones (-12..12) for the whole track:", "0");
        if (txt is null || !int.TryParse(txt, out var st)) return;
        st = Math.Clamp(st, -12, 12);
        if (st == 0) return;
        var range = hasSelection ? Editor.SelectionCellRange : ((int, int, int, int)?)null;
        var unplaced = 0;
        DocumentEdits.Run(Doc, project => { (_, unplaced) = MusicTheoryService.TransposeTrack(t, st, range); return true; });
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Instrument);
        StatusText.Text = $"Transposed {(hasSelection ? "the selection of " : "")}{t.Name} {st:+0;-0} st" +
            (unplaced > 0 ? $" ({unplaced} note{(unplaced == 1 ? "" : "s")} did not fit on a free string and kept their fret)" : "");
    }

    // Tools > Scale finder: likely scales for the selection / song, or any scale, shown on the fretboard.
    private void ScaleFinder_Click(object sender, RoutedEventArgs e) => InstrumentPane.OpenScaleFinder();

    private void Tuner_Click(object sender, RoutedEventArgs e)
    {
        // Chromatic tuner: the engine listens to the armed input; the window follows the selected track's tuning.
        TunerWindow.Open(this, _engine, () => SelectedTrack);
    }

    private void Metronome_Click(object sender, RoutedEventArgs e)
    {
        _transport.Metronome = !_transport.Metronome;
        MetronomeMenu.IsChecked = _transport.Metronome;
        SetTransportActive(MetronomeButton, _transport.Metronome);
        ApplyMetronomeSettingsToEngines();
        StatusText.Text = _transport.Metronome ? "Metronome on" : "Metronome off";
        SaveSettings();
    }

    private void MetronomeButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        SyncMetronomeSettingsPopup();
        MetronomeSettingsPopup.IsOpen = true;
        e.Handled = true;
    }

    private void CloseMetronomeSettings_Click(object sender, RoutedEventArgs e)
        => MetronomeSettingsPopup.IsOpen = false;

    private void MetronomeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_mainWindowInitialized || _transport.SyncingMetronomeSettings || sender is not Slider slider || MetronomeVolumeValue is null) return;
        var value = Math.Clamp((int)Math.Round(slider.Value), 0, 100);
        switch (slider.Tag as string)
        {
            case "Master": _settings.Audio.MetronomeVolume = value; break;
            case "Accent": _settings.Audio.MetronomeAccentVolume = value; break;
            case "Click": _settings.Audio.MetronomeClickVolume = value; break;
            default: return;
        }
        SyncMetronomeSettingsPopup();
        ApplyMetronomeSettingsToEngines();
        QueueMetronomeSettingsSave();
    }

    private void MetronomeSound_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_mainWindowInitialized || _transport.SyncingMetronomeSettings || MetronomeSoundCombo?.SelectedItem is not ComboBoxItem { Tag: string preset }) return;
        if (!_transport.ApplyMetronomePreset(preset)) return;
        ApplyMetronomeSettingsToEngines();
        SaveSettings();
    }

    private void CountInVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_mainWindowInitialized || CountInVolumeValue is null) return;
        var value = (int)Math.Round(e.NewValue);
        CountInVolumeValue.Text = $"{value}%";
        if (_transport.SyncingMetronomeSettings) return;
        _settings.Audio.CountInVolume = value;
        _options.Playback.CountInVolume = value;
        QueueMetronomeSettingsSave();
    }


    private void CountInButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _transport.SyncingCountIn = true;
        var audio = _settings.Audio;
        CountInBarsCombo.SelectedItem = CountInBarsCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == Math.Clamp(audio.CountInBars, 1, 4).ToString()) ?? CountInBarsCombo.Items[0];
        CountInSoundCombo.SelectedItem = CountInSoundCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == audio.CountInSound) ?? CountInSoundCombo.Items[0];
        CountInVolumeSlider.Value = audio.CountInVolume;
        CountInSongStartCheck.IsChecked = audio.CountInOnlyAtSongStart;
        CountInSectionCheck.IsChecked = audio.CountInEachSection;
        CountInLoopCheck.IsChecked = audio.LoopCountInEachLoop;
        _transport.SyncingCountIn = false;
        CountInSettingsPopup.IsOpen = true;
        e.Handled = true;
    }

    private void LoopButtonSection_Click(object sender, RoutedEventArgs e)
    {
        _settings.Audio.LoopButtonLoopsSection = LoopButtonSectionCheck.IsChecked == true;
        SaveSettings();
    }

    private void CountInChecks_Click(object sender, RoutedEventArgs e)
    {
        if (_transport.SyncingCountIn) return;
        _settings.Audio.CountInOnlyAtSongStart = CountInSongStartCheck.IsChecked == true;
        _settings.Audio.CountInEachSection = CountInSectionCheck.IsChecked == true;
        _settings.Audio.LoopCountInEachLoop = CountInLoopCheck.IsChecked == true;
        ApplyLoopBehaviour();
        _options.Playback.CountInEachSection = _transport.CountIn && _settings.Audio.CountInEachSection;
        SaveSettings();
    }

    private void CountInOption_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_mainWindowInitialized || _transport.SyncingCountIn) return;
        var audio = _settings.Audio;
        if (CountInBarsCombo.SelectedItem is ComboBoxItem { Tag: string bars } && int.TryParse(bars, out var n))
            audio.CountInBars = n;
        if (CountInSoundCombo.SelectedItem is ComboBoxItem { Tag: string sound }) audio.CountInSound = sound;
        ApplyCountInSound();
        _midi.UpdateOptions(_project, o => o.CountInBars = audio.CountInBars);
        SaveSettings();
    }

    private void ApplyCountInSound() => _transport.ApplyCountInSound();

    private void LoopCountBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { LoopBehaviour_Changed(sender, e); e.Handled = true; }
    }

    private void LoopBehaviour_Changed(object sender, RoutedEventArgs e)
    {
        if (_transport.SyncingLoopSettings) return;
        _transport.ReadLoopBehaviour(LoopCountBox.Text, LoopCountInCheck.IsChecked == true, LoopTrainerRadio.IsChecked == true,
            LoopTrainerFromBox.Text, LoopTrainerToBox.Text, LoopTrainerStepBox.Text);
        SyncLoopBehaviourControls();
        ApplyLoopBehaviour();
        SaveSettings();
    }

    private void SyncLoopBehaviourControls()
    {
        var audio = _settings.Audio;
        var was = _transport.SyncingLoopSettings;
        _transport.SyncingLoopSettings = true;
        LoopCountBox.Text = audio.LoopCount > 0 ? audio.LoopCount.ToString() : "∞";
        LoopCountInCheck.IsChecked = audio.LoopCountInEachLoop;
        LoopTrainerRadio.IsChecked = audio.LoopSpeedTrainer;
        LoopSimpleRadio.IsChecked = !audio.LoopSpeedTrainer;
        LoopTrainerFromBox.Text = audio.LoopTrainerFrom.ToString();
        LoopTrainerToBox.Text = audio.LoopTrainerTo.ToString();
        LoopTrainerStepBox.Text = audio.LoopTrainerStep.ToString();
        _transport.SyncingLoopSettings = was;
    }

    private void ApplyLoopBehaviour()
    {
        _transport.ApplyLoopBehaviour();
        UpdateLoopCountBadge(_transport.LoopsCompleted);
    }


    // Loops-left badge beside the loop button: it takes its own space, so neighbouring buttons move over.
    private void UpdateLoopCountBadge(int completed)
    {
        _transport.LoopsCompleted = completed;
        var total = _settings.Audio.LoopCount;
        if (total <= 0) { LoopCountBadge.Visibility = Visibility.Collapsed; return; }
        var left = Math.Max(0, total - completed);
        LoopCountBadge.Text = left.ToString();
        LoopCountBadge.ToolTip = left == 1 ? "1 loop left" : $"{left} loops left (of {total})";
        LoopCountBadge.Visibility = Visibility.Visible;
    }

    private void MetronomeBoost_Click(object sender, RoutedEventArgs e)
    {
        if (!_mainWindowInitialized) return;
        _settings.Audio.MetronomeBoost = MetronomeBoostCheck.IsChecked == true;
        _options.Playback.MetronomeBoost = _settings.Audio.MetronomeBoost;
        SaveSettings();
    }

    private void MetronomeSubdivision_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_mainWindowInitialized || _transport.SyncingMetronomeSettings || MetronomeSubdivisionCombo?.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !int.TryParse(tag, out var subdivisions)) return;
        _settings.Audio.MetronomeSubdivision = subdivisions is 1 or 2 or 3 or 4 ? subdivisions : 1;
        ApplyMetronomeSettingsToEngines();
        SaveSettings();
    }

    private void SyncMetronomeSettingsPopup()
    {
        if (!_mainWindowInitialized || MetronomeVolumeSlider is null || MetronomeAccentSlider is null ||
            MetronomeClickSlider is null || MetronomeSoundCombo is null || MetronomeSubdivisionCombo is null ||
            MetronomeVolumeValue is null || MetronomeAccentValue is null || MetronomeClickValue is null) return;
        _transport.SyncingMetronomeSettings = true;
        var audio = _settings.Audio;
        audio.MetronomeVolume = Math.Clamp(audio.MetronomeVolume, 0, 100);
        audio.MetronomeAccentVolume = Math.Clamp(audio.MetronomeAccentVolume, 0, 100);
        audio.MetronomeClickVolume = Math.Clamp(audio.MetronomeClickVolume, 0, 100);
        audio.MetronomeSubdivision = audio.MetronomeSubdivision is 1 or 2 or 3 or 4 ? audio.MetronomeSubdivision : 1;
        MetronomeVolumeSlider.Value = audio.MetronomeVolume;
        MetronomeAccentSlider.Value = audio.MetronomeAccentVolume;
        MetronomeClickSlider.Value = audio.MetronomeClickVolume;
        MetronomeVolumeValue.Text = $"{audio.MetronomeVolume}%";
        MetronomeAccentValue.Text = $"{audio.MetronomeAccentVolume}%";
        MetronomeClickValue.Text = $"{audio.MetronomeClickVolume}%";
        MetronomeSoundCombo.SelectedIndex =
            audio.MetronomeAccent == 33 && audio.MetronomeClick == 34 ? 0 :
            audio.MetronomeAccent == 34 && audio.MetronomeClick == 33 ? 0 :
            audio.MetronomeAccent == 76 && audio.MetronomeClick == 77 ? 1 :
            audio.MetronomeAccent == 37 && audio.MetronomeClick == 37 ? 2 :
            audio.MetronomeAccent == 39 && audio.MetronomeClick == 39 ? 3 : 4;
        MetronomeSubdivisionCombo.SelectedIndex = audio.MetronomeSubdivision - 1;
        _transport.SyncingMetronomeSettings = false;
    }

    private void ApplyMetronomeSettingsToEngines() => _transport.ApplyMetronomeSettingsToEngines();

    private void QueueMetronomeSettingsSave() => _transport.QueueSettingsSave();

    private void CountIn_Click(object sender, RoutedEventArgs e)
    {
        _transport.CountIn = !_transport.CountIn;
        CountInMenu.IsChecked = _transport.CountIn;
        SetTransportActive(CountInButton, _transport.CountIn);
        _options.Playback.CountInEachSection = _transport.CountIn && _settings.Audio.CountInEachSection;
        _midi.SetSectionStarts(_project.Markers.Select(marker => marker.MeasureIndex).Where(bar => bar > 0));
        // Takes effect the next time playback starts from silence; toggling it never interrupts playback.
        if (!_midi.IsPlaying) _midi.UpdateOptions(_project, o => o.CountIn = _transport.CountIn);
    }

    private static void SetTransportActive(Button button, bool active)
    {
        var (fill, accent) = button.Name switch
        {
            "MetronomeButton" => ("PlaybarMetroBrush", Color.FromRgb(0xFF, 0x95, 0x56)),
            "CountInButton" => ("PlaybarBeatBrush", Color.FromRgb(0xC1, 0x9B, 0xFF)),
            _ => ("PlaybarLoopBrush", Color.FromRgb(0x38, 0xE8, 0x89))
        };
        button.Background = active
            ? (Brush)Application.Current.FindResource(fill)
            : (Brush)Application.Current.FindResource("PlaybarSurfaceBrush");
        var light = TabForge.Visualization.VisualTheme.IsLight;
        if (light) accent = Color.FromRgb((byte)(accent.R * 0.62), (byte)(accent.G * 0.62), (byte)(accent.B * 0.62)); // neon reads harsh on grey
        button.BorderBrush = active
            ? new SolidColorBrush(accent)
            : (Brush)Application.Current.FindResource("PlaybarBorderBrush");
        button.Opacity = active || light ? 1.0 : 0.78;
        button.Effect = active
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = accent,
                BlurRadius = light ? 8 : 13,
                ShadowDepth = 0,
                Opacity = light ? 0.35 : 0.86
            }
            : null;
        if (button.Name is "MetronomeButton" or "LoopButton")
        {
            var label = button.Name == "LoopButton" ? "Loop" : "Metronome";
            System.Windows.Automation.AutomationProperties.SetName(button, $"{label} {(active ? "on" : "off")}");
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, active ? "Active" : "Inactive");
        }
        if (button.Content is SvgIconView icon)
            icon.IconColor = active ? accent : Color.FromRgb(0x9A, 0xA6, 0xB2);
    }

    // Tools > Chord finder… and Tools > Song stats…: small windows (Views/ChordFinderWindow, Views/SongStatsWindow).
    private void ChordFinder_Click(object sender, RoutedEventArgs e) => Views.ChordFinderWindow.Show(this, InsertChordName);

    private void SongStats_Click(object sender, RoutedEventArgs e) => Views.SongStatsWindow.Show(this, _project, _currentPath);

    /// <summary>Attaches a chord name to the beat under the cursor; false when there is no cursor cell.</summary>
    private bool InsertChordName(string name)
    {
        var c = Editor.Effects.CurrentCell(); if (c is null) return false;
        DocumentEdits.Run(Doc, _ => { c.ChordName = name; return true; }); RefreshAfterEdit(EditRefresh.Score);
        return true;
    }

    private void LyricsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DocumentViewBinder.CommitLyrics(Doc, LyricsBox.Text)) RefreshAfterEdit(EditRefresh.None);
    }

    /// <summary>The window as the host of its <see cref="TransportControlsController"/>.</summary>
    private sealed class TransportHost : ITransportControlsHost
    {
        private readonly MainWindow _window;
        public TransportHost(MainWindow window) => _window = window;

        public IReadOnlyList<DocumentSession> Documents => _window._documents.Documents;
        public AppSettings Settings => _window._settings;
        public PlaybackPreferences Preferences => _window._options.Playback;
        public void SaveSettings() => _window.SaveSettings();
        public ComboBox? SpeedCombo => _window.SpeedCombo;
        public bool IsInteractive => _window.IsLoaded && !_window._restoring;
        public void SetEngineSpeed(double speed)
        {
            if (_window._midi.IsPlaying) _window._midi.SetSpeed(_window._project, speed);
        }
        public void SetStatus(string text) => _window.StatusText.Text = text;
    }
}
