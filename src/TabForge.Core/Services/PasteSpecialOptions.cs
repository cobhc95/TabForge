namespace TabForge.Services;

// Owns: the Paste Special choices (explicit answers, repeat count and octave shift, normalised to their ranges) for a score clip (beats or bars) or for a copied timeline range (Range).
// Does not own: the dialog (PasteSpecialPresenter) or the pastes themselves (PasteCommands.PasteSpecial, RangePaste).
// Tests: TimelinePasteTests.

/// <summary>
/// What the Paste Special dialog chooses (docs/COPY_PASTE_DESIGN.md 3.6): every answer is explicit, so no question is asked.
/// <see cref="BeatMode"/> applies to beats clips, <see cref="BarsMode"/> to bars clips; <see cref="Range"/> is set instead when a timeline range is pasted
/// (then <see cref="Repeat"/> is its repeat count) and <see cref="Remember"/> says the options should start the next window.
/// </summary>
public sealed record PasteSpecialOptions(
    int Repeat = 1,
    BeatPasteMode BeatMode = BeatPasteMode.Replace,
    BarsOntoNotesAnswer BarsMode = BarsOntoNotesAnswer.Overwrite,
    int OctaveShift = 0,
    bool KeepStringAndFret = false,
    bool CopyBarSettings = true,
    RangePasteOptions? Range = null,
    bool Remember = false)
{
    public const int MaxRepeat = 99;
    public const int MaxOctaveShift = 2;

    /// <summary>The same choices with the repeat count in 1..99 (1..64 for a range) and the octave shift in -2..+2.</summary>
    public PasteSpecialOptions Normalized() => this with
    {
        Repeat = Math.Clamp(Repeat, 1, Range is null ? MaxRepeat : RangePasteOptions.MaxRepeat),
        OctaveShift = Math.Clamp(OctaveShift, -MaxOctaveShift, MaxOctaveShift),
        Range = Range?.Normalized(),
    };
}
