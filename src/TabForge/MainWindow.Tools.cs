using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;

namespace TabForge;

// MainWindow, tools: the handlers and host members of the Tools menu, the metronome, count-in and loop settings popups, and the chord and song windows.
// Owns: the XAML and menu handlers of the Tools commands and the transport popups (one-line forwards), and the host members of the transport settings.
// Does not own: what the tools do (ToolActionsFlow), the popups' logic (TransportSettingsController), the toggle look (TransportButtonStyle).
// Tests: listed in docs/feature-map/editing-and-notation.md.
public partial class MainWindow : ITransportSettingsHost
{
    // ---------- tools ----------

    private void CheckBars_Click(object sender, RoutedEventArgs e) => ToolActions.CheckBars();
    private void Transpose_Click(object sender, RoutedEventArgs e) => ToolActions.Transpose();

    // Tools > Scale finder: likely scales for the selection / song, or any scale, shown on the fretboard.
    private void ScaleFinder_Click(object sender, RoutedEventArgs e) => InstrumentPane.OpenScaleFinder();

    private void Tuner_Click(object sender, RoutedEventArgs e)
    {
        // Chromatic tuner: the engine listens to the armed input; the window follows the selected track's tuning.
        TunerWindow.Open(this, _engine, () => SelectedTrack);
    }

    // ---- transport settings popups (TransportSettingsController) ----

    private TransportSettingsController? _transportSettings;
    private TransportSettingsController TransportSettings => _transportSettings ??= new TransportSettingsController(this);

    bool ITransportSettingsHost.IsInitialized => _mainWindowInitialized;
    TransportControlsController ITransportSettingsHost.Transport => _transport;
    PlaybackEngine ITransportSettingsHost.Engine => _midi;
    TransportPopupControls ITransportSettingsHost.Controls => new(
        MetronomeButton, MetronomeMenu, MetronomeSettingsPopup, MetronomeVolumeSlider, MetronomeAccentSlider, MetronomeClickSlider,
        MetronomeVolumeValue, MetronomeAccentValue, MetronomeClickValue, MetronomeSoundCombo, MetronomeSubdivisionCombo, MetronomeBoostCheck,
        CountInButton, CountInMenu, CountInSettingsPopup, CountInBarsCombo, CountInSoundCombo, CountInVolumeSlider, CountInVolumeValue,
        CountInSongStartCheck, CountInSectionCheck, CountInLoopCheck, LoopButton, LoopSettingsPopup, LoopRangeText, LoopScopeCombo,
        LoopClearOnDisableCheck, LoopButtonSectionCheck, LoopCountBadge, LoopCountBox, LoopCountInCheck, LoopTrainerRadio, LoopSimpleRadio,
        LoopTrainerFromBox, LoopTrainerToBox, LoopTrainerStepBox);

    private void Metronome_Click(object sender, RoutedEventArgs e) => TransportSettings.ToggleMetronome();
    private void CountIn_Click(object sender, RoutedEventArgs e) => TransportSettings.ToggleCountIn();

    private void MetronomeButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        TransportSettings.OpenMetronomeSettings();
        e.Handled = true;
    }

    private void CountInButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        TransportSettings.OpenCountInSettings();
        e.Handled = true;
    }

    private void CloseMetronomeSettings_Click(object sender, RoutedEventArgs e)
        => MetronomeSettingsPopup.IsOpen = false;

    private void MetronomeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider) TransportSettings.OnMetronomeSlider(slider);
    }

    private void MetronomeSound_SelectionChanged(object sender, SelectionChangedEventArgs e) => TransportSettings.OnMetronomeSound(MetronomeSoundCombo);
    private void MetronomeSubdivision_SelectionChanged(object sender, SelectionChangedEventArgs e) => TransportSettings.OnMetronomeSubdivision(MetronomeSubdivisionCombo);
    private void MetronomeBoost_Click(object sender, RoutedEventArgs e) => TransportSettings.OnMetronomeBoost(MetronomeBoostCheck);
    private void CountInVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => TransportSettings.OnCountInVolume(e.NewValue);
    private void CountInChecks_Click(object sender, RoutedEventArgs e) => TransportSettings.OnCountInChecks();
    private void CountInOption_Changed(object sender, SelectionChangedEventArgs e) => TransportSettings.OnCountInOption();
    private void LoopButtonSection_Click(object sender, RoutedEventArgs e) => TransportSettings.OnLoopButtonSection(LoopButtonSectionCheck);

    private void LoopCountBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { LoopBehaviour_Changed(sender, e); e.Handled = true; }
    }

    private void LoopBehaviour_Changed(object sender, RoutedEventArgs e) => TransportSettings.OnLoopBehaviour();

    // What the settings, document and track code calls: the popups and the transport state behind them.
    private void SyncMetronomeSettingsPopup() => TransportSettings.SyncMetronomePopup();
    private void ApplyLoopBehaviour() => TransportSettings.ApplyLoopBehaviour();
    private void UpdateLoopCountBadge(int completed) => TransportSettings.ShowLoopCountBadge(completed);
    private void ApplyCountInSound() => _transport.ApplyCountInSound();
    private void ApplyMetronomeSettingsToEngines() => _transport.ApplyMetronomeSettingsToEngines();
    private void QueueMetronomeSettingsSave() => _transport.QueueSettingsSave();
    private static void SetTransportActive(Button button, bool active) => TransportButtonStyle.Apply(button, active);

    // Tools > Chord finder… and Tools > Song stats…: small windows (Views/ChordFinderWindow, Views/SongStatsWindow).
    private void ChordFinder_Click(object sender, RoutedEventArgs e) => Views.ChordFinderWindow.Show(this, ToolActions.InsertChordName);

    private void SongStats_Click(object sender, RoutedEventArgs e) => Views.SongStatsWindow.Show(this, _project, _currentPath);

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
