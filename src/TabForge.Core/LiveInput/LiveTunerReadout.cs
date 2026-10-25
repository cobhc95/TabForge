using System.Globalization;
using TabForge.Presenters;

namespace TabForge.LiveInput;

// Owns: what the live input strip's tuner shows: the note name, the cents needle position and the "+8 cents · 82.4 Hz · sharp" text, steadied by the
//   tuner presenter's smoothing and hidden (NaN cents) when no clear pitch has come for a while.
// Does not own: the reading (the engine), the note and smoothing rules (TunerPresenter) or the drawing.
// Tests: TabForge.Avalonia.Tests.LiveTunerReadoutTests.
/// <summary>The strip tuner's display state. UI thread only.</summary>
public sealed class LiveTunerReadout
{
    public const string NoNote = "--", InTuneText = "in tune", SharpText = "sharp", FlatText = "flat";
    private readonly TunerPresenter _presenter = new(null);
    private int _shownCents = int.MinValue, _shownHzTenths = int.MinValue;

    /// <summary>The note's name, or <see cref="NoNote"/>.</summary>
    public string Note { get; private set; } = NoNote;
    /// <summary>"+8 cents · 82.4 Hz · sharp"; empty with no pitch.</summary>
    public string Detail { get; private set; } = "";
    /// <summary>Cents from the note (-50..50); NaN with no clear pitch (the needle is hidden).</summary>
    public double Cents { get; private set; } = double.NaN;
    /// <summary>The cents alone for the slim strip ("+8 ¢"); empty with no pitch.</summary>
    public string CentsText { get; private set; } = "";
    public TunerTone Tone => TunerPresenter.ToneForCents(Cents);

    /// <summary>The A4 reference in Hz, clamped to the Preferences range.</summary>
    public double ReferenceHz
    {
        get => _presenter.ReferenceHz;
        set => _presenter.ReferenceHz = Math.Clamp(value, LiveStripRules.MinReferenceHz, LiveStripRules.MaxReferenceHz);
    }

    /// <summary>Takes the engine's latest reading (<paramref name="seq"/> changes with each one); true when what is shown changed.</summary>
    public bool Update(float hz, float clarity, int seq, DateTime utcNow)
    {
        var frame = _presenter.Update(null, true, hz, clarity, seq, utcNow);
        if (!frame.Changed) return false;
        Note = frame.NoteText;
        Cents = frame.NeedleCents;
        if (double.IsNaN(Cents)) { Detail = CentsText = ""; _shownCents = _shownHzTenths = int.MinValue; return true; }
        var cents = (int)Math.Round(Cents); var tenths = (int)Math.Round(hz * 10);
        if (cents != _shownCents || tenths != _shownHzTenths)
        {
            _shownCents = cents; _shownHzTenths = tenths;
            CentsText = LiveStripRules.CentsText(cents);
            Detail = string.Create(CultureInfo.InvariantCulture, $"{cents:+0;-0;0} cents · {tenths / 10.0:0.0} Hz · {Direction(Cents)}");
        }
        return true;
    }

    /// <summary>"in tune" within the in-tune band of the needle's colour rule, else "sharp" or "flat".</summary>
    public static string Direction(double cents) =>
        TunerPresenter.ToneForCents(cents) == TunerTone.InTune ? InTuneText : cents > 0 ? SharpText : FlatText;
}
