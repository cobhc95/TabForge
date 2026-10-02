using System.Windows.Controls;
using System.Windows.Threading;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Controllers;

/// <summary>What the transport controls need from their window.</summary>
internal interface ITransportControlsHost
{
    /// <summary>The songs open in this window (each has its own playback engine).</summary>
    IReadOnlyList<DocumentSession> Documents { get; }
    AppSettings Settings { get; }
    void SaveSettings();
    /// <summary>The playback behaviour every song's engine reads (shared by the windows).</summary>
    PlaybackPreferences Preferences { get; }
    /// <summary>The speed box in the Zoom and speed pane (null before the window's controls exist).</summary>
    ComboBox? SpeedCombo { get; }
    /// <summary>The window is loaded and not restoring its settings: a control's change is the user's.</summary>
    bool IsInteractive { get; }
    /// <summary>The speed of the song on show, while it plays.</summary>
    void SetEngineSpeed(double speed);
    void SetStatus(string text);
}

// Owns: one window's transport state: speed, count-in and metronome switches, loops left, and the refresh flags that silence
//     control handlers.
// Does not own: playback itself and the stored settings values.
// Tests: TestWindowLifetime, TestSpeedControl.
/// <summary>
/// One window's transport state: playback speed, count-in and metronome switches, the loops-left count and the "controls are being refreshed from
/// the settings" flags that keep a control's change handler quiet while the window writes to it. A metronome or count-in slider fires many
/// changes in a drag: they are saved once, after a short pause. <see cref="Dispose"/> (once, when the window has really closed) saves a change that is
/// still waiting for that pause, then stops the timer.
/// </summary>
internal sealed class TransportControlsController : IDisposable
{
    private readonly ITransportControlsHost _host;
    private readonly DispatcherTimer _saveTimer;
    private bool _disposed;

    public TransportControlsController(ITransportControlsHost host)
    {
        _host = host;
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(280) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _host.SaveSettings();
        };
    }

    /// <summary>Playback speed, 0.25 to 2.0 (1.0 is the written tempo).</summary>
    public double Speed { get; set; } = 1.0;
    public bool CountIn { get; set; }
    public bool Metronome { get; set; }
    /// <summary>Loops played so far in the running loop (the badge beside the loop button shows what is left).</summary>
    public int LoopsCompleted { get; set; }
    public bool SyncingLoopSettings { get; set; }
    public bool SyncingCountIn { get; set; }
    public bool SyncingMetronomeSettings { get; set; }

    /// <summary>A metronome or count-in setting changed: it is saved after a short pause, not at every step of a drag.</summary>
    public void QueueSettingsSave()
    {
        if (_disposed) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>True while a change waits for its save (self-test).</summary>
    public bool SavePending => _saveTimer.IsEnabled;

    /// <summary>The metronome switch and its sound settings go to every open song's engine (a song shown later clicks the same).</summary>
    public void ApplyMetronomeSettingsToEngines()
    {
        var audio = _host.Settings.Audio;
        foreach (var document in _host.Documents)
            document.Playback.Engine.SetMetronomeSettings(Metronome, audio.MetronomeVolume,
                audio.MetronomeAccentVolume, audio.MetronomeClickVolume,
                audio.MetronomeAccent, audio.MetronomeClick, audio.MetronomeSubdivision);
    }

    /// <summary>The loop count, count-in and speed-trainer settings go to the playback engine (they apply to every song).</summary>
    public void ApplyLoopBehaviour()
    {
        var audio = _host.Settings.Audio;
        _host.Preferences.Loop = new PlaybackEngine.LoopBehaviour(
            audio.LoopCount, audio.LoopCountInEachLoop, audio.LoopSpeedTrainer,
            audio.LoopTrainerFrom, audio.LoopTrainerTo, audio.LoopTrainerStep);
    }

    // ---- speed ----

    private bool _speedSync;

    private static readonly double[] SpeedPresets = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };

    /// <summary>Parses typed speed: "90%" or "90" = 0.9; a bare value of 4 or less is a factor ("0.9", "1.25").</summary>
    internal static double? ParseSpeedText(string? text)
    {
        var t = (text ?? "").Trim().Replace(',', '.');
        var pct = t.EndsWith('%');
        t = t.TrimEnd('%', '×', 'x', 'X').Trim();
        if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
            return null;
        return pct || v > 4 ? v / 100.0 : v;
    }

    /// <summary>The next preset above (direction +1) or below (-1) the current speed; a custom speed steps to its neighbouring preset.</summary>
    internal static double NextSpeedPreset(double current, int direction)
    {
        current = ClampSpeed(current);
        if (direction > 0) foreach (var p in SpeedPresets) { if (p > current + 1e-6) return p; }
        else for (var i = SpeedPresets.Length - 1; i >= 0; i--) { if (SpeedPresets[i] < current - 1e-6) return SpeedPresets[i]; }
        return current;
    }

    internal static double ClampSpeed(double v) => double.IsFinite(v) ? Math.Clamp(v, 0.25, 2.0) : 1.0;

    /// <summary>Makes an editable speed combo display the given speed as a percentage; returns the text shown.</summary>
    internal static string ShowSpeedOn(ComboBox combo, double speed)
    {
        combo.ApplyTemplate();
        var label = $"{ClampSpeed(speed) * 100:0}%";
        var preset = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Content?.ToString(), label, StringComparison.Ordinal));
        combo.SelectedItem = preset;
        if (preset is null) combo.SelectedIndex = -1;
        combo.Text = label;
        return label;
    }

    /// <summary>A preset was picked in the speed box.</summary>
    public void OnSpeedComboChanged()
    {
        if (_speedSync || !_host.IsInteractive || _host.SpeedCombo?.SelectedItem is not ComboBoxItem item) return;
        ApplySpeedText(item.Content?.ToString() ?? "100%");
    }

    /// <summary>A custom speed was typed in the speed box (Enter or leaving the box).</summary>
    public void CommitCustomSpeed()
    {
        if (!_host.IsInteractive || _host.SpeedCombo is not { } combo || combo.SelectedItem is ComboBoxItem) return;
        ApplySpeedText(combo.Text);
    }

    private void ApplySpeedText(string text)
    {
        if (ParseSpeedText(text) is not { } v) { ShowSpeed(); return; }
        ApplySpeed(v);
    }

    /// <summary>Sets the playback speed everywhere (engine and the speed box) and saves it.</summary>
    public void ApplySpeed(double value)
    {
        Speed = ClampSpeed(value);
        _host.SetEngineSpeed(Speed);
        ShowSpeed();
        _host.SetStatus($"Speed {Speed:0.00}×");
        if (_host.IsInteractive) _host.SaveSettings();
    }

    /// <summary>Re-draws the speed box from <see cref="Speed"/> (also after a settings restore).</summary>
    public void ShowSpeed()
    {
        if (_host.SpeedCombo is not { } combo) return;
        var was = _speedSync;
        _speedSync = true;
        try { ShowSpeedOn(combo, Speed); }
        finally { _speedSync = was; }
    }

    // ---- what the transport sends to the engine ----

    /// <summary>The playback options for a start at the given bar and cell, from the transport switches and the audio settings.</summary>
    public PlaybackOptions BuildOptions(int startBar, int startCell, bool loop, int loopStartBar, int loopEndBar, int loopStartCell, int loopEndCell)
    {
        var audio = _host.Settings.Audio;
        _host.Preferences.CountInEachSection = CountIn && audio.CountInEachSection;
        return new PlaybackOptions
        {
            StartBar = startBar,
            StartCell = startCell,
            Speed = Speed,
            Loop = loop,
            LoopStartBar = loopStartBar,
            LoopEndBar = loopEndBar,
            LoopStartCell = loopStartCell,
            LoopEndCell = loopEndCell,
            Metronome = Metronome,
            // "Only from bar 1": the count-in plays only when starting at the top of the song.
            CountIn = CountIn && (!audio.CountInOnlyAtSongStart || startBar == 0),
            CountInBars = audio.CountInBars,
            MetronomeAccentNote = audio.MetronomeAccent,
            MetronomeClickNote = audio.MetronomeClick,
            MetronomeVolume = audio.MetronomeVolume,
            MetronomeAccentVolume = audio.MetronomeAccentVolume,
            MetronomeClickVolume = audio.MetronomeClickVolume,
            MetronomeSubdivision = audio.MetronomeSubdivision,
            LetRingCapMs = audio.LetRingCapMs
        };
    }

    /// <summary>The bars the loop button covers when no area was picked, per the loop scope setting ("Bar", "Song" or the section of <paramref name="bar"/>).</summary>
    public static (int start, int end) DefaultLoopArea(SongProject project, string scope, int bar)
    {
        var lastBar = Math.Max(0, (project.Tracks.Count == 0 ? 1 : project.Tracks.Max(t => t.Measures.Count)) - 1);
        bar = Math.Clamp(bar, 0, lastBar);
        switch (scope)
        {
            case "Bar":
                return (bar, bar);
            case "Song":
                return (0, lastBar);
            default:
                var markers = SectionLayout.Sorted(project);
                var index = markers.FindLastIndex(m => m.MeasureIndex <= bar);
                if (index < 0) return markers.Count > 0 ? (0, Math.Max(0, markers[0].MeasureIndex - 1)) : (0, lastBar);
                var sectionEnd = SectionLayout.End(markers, index, lastBar + 1);
                // A bar in the gap after a resized section loops the gap itself.
                if (bar >= sectionEnd)
                    return (sectionEnd, Math.Clamp((index + 1 < markers.Count ? markers[index + 1].MeasureIndex : lastBar + 1) - 1, sectionEnd, lastBar));
                return (markers[index].MeasureIndex, Math.Clamp(sectionEnd - 1, markers[index].MeasureIndex, lastBar));
        }
    }

    /// <summary>The count-in sound setting ("accent,click" notes, or a preset name) goes to the playback engine.</summary>
    public void ApplyCountInSound()
    {
        var parts = _host.Settings.Audio.CountInSound.Split(',');
        var custom = parts.Length == 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _);
        _host.Preferences.CountInAccentNote = custom ? int.Parse(parts[0]) : -1;
        _host.Preferences.CountInClickNote = custom ? int.Parse(parts[1]) : -1;
    }

    /// <summary>Sets the metronome notes of a sound preset; false for "custom" or an unknown name (nothing changes).</summary>
    public bool ApplyMetronomePreset(string preset)
    {
        var audio = _host.Settings.Audio;
        (int accent, int click) notes;
        switch (preset)
        {
            case "classic": notes = (33, 34); break;
            case "woodblock": notes = (76, 77); break;
            case "sidestick": notes = (37, 37); break;
            case "clap": notes = (39, 39); break;
            default: return false;
        }
        (audio.MetronomeAccent, audio.MetronomeClick) = notes;
        return true;
    }

    /// <summary>Reads the loop settings popup's boxes into the audio settings (an unreadable box keeps its setting, or falls back to endless / 0).</summary>
    public void ReadLoopBehaviour(string countText, bool countInEachLoop, bool speedTrainer, string? fromText, string? toText, string? stepText)
    {
        var audio = _host.Settings.Audio;
        audio.LoopCount = int.TryParse(countText.Trim(), out var count) ? Math.Clamp(count, 0, 9999) : 0;
        audio.LoopCountInEachLoop = countInEachLoop;
        audio.LoopSpeedTrainer = speedTrainer;
        audio.LoopTrainerFrom = ParsePercent(fromText, audio.LoopTrainerFrom);
        audio.LoopTrainerTo = ParsePercent(toText, audio.LoopTrainerTo);
        audio.LoopTrainerStep = int.TryParse(stepText?.Trim().TrimEnd('%'), out var step) ? Math.Clamp(step, 0, 100) : audio.LoopTrainerStep;
    }

    private static int ParsePercent(string? text, int fallback) =>
        int.TryParse(text?.Trim().TrimEnd('%'), out var v) ? Math.Clamp(v, 10, 200) : fallback;

    /// <summary>A change still waiting for its save is saved now.</summary>
    public void FlushPendingSave()
    {
        if (!_saveTimer.IsEnabled) return;
        _saveTimer.Stop();
        _host.SaveSettings();
    }

    public void Dispose()
    {
        if (_disposed) return;
        FlushPendingSave();
        _disposed = true;
        _saveTimer.Stop();
    }
}
