// Owns: the Paste Special dialog state without UI types: the repeat count (typed or stepped, 1..99; 1..64 for a copied range), the placement mode and its labels
//       for a bars clip, a beats clip or a copied timeline range, the range what / overlap / remember choices and which of them show, the octave shift (-2..+2)
//       and its text, whether the octave steps are on, the two check boxes, and the PasteSpecialOptions they make.
// Does not own: the window, or the pastes (PasteCommands.PasteSpecial, RangePaste, through DocumentEdits).
// Tests: TimelinePasteTests.
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>What the window starts from when a copied timeline range is pasted: the choices to preselect, whether "Remember" starts ticked, and which parts the range has (the window shows only the sections that matter).</summary>
public sealed record RangePasteRequest(RangePasteOptions Initial, bool Remember = false, bool HasTabs = true, bool HasClips = true);

/// <summary>Thread: UI thread only (driven by the Paste Special window).</summary>
public sealed class PasteSpecialPresenter
{
    public const string Title = "Paste special";
    public const string RepeatHint = "copies, one after another (1-99)";
    public const string KeepFingeringText = "Keep string and fret (don't re-finger)";
    public const string KeepFingeringToolTip = "Put each note on the same string and fret, even when the instrument is tuned differently (the pitch follows the target's tuning). Octave shift is then not used.";
    public const string BarSettingsText = "Copy bar settings (time signature, key, tempo and similar)";
    public const string BarSettingsToolTip = "Untick to keep the target bars' own settings.";
    public const string RememberText = "Remember these options for Paste special";
    public static readonly IReadOnlyList<string> RangeModeLabels = new[] { "Overwrite the bars there", "Merge: fill only empty spots", "Insert: push the later bars and clips to the right" };
    public static readonly IReadOnlyList<string> WhatLabels = new[] { "Tabs and clips", "Tabs only", "Clips only" };
    public static readonly IReadOnlyList<string> OverlapLabels = new[] { "Trim the clips already there", "Keep both on a new take lane" };

    private readonly bool _bars;
    private readonly RangePasteRequest? _range;

    public PasteSpecialPresenter(bool bars)
    {
        _bars = bars;
        ModeLabels = bars
            ? new[] { "Overwrite the bars", "Insert before (all tracks)", "Insert after (all tracks)" }
            : new[] { "Replace the notes at the cursor", "Insert and push the following notes along (this track only)" };
    }

    /// <summary>The presenter for a copied timeline range, starting from <paramref name="request"/>.</summary>
    public PasteSpecialPresenter(RangePasteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _range = request;
        var initial = request.Initial.Normalized();
        ModeLabels = RangeModeLabels;
        Mode = (int)initial.Mode;
        What = (int)initial.What;
        Overlap = (int)initial.Overlap;
        Repeat = initial.Repeat;
        Remember = request.Remember;
    }

    public bool IsRange => _range is not null;
    public bool HasBarSettings => _bars;
    public string ModeHeading => IsRange ? "Pasting copied bars" : _bars ? "Pasting whole bars" : "Pasting beats";
    public IReadOnlyList<string> ModeLabels { get; }

    public int Repeat { get; private set; } = 1;
    public int Mode { get; set; }
    public int Octave { get; private set; }
    public bool KeepStringAndFret { get; set; }
    public bool CopyBarSettings { get; set; } = true;

    /// <summary>Range only: which parts are pasted (index into <see cref="WhatLabels"/>), what an overlapped clip does (<see cref="OverlapLabels"/>), and whether the choices start the next window.</summary>
    public int What { get; set; }
    public int Overlap { get; set; }
    public bool Remember { get; set; }

    /// <summary>The sections of a range paste: "what" only when the range has both tabs and clips; the clip overlap only when it has clips, and not for Merge (which never overlaps).</summary>
    public bool ShowWhat => _range is { HasTabs: true, HasClips: true };
    public bool ShowOverlap => _range is { HasClips: true };
    public bool OverlapEnabled => Mode != (int)RangePasteMode.Merge;

    /// <summary>The octave shift, string and fret, and bar settings belong to a score clip; a copied range pastes bar for bar.</summary>
    public bool ShowNoteOptions => !IsRange;

    public int RepeatMax => IsRange ? RangePasteOptions.MaxRepeat : PasteSpecialOptions.MaxRepeat;
    public string RepeatHintText => IsRange ? $"copies, one after another (1-{RepeatMax})" : RepeatHint;

    /// <summary>Only digits may be typed or pasted into the repeat box.</summary>
    public static bool IsDigits(string text) => text.All(char.IsDigit);

    /// <summary>Reads the repeat box: unreadable text counts as 1, and the count stays in 1..<see cref="RepeatMax"/>.</summary>
    public void SetRepeatText(string text) => Repeat = int.TryParse(text, out var n) ? Math.Clamp(n, 1, RepeatMax) : 1;

    public void StepRepeat(int by) => Repeat = Math.Clamp(Repeat + by, 1, RepeatMax);

    public void SetOctave(int value) => Octave = Math.Clamp(value, -PasteSpecialOptions.MaxOctaveShift, PasteSpecialOptions.MaxOctaveShift);

    public string OctaveText => Octave == 0 ? "none" : $"{(Octave > 0 ? "+" : "−")}{Math.Abs(Octave)} octave{(Math.Abs(Octave) == 1 ? "" : "s")}";

    /// <summary>Keeping string and fret turns the octave shift off.</summary>
    public bool OctaveOn => !KeepStringAndFret;
    public bool OctaveDownEnabled => OctaveOn && Octave > -PasteSpecialOptions.MaxOctaveShift;
    public bool OctaveUpEnabled => OctaveOn && Octave < PasteSpecialOptions.MaxOctaveShift;

    /// <summary>The choices as they stand now.</summary>
    public PasteSpecialOptions Result()
    {
        if (IsRange)
        {
            var chosen = new RangePasteOptions((RangePasteMode)Mode, (RangePasteWhat)What, (RangeClipOverlap)Overlap, Repeat).Normalized();
            return new PasteSpecialOptions(chosen.Repeat, Range: chosen, Remember: Remember).Normalized();
        }
        return new PasteSpecialOptions(
            Repeat,
            Mode == 1 && !_bars ? BeatPasteMode.Insert : BeatPasteMode.Replace,
            _bars ? Mode switch { 1 => BarsOntoNotesAnswer.InsertBefore, 2 => BarsOntoNotesAnswer.InsertAfter, _ => BarsOntoNotesAnswer.Overwrite } : BarsOntoNotesAnswer.Overwrite,
            KeepStringAndFret ? 0 : Octave,
            KeepStringAndFret,
            !_bars || CopyBarSettings).Normalized();
    }
}
