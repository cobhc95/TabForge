using System.Globalization;

namespace TabForge.Presenters;

// Owns: the 'Move notes' popup's model (T with notes selected): the title and 'n notes selected' line, the Half steps / Stay in the key choice and its captions,
//   the signed amount with its -/+ and octave steps (words such as 'up 1', 'down 2', 'no change' with a 'step(s)' or 'half step(s)' caption), the Before / After note-name
//   chips for the lowest three distinct pitches, the optional snap-to-key tick (half-step mode only) and the range warning. The chips follow the mode and the snap exactly like
//   PianoRollPresenter.TransposeAsync, which moves the whole selection together and stops at MIDI 0-127.
// Does not own: the popup (the app's PianoRollTransposePopup), the edit (PianoRollPresenter.TransposeAsync) or the scale table (PianoRollScaleLock).
// Tests: TabForge.Avalonia.Tests.PianoRollTransposeDialogTests.
/// <summary>What the Move notes popup answers: the number, whether it counts scale steps, and whether the result snaps to the key (this move only).</summary>
public readonly record struct MoveNotesAnswer(int Delta, bool ScaleSteps, bool Snap);

public sealed class PianoRollTransposeDialog
{
    /// <summary>The half steps one octave pill and Shift+Up/Down step.</summary>
    public const int Octave = 12;

    /// <summary>The scale steps of one octave (a seven-note scale).</summary>
    public const int OctaveSteps = 7;

    private const int MaxScaleSteps = 48, MaxChips = 3;

    private bool _scaleSteps;

    /// <param name="pitches">The selected notes' pitches (any order, repeats allowed).</param>
    /// <param name="scaleName">The scale's display name ('D minor'), null when no scale is picked.</param>
    /// <param name="scaleLock">The Lock to scale setting: the snap tick starts on it (and scale-steps moves follow it as before).</param>
    public PianoRollTransposeDialog(int count, IReadOnlyList<int> pitches, int initial, bool scaleSteps, IReadOnlySet<int>? scale, bool scaleLock, string? scaleName = null)
    {
        Count = count;
        var all = pitches.Count == 0 ? new[] { 60 } : pitches.Select(PianoRollEdits.ClampPitch).ToArray();
        Pitches = all.Distinct().Order().ToList();
        Low = Pitches[0];
        High = Pitches[^1];
        Scale = scale is { Count: > 0 } ? scale : null;
        ScaleName = scaleName;
        ScaleLock = scaleLock;
        SnapToKey = scaleLock;
        _scaleSteps = scaleSteps;
        Delta = Clamp(initial);
    }

    /// <summary>The older form: just the lowest and highest pitch.</summary>
    public PianoRollTransposeDialog(int count, int low, int high, int initial, bool scaleSteps, IReadOnlySet<int>? scale, bool scaleLock)
        : this(count, low == high ? new[] { low } : new[] { low, Math.Max(high, low) }, initial, scaleSteps, scale, scaleLock) { }

    /// <summary>How many notes the dialog moves.</summary>
    public int Count { get; }

    /// <summary>The distinct selected pitches, lowest first.</summary>
    public IReadOnlyList<int> Pitches { get; }

    /// <summary>The lowest selected pitch.</summary>
    public int Low { get; }

    /// <summary>The highest selected pitch.</summary>
    public int High { get; }

    /// <summary>The scale highlight's pitch classes (null: no scale picked).</summary>
    public IReadOnlySet<int>? Scale { get; }

    /// <summary>The scale's display name ('D minor'); null with no scale.</summary>
    public string? ScaleName { get; }

    /// <summary>True when a scale is picked, so 'Stay in the key' and the snap tick can exist.</summary>
    public bool HasScale => Scale is not null;

    /// <summary>The Lock to scale setting when the popup opened.</summary>
    public bool ScaleLock { get; }

    /// <summary>The choice 'Stay in the key' (scale steps); false is 'Half steps'. Without a scale it stays false. Changing it keeps the number inside the new range.</summary>
    public bool ScaleSteps
    {
        get => _scaleSteps && HasScale;
        set { _scaleSteps = value && HasScale; Delta = Clamp(Delta); }
    }

    /// <summary>True when the result snaps to the key: the tick in half-step mode (default = Lock to scale), the Lock setting in scale-steps mode. This move only.</summary>
    public bool SnapToKey { get; set; }

    /// <summary>True when the snap tick shows: a scale is picked and Half steps is chosen.</summary>
    public bool SnapVisible => HasScale && !ScaleSteps;

    /// <summary>True when the result is pulled into the key for this move.</summary>
    public bool SnapApplies => HasScale && (ScaleSteps ? ScaleLock : SnapToKey);

    /// <summary>The signed number the dialog holds now (half steps, or steps in 'Stay in the key').</summary>
    public int Delta { get; private set; }

    /// <summary>The octave pills' amount: 12 half steps, or 7 steps in 'Stay in the key'.</summary>
    public int OctaveAmount => ScaleSteps ? OctaveSteps : Octave;

    /// <summary>What Move notes does now: the number, the mode and the snap.</summary>
    public MoveNotesAnswer Answer => new(Delta, ScaleSteps, SnapApplies);

    /// <summary>The window title line.</summary>
    public const string TitleText = "Move notes up or down";

    /// <summary>'3 notes selected' ('1 note selected').</summary>
    public string CountText => string.Create(CultureInfo.InvariantCulture, $"{Count} {(Count == 1 ? "note" : "notes")} selected");

    /// <summary>The line above the stepper: 'Move 26 notes' style kept for older callers.</summary>
    public string HeadingText => string.Create(CultureInfo.InvariantCulture, $"Move {Count} {(Count == 1 ? "note" : "notes")}");

    public const string HalfStepsText = "Half steps", HalfStepsCaption = "every key on the piano", StayText = "Stay in the key", NoScaleCaption = "pick a key first";

    /// <summary>'only notes of D minor', or 'pick a key first' when no scale is picked.</summary>
    public string StayCaption => HasScale ? "only notes of " + (ScaleName ?? "the scale") : NoScaleCaption;

    /// <summary>The big value in words: 'up 1', 'down 2' or 'no change'.</summary>
    public string DeltaWords => Delta == 0 ? "no change" : (Delta > 0 ? "up " : "down ") + Math.Abs(Delta).ToString(CultureInfo.InvariantCulture);

    /// <summary>The caption under the value: 'step' / 'steps' (Stay in the key) or 'half step' / 'half steps'.</summary>
    public string UnitCaption => (ScaleSteps ? "step" : "half step") + (Math.Abs(Delta) == 1 ? "" : "s");

    public string SnapText => "Snap the result to the key (" + (ScaleName ?? "scale") + ")";

    public const string SnapCaption = "Notes that land outside the key move to the nearest key note";

    public const string WarningLine = "Some notes would go out of range";

    /// <summary>The note names of the lowest (up to three) distinct pitches, before the move.</summary>
    public IReadOnlyList<string> BeforeChips => Pitches.Take(MaxChips).Select(PianoRollPositionText.PitchName).ToList();

    /// <summary>Where those pitches land with the number, mode and snap now.</summary>
    public IReadOnlyList<string> AfterChips => Pitches.Take(MaxChips).Select(p => PianoRollPositionText.PitchName(Apply(p))).ToList();

    /// <summary>The pitch the number, mode and snap give a selected pitch (the popup's chips and the edit agree).</summary>
    public int Landing(int pitch) => Apply(pitch);

    /// <summary>The amber line shown while a note would hit the MIDI range 0-127; empty otherwise.</summary>
    public string WarningText => Clamped ? WarningLine : "";

    private bool Clamped
    {
        get
        {
            if (Delta == 0) return false;
            if (!ScaleSteps) return Delta < -Low || Delta > MidiLimits.Max - High;
            var sign = Math.Sign(Delta);
            var edge = sign > 0 ? High : Low;
            return PianoRollScaleLock.Step(edge, Scale, Delta) == PianoRollScaleLock.Step(edge, Scale, Delta + sign);
        }
    }

    /// <summary>Moves the amount by <paramref name="step"/> (-/+ buttons, octave pills, Up/Down arrows, wheel).</summary>
    public void Add(int step) => Delta = Clamp(Delta + step);

    /// <summary>Holds <paramref name="value"/> (a typed number).</summary>
    public void SetDelta(int value) => Delta = Clamp(value);

    /// <summary>The signed number as the box shows it ('+3' style).</summary>
    public static string Signed(int value) =>
        value > 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    private int Clamp(int value) => Math.Clamp(value, ScaleSteps ? -MaxScaleSteps : -MidiLimits.Max, ScaleSteps ? MaxScaleSteps : MidiLimits.Max);

    // Where a pitch lands with the dialog's number, exactly like TransposeAsync (steps or half steps, then the snap).
    private int Apply(int pitch)
    {
        var moved = ScaleSteps
            ? PianoRollScaleLock.Step(pitch, Scale, Delta)
            : pitch + Math.Clamp(Delta, -Low, MidiLimits.Max - High);   // the whole selection stops together, as Move does
        return SnapApplies ? PianoRollScaleLock.Snap(moved, Scale) : moved;
    }
}
