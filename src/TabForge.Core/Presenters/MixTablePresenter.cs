// Owns: the Mix Table's rules without UI types: the scale and pan ranges, the start value of each field (the beat's mix point, else the
//       track's value scaled to the table), the instrument choices (one per program, drum kits left out), the Transition labels with their
//       start index, the tempo text and its 20 to 400 parse, the texts, and the result a finished window hands back.
// Does not own: the window, its sliders, check boxes, combo boxes and buttons, the dialog result, or the write to the beat (MainWindow
//       writes it through DocumentEdits.Run).
// Tests: no dedicated test class.
using TabForge.Models;

namespace TabForge.Presenters;

/// <summary>A Mix Table field: whether its row is ticked and its value. Ticked fields are written to the beat.</summary>
public readonly record struct MixFieldValue(bool On, int Value);

/// <summary>What the window's controls hold when OK is pressed. The window reads them and passes them to <see cref="MixTablePresenter.Build"/>.</summary>
public sealed record MixTableEdits(
    bool InstrumentOn, int InstrumentIndex, MixFieldValue Volume, MixFieldValue Pan, MixFieldValue Chorus, MixFieldValue Reverb,
    MixFieldValue Phaser, MixFieldValue Tremolo, bool TempoOn, string TempoText, int TransitionIndex, bool AllTracks);

/// <summary>
/// Mix Table rules for one beat. Built from the beat's mix point and the track's values. The window reads the start values and labels
/// from it, and <see cref="Build"/> turns the window's controls into the result. Thread: UI thread only.
/// </summary>
public sealed class MixTablePresenter
{
    public const int ScaleMin = 0;
    public const int ScaleMax = 16;
    public const int PanMin = -8;
    public const int PanMax = 8;
    public const int TempoMin = 20;
    public const int TempoMax = 400;
    public const int MaxTransitionBeats = 8;

    public const string Title = "Mix Table";
    public const string Prompt = "Choose the parameters to change from this beat and when these changes shall be applied:";
    public const string InstrumentLabel = "Instrument";
    public const string VolumeLabel = "Volume";
    public const string PanLabel = "Pan";
    public const string ChorusLabel = "Chorus";
    public const string ReverbLabel = "Reverb";
    public const string PhaserLabel = "Phaser";
    public const string TremoloLabel = "Tremolo";
    public const string TempoLabel = "Tempo (bar)";
    public const string TransitionLabel = "Transition";
    public const string AllTracksLabel = "All tracks";
    public const string ClearButton = "Clear";
    public const string OkButton = "OK";
    public const string CancelButton = "Cancel";

    /// <summary>The window's answer: the mix point to set on the beat (null when none) and the tempo for the bar (null when none).</summary>
    public sealed record Result(MixChange? Mix, int? Tempo);

    private readonly List<(int Program, string Label)> _instrumentChoices;

    /// <param name="current">The beat's mix point, or null when the beat has none.</param>
    /// <param name="currentProgram">The track's instrument program, used when the beat has none.</param>
    /// <param name="currentVolume">The track's volume (0 to 127), scaled to the table's range when the beat has none.</param>
    /// <param name="currentPan">The track's pan (0 to 127, 64 is centre), scaled to the table's range when the beat has none.</param>
    /// <param name="currentTempo">The bar's tempo, or null when the bar has none.</param>
    /// <param name="songTempo">The song's tempo, the tempo box's start when the bar has none.</param>
    /// <param name="instruments">The catalogue entries: program, name and whether it is a drum kit.</param>
    public MixTablePresenter(MixChange? current, int currentProgram, int currentVolume, int currentPan, int? currentTempo, int songTempo,
        IEnumerable<(int Program, string Name, bool IsDrumKit)> instruments)
    {
        _instrumentChoices = instruments.Where(e => !e.IsDrumKit).GroupBy(e => e.Program)
            .Select(g => (g.Key, $"{g.Key + 1} - {g.First().Name}")).ToList();
        var programNow = current?.Program ?? currentProgram;
        InstrumentOn = current?.Program is not null;
        InstrumentStartIndex = _instrumentChoices.FindIndex(c => c.Program == programNow);
        Volume = new MixFieldValue(current?.Volume is not null,
            current?.Volume ?? Math.Clamp((int)Math.Round(currentVolume / 8.0), ScaleMin, ScaleMax));
        Pan = new MixFieldValue(current?.Pan is not null,
            current?.Pan ?? Math.Clamp((int)Math.Round((currentPan - 64) / 8.0), PanMin, PanMax));
        Chorus = new MixFieldValue(current?.Chorus is not null, current?.Chorus ?? 0);
        Reverb = new MixFieldValue(current?.Reverb is not null, current?.Reverb ?? 0);
        Phaser = new MixFieldValue(current?.Phaser is not null, current?.Phaser ?? 0);
        Tremolo = new MixFieldValue(current?.Tremolo is not null, current?.Tremolo ?? 0);
        TempoOn = currentTempo is not null;
        TempoStartText = (currentTempo ?? songTempo).ToString();
        TransitionLabels = Enumerable.Range(0, MaxTransitionBeats + 1)
            .Select(beats => beats == 0 ? "Immediately" : beats == 1 ? "Over 1 beat" : $"Over {beats} beats").ToList();
        TransitionStartIndex = Math.Clamp(current?.TransitionBeats ?? 0, 0, MaxTransitionBeats);
        AllTracksStart = current?.AllTracks ?? false;
    }

    /// <summary>The instrument rows: the program's number and name, one per program, drum kits left out.</summary>
    public IReadOnlyList<(int Program, string Label)> InstrumentChoices => _instrumentChoices;

    /// <summary>Whether the instrument row starts ticked.</summary>
    public bool InstrumentOn { get; }

    /// <summary>The index in <see cref="InstrumentChoices"/> of the start instrument, or -1 when the program is not listed.</summary>
    public int InstrumentStartIndex { get; }

    public MixFieldValue Volume { get; }
    public MixFieldValue Pan { get; }
    public MixFieldValue Chorus { get; }
    public MixFieldValue Reverb { get; }
    public MixFieldValue Phaser { get; }
    public MixFieldValue Tremolo { get; }

    /// <summary>Whether the tempo row starts ticked.</summary>
    public bool TempoOn { get; }

    /// <summary>The tempo box's text at the start: the bar's tempo, or the song's when the bar has none.</summary>
    public string TempoStartText { get; }

    /// <summary>The Transition choices: Immediately, then Over 1 beat to Over 8 beats. The index is the number of beats.</summary>
    public IReadOnlyList<string> TransitionLabels { get; }

    /// <summary>The Transition choice selected at the start.</summary>
    public int TransitionStartIndex { get; }

    /// <summary>Whether All tracks starts ticked.</summary>
    public bool AllTracksStart { get; }

    /// <summary>The result of the Clear button: an empty mix point and no tempo.</summary>
    public static Result Cleared => new(new MixChange(), null);

    /// <summary>The text shown beside a slider at a position.</summary>
    public static string ValueText(double sliderValue) => ((int)sliderValue).ToString();

    /// <summary>
    /// The result of OK: each ticked field becomes the value its slider holds (a field left unticked stays null), the Transition index is the
    /// number of beats, and the tempo is the typed number clamped to 20 to 400 when its row is ticked and the text is a whole number.
    /// </summary>
    public Result Build(MixTableEdits edits)
    {
        var program = edits.InstrumentOn && edits.InstrumentIndex >= 0 && edits.InstrumentIndex < _instrumentChoices.Count
            ? _instrumentChoices[edits.InstrumentIndex].Program
            : (int?)null;
        var mix = new MixChange
        {
            Program = program,
            Volume = Picked(edits.Volume), Pan = Picked(edits.Pan), Chorus = Picked(edits.Chorus),
            Reverb = Picked(edits.Reverb), Phaser = Picked(edits.Phaser), Tremolo = Picked(edits.Tremolo),
            TransitionBeats = edits.TransitionIndex, AllTracks = edits.AllTracks,
        };
        int? tempo = edits.TempoOn && int.TryParse(edits.TempoText.Trim(), out var bpm) ? Math.Clamp(bpm, TempoMin, TempoMax) : null;
        return new Result(mix, tempo);
    }

    private static int? Picked(MixFieldValue field) => field.On ? field.Value : null;
}
