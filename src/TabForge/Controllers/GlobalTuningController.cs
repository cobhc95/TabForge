using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Controllers;

/// <summary>What the global-tuning controller needs from its window.</summary>
internal interface IGlobalTuningHost
{
    /// <summary>The song the window shows now.</summary>
    DocumentSession Document { get; }
    void SetTuningLabel(int shift);
    void SetTuningLabel(string name);
    /// <summary>Redraws score, timeline and instrument after the retune, then rebuilds playback.</summary>
    void AfterRetune();
    void SetStatus(string text);
    /// <summary>The global tuning window: the chosen per-string pitches (six-string reference, high to low), or null when cancelled.</summary>
    int[]? ChooseTuning(int[] current);
}

// Owns: retuning every pitched track at once (one undo step), the document's per-string shift readout and the tuning button's label.
// Does not own: the tuning window or the button (the host and TuningButtonController), nor the playback rebuild (the host's AfterRetune).
// Needs from its host: IGlobalTuningHost; the song is passed through the host's Document, never read from a window.
// Tests: TestGlobalTuningController.
internal sealed class GlobalTuningController
{
    private readonly IGlobalTuningHost _host;

    public GlobalTuningController(IGlobalTuningHost host) => _host = host;

    private int[] Offsets => _host.Document.TuningShift;

    public static bool IsUniform(int[] offsets) => offsets.All(o => o == offsets[0]);

    public bool IsUniformNow => IsUniform(Offsets);

    public void SetUniformShift(int shift) => Retune(Offsets.Select(o => shift - o).ToArray());

    public void ResetToOriginal() => Retune(Offsets.Select(o => -o).ToArray());

    public void RetuneAll(int semitones) => Retune(Enumerable.Repeat(semitones, 6).ToArray());

    /// <summary>Opens the tuning window on the current per-string tuning and applies the change it returns.</summary>
    public void ChooseAndRetune()
    {
        var current = GlobalTuningWindowStandard.Select((p, i) => p + Offsets[i]).ToArray();
        if (_host.ChooseTuning(current) is not { } chosen) return;
        Retune(chosen.Select((p, i) => p - current[i]).ToArray());
    }

    /// <summary>
    /// Retunes every pitched track by a per-string change (six-string reference, high to low): string
    /// tunings and sounding pitches move together and fret numbers stay the same, like physically
    /// retuning the instrument. Strings map from the lowest string up, so a bass follows the four
    /// lowest strings and extra low strings on 7/8-strings follow the lowest.
    /// </summary>
    public void Retune(int[] delta)
    {
        if (delta.All(d => d == 0)) return;
        var doc = _host.Document;
        DocumentEdits.Run(doc, project =>
        {
            ApplyDelta(project, delta);
            for (var i = 0; i < 6; i++) doc.TuningShift[i] += delta[i];   // the document's tuning-shift readout (session state, not part of the undo snapshot)
            return true;
        });
        UpdateLabel();
        _host.AfterRetune();
        var offsets = doc.TuningShift;
        var uniform = IsUniform(offsets) ? offsets[0] : 0;
        _host.SetStatus(offsets.All(o => o == 0) ? "Original tuning"
            : IsUniform(offsets) ? $"All tracks retuned {(uniform > 0 ? "+" : "")}{uniform} semitones"
            : "All tracks retuned (custom per-string tuning)");
    }

    /// <summary>The tuning button's text: the signed shift when every string moved alike, otherwise the matching preset's name or "Custom".</summary>
    public void UpdateLabel()
    {
        var offsets = Offsets;
        if (IsUniform(offsets)) { _host.SetTuningLabel(offsets[0]); return; }
        var tuning = GlobalTuningWindowStandard.Select((p, i) => p + offsets[i]).ToArray();
        var preset = Views.TrackPropertiesWindow.SixStringPresets.FirstOrDefault(p => p.HighToLow.SequenceEqual(tuning));
        _host.SetTuningLabel(preset.Name?.Split(" (")[0] ?? "Custom");
    }

    private static int[] GlobalTuningWindowStandard => Views.GlobalTuningWindow.StandardE;

    private static void ApplyDelta(SongProject project, int[] delta)
    {
        foreach (var track in project.Tracks.Where(t => t.MidiChannel != 9))
        {
            var count = track.StringTunings.Count;
            int DeltaOf(int stringIndex) => delta[Math.Clamp(count <= 6 ? stringIndex + (6 - count) : Math.Min(stringIndex, 5), 0, 5)];
            track.StringTunings = track.StringTunings.Select((p, s) => Math.Clamp(p + DeltaOf(s), 0, 127)).ToList();
            foreach (var measure in track.Measures)
                foreach (var cell in measure.Cells.Concat(measure.Voice2Cells))
                    foreach (var note in cell.Notes)
                    {
                        var d = DeltaOf(note.StringIndex);
                        note.MidiValue = Math.Clamp(note.MidiValue + d, 0, 127);
                        if (note.SlideTargetMidi > 0) note.SlideTargetMidi = Math.Clamp(note.SlideTargetMidi + d, 0, 127);
                        if (note.TrillTargetMidi > 0) note.TrillTargetMidi = Math.Clamp(note.TrillTargetMidi + d, 0, 127);
                    }
        }
    }
}
