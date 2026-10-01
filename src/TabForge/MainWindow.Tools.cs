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
            MessageBox.Show(this, "Every bar adds up to its time signature.", "Check bars (F4)", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "Bars OK";
            return;
        }
        var lines = issues.Take(20).Select(issue => issue.Describe());
        var more = issues.Count > 20 ? $"\n…and {issues.Count - 20} more" : "";
        MessageBox.Show(this, string.Join("\n", lines) + more, "Check bars (F4)", MessageBoxButton.OK, MessageBoxImage.Warning);
        StatusText.Text = $"{issues.Count} bar{(issues.Count == 1 ? "" : "s")} to check";
    }

    private void Transpose_Click(object sender, RoutedEventArgs e)
    {
        var t = SelectedTrack; if (t is null) return;
        // Percussion numbers are instruments, not pitches: a drum track is never transposed.
        if (t.Kind == TrackKind.Drums || t.MidiChannel == 9) { StatusText.Text = $"{t.Name} is a drum track: drums are not transposed"; return; }
        // With bars/beats selected only those move; otherwise the whole track (both voices).
        var hasSelection = Editor.HasSelection;
        var txt = GpDialogs.Prompt("Transpose", hasSelection ? "Semitones (-12..12) for the selection:" : "Semitones (-12..12) for the whole track:", "0");
        if (txt is null || !int.TryParse(txt, out var st)) return;
        st = Math.Clamp(st, -12, 12);
        if (st == 0) return;
        CaptureUndo();
        var range = hasSelection ? Editor.SelectionCellRange : ((int, int, int, int)?)null;
        var (_, unplaced) = MusicTheoryService.TransposeTrack(t, st, range);
        CommitEdit(EditRefresh.Score | EditRefresh.Instrument);
        StatusText.Text = $"Transposed {(hasSelection ? "the selection of " : "")}{t.Name} {st:+0;-0} st" +
            (unplaced > 0 ? $" ({unplaced} note{(unplaced == 1 ? "" : "s")} did not fit on a free string and kept their fret)" : "");
    }

    // Tools > Scale finder: likely scales for the selection / song, or any scale, shown on the fretboard.
    private void ScaleFinder_Click(object sender, RoutedEventArgs e) => OpenScaleFinder();

    /// <summary>Shows (and focuses) the Practice / Mixer panel and scrolls to one of its sections.</summary>
    private void ShowInPracticePanel(FrameworkElement target)
    {
        _dockWorkspace?.SetPanelVisible("practice", true);
        _dockWorkspace?.SelectPanel("practice");
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => target.BringIntoView()));
    }
    private void Tuner_Click(object sender, RoutedEventArgs e)
    {
        // Chromatic tuner: the engine listens to the armed input; the window follows the selected track's tuning.
        TunerWindow.Open(this, Audio.AudioEngineClient.Instance, () => SelectedTrack);
    }

    private void Metronome_Click(object sender, RoutedEventArgs e)
    {
        _metronome = !_metronome;
        MetronomeMenu.IsChecked = _metronome;
        SetTransportActive(MetronomeButton, _metronome);
        ApplyMetronomeSettingsToEngines();
        StatusText.Text = _metronome ? "Metronome on" : "Metronome off";
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
        if (!_mainWindowInitialized || _syncingMetronomeSettings || sender is not Slider slider || MetronomeVolumeValue is null) return;
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
        if (!_mainWindowInitialized || _syncingMetronomeSettings || MetronomeSoundCombo?.SelectedItem is not ComboBoxItem { Tag: string preset }) return;
        switch (preset)
        {
            case "classic":
                _settings.Audio.MetronomeAccent = 33;
                _settings.Audio.MetronomeClick = 34;
                break;
            case "woodblock":
                _settings.Audio.MetronomeAccent = 76;
                _settings.Audio.MetronomeClick = 77;
                break;
            case "sidestick":
                _settings.Audio.MetronomeAccent = 37;
                _settings.Audio.MetronomeClick = 37;
                break;
            case "clap":
                _settings.Audio.MetronomeAccent = 39;
                _settings.Audio.MetronomeClick = 39;
                break;
            case "custom":
                return;
            default:
                return;
        }
        ApplyMetronomeSettingsToEngines();
        SaveSettings();
    }

    private void CountInVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_mainWindowInitialized || CountInVolumeValue is null) return;
        var value = (int)Math.Round(e.NewValue);
        CountInVolumeValue.Text = $"{value}%";
        if (_syncingMetronomeSettings) return;
        _settings.Audio.CountInVolume = value;
        TabForge.Playback.PlaybackEngine.CountInVolume = value;
        QueueMetronomeSettingsSave();
    }

    private bool _syncingCountIn;

    private void CountInButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _syncingCountIn = true;
        var audio = _settings.Audio;
        CountInBarsCombo.SelectedItem = CountInBarsCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == Math.Clamp(audio.CountInBars, 1, 4).ToString()) ?? CountInBarsCombo.Items[0];
        CountInSoundCombo.SelectedItem = CountInSoundCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == audio.CountInSound) ?? CountInSoundCombo.Items[0];
        CountInVolumeSlider.Value = audio.CountInVolume;
        CountInSongStartCheck.IsChecked = audio.CountInOnlyAtSongStart;
        CountInSectionCheck.IsChecked = audio.CountInEachSection;
        CountInLoopCheck.IsChecked = audio.LoopCountInEachLoop;
        _syncingCountIn = false;
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
        if (_syncingCountIn) return;
        _settings.Audio.CountInOnlyAtSongStart = CountInSongStartCheck.IsChecked == true;
        _settings.Audio.CountInEachSection = CountInSectionCheck.IsChecked == true;
        _settings.Audio.LoopCountInEachLoop = CountInLoopCheck.IsChecked == true;
        ApplyLoopBehaviour();
        TabForge.Playback.PlaybackEngine.CountInEachSection = _countIn && _settings.Audio.CountInEachSection;
        SaveSettings();
    }

    private void CountInOption_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_mainWindowInitialized || _syncingCountIn) return;
        var audio = _settings.Audio;
        if (CountInBarsCombo.SelectedItem is ComboBoxItem { Tag: string bars } && int.TryParse(bars, out var n))
            audio.CountInBars = n;
        if (CountInSoundCombo.SelectedItem is ComboBoxItem { Tag: string sound }) audio.CountInSound = sound;
        ApplyCountInSound();
        _midi.UpdateOptions(_project, o => o.CountInBars = audio.CountInBars);
        SaveSettings();
    }

    private void ApplyCountInSound()
    {
        var parts = _settings.Audio.CountInSound.Split(',');
        var custom = parts.Length == 2 && int.TryParse(parts[0], out var accent) && int.TryParse(parts[1], out var click);
        TabForge.Playback.PlaybackEngine.CountInAccentNote = custom ? int.Parse(parts[0]) : -1;
        TabForge.Playback.PlaybackEngine.CountInClickNote = custom ? int.Parse(parts[1]) : -1;
    }

    private void LoopCountBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { LoopBehaviour_Changed(sender, e); e.Handled = true; }
    }

    private static int ParsePercent(string? text, int fallback) =>
        int.TryParse(text?.Trim().TrimEnd('%'), out var v) ? Math.Clamp(v, 10, 200) : fallback;

    private void LoopBehaviour_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingLoopSettings) return;
        var audio = _settings.Audio;
        var countText = LoopCountBox.Text.Trim();
        audio.LoopCount = int.TryParse(countText, out var count) ? Math.Clamp(count, 0, 9999) : 0;
        audio.LoopCountInEachLoop = LoopCountInCheck.IsChecked == true;
        audio.LoopSpeedTrainer = LoopTrainerRadio.IsChecked == true;
        audio.LoopTrainerFrom = ParsePercent(LoopTrainerFromBox.Text, audio.LoopTrainerFrom);
        audio.LoopTrainerTo = ParsePercent(LoopTrainerToBox.Text, audio.LoopTrainerTo);
        audio.LoopTrainerStep = int.TryParse(LoopTrainerStepBox.Text?.Trim().TrimEnd('%'), out var step) ? Math.Clamp(step, 0, 100) : audio.LoopTrainerStep;
        SyncLoopBehaviourControls();
        ApplyLoopBehaviour();
        SaveSettings();
    }

    private void SyncLoopBehaviourControls()
    {
        var audio = _settings.Audio;
        var was = _syncingLoopSettings;
        _syncingLoopSettings = true;
        LoopCountBox.Text = audio.LoopCount > 0 ? audio.LoopCount.ToString() : "∞";
        LoopCountInCheck.IsChecked = audio.LoopCountInEachLoop;
        LoopTrainerRadio.IsChecked = audio.LoopSpeedTrainer;
        LoopSimpleRadio.IsChecked = !audio.LoopSpeedTrainer;
        LoopTrainerFromBox.Text = audio.LoopTrainerFrom.ToString();
        LoopTrainerToBox.Text = audio.LoopTrainerTo.ToString();
        LoopTrainerStepBox.Text = audio.LoopTrainerStep.ToString();
        _syncingLoopSettings = was;
    }

    private void ApplyLoopBehaviour()
    {
        var audio = _settings.Audio;
        TabForge.Playback.PlaybackEngine.LoopSettings = new TabForge.Playback.PlaybackEngine.LoopBehaviour(
            audio.LoopCount, audio.LoopCountInEachLoop, audio.LoopSpeedTrainer,
            audio.LoopTrainerFrom, audio.LoopTrainerTo, audio.LoopTrainerStep);
        UpdateLoopCountBadge(_loopsCompleted);
    }

    private int _loopsCompleted;

    // Loops-left badge beside the loop button: it takes its own space, so neighbouring buttons move over.
    private void UpdateLoopCountBadge(int completed)
    {
        _loopsCompleted = completed;
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
        TabForge.Playback.PlaybackEngine.MetronomeBoost = _settings.Audio.MetronomeBoost;
        SaveSettings();
    }

    private void MetronomeSubdivision_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_mainWindowInitialized || _syncingMetronomeSettings || MetronomeSubdivisionCombo?.SelectedItem is not ComboBoxItem { Tag: string tag } ||
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
        _syncingMetronomeSettings = true;
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
        _syncingMetronomeSettings = false;
    }

    private void ApplyMetronomeSettingsToEngines()
    {
        var audio = _settings.Audio;
        foreach (var document in _documents.Documents)
            document.Playback.Engine.SetMetronomeSettings(_metronome, audio.MetronomeVolume,
                audio.MetronomeAccentVolume, audio.MetronomeClickVolume,
                audio.MetronomeAccent, audio.MetronomeClick, audio.MetronomeSubdivision);
    }

    private void QueueMetronomeSettingsSave()
    {
        if (_metronomeSettingsSaveTimer is null) SaveSettings();
        else
        {
            _metronomeSettingsSaveTimer.Stop();
            _metronomeSettingsSaveTimer.Start();
        }
    }

    private void CountIn_Click(object sender, RoutedEventArgs e)
    {
        _countIn = !_countIn;
        CountInMenu.IsChecked = _countIn;
        SetTransportActive(CountInButton, _countIn);
        TabForge.Playback.PlaybackEngine.CountInEachSection = _countIn && _settings.Audio.CountInEachSection;
        _midi.SetSectionStarts(_project.Markers.Select(marker => marker.MeasureIndex).Where(bar => bar > 0));
        // Takes effect the next time playback starts from silence; toggling it never interrupts playback.
        if (!_midi.IsPlaying) _midi.UpdateOptions(_project, o => o.CountIn = _countIn);
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

    // ---------- chords/scales tab ----------

    private void ChordShow_Click(object sender, RoutedEventArgs e)
    {
        var root = ChordRootCombo.SelectedItem?.ToString() ?? "C";
        var type = ChordTypeCombo.SelectedItem?.ToString() ?? "Maj";
        if (!MusicTheoryService.Chords.TryGetValue(type, out var iv)) iv = new[] { 0, 4, 7 };
        var rootIdx = Array.FindIndex(MusicTheoryService.NoteNames, n => n == root);
        var notes = iv.Select(i => MusicTheoryService.NoteNames[(rootIdx + i) % 12]).ToList();
        ChordResultList.ItemsSource = notes.Select((n, i) => $"{n}  (tone {iv[i]})").ToList();
    }

    private void ChordInsert_Click(object sender, RoutedEventArgs e)
    {
        var c = Editor.CurrentCell(); if (c is null) return;
        var root = ChordRootCombo.SelectedItem?.ToString() ?? "C";
        var type = ChordTypeCombo.SelectedItem?.ToString() ?? "Maj";
        CaptureUndo(); c.ChordName = $"{root}{type}"; CommitEdit(EditRefresh.Score);
    }

    private void ScaleShow_Click(object sender, RoutedEventArgs e)
    {
        var root = ScaleRootCombo.SelectedItem?.ToString() ?? "C";
        var name = ScaleNameCombo.SelectedItem?.ToString() ?? "Major";
        ScaleResultText.Text = $"{root} {name}: {string.Join(" – ", MusicTheoryService.ScaleNotes(root, name))}";
    }

    private void LyricsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_project.Lyrics != LyricsBox.Text) { CaptureUndo(); _project.Lyrics = LyricsBox.Text; CommitEdit(EditRefresh.None); }
    }
}
