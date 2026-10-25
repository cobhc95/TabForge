namespace TabForge.KeyboardMode;

// Owns: which keys light because their note is due: from just before a note's onset until its end (at least a short while), per hand, for the notes of a placed page; a hidden hand's notes light nothing.
// Does not own: the notes page (KeyboardModeWindow.Collect), the held keys (KeyboardFeedback) or drawing.
// Tests: TabForge.Avalonia.Tests.KeyboardStripViewTests.
public static class KeyboardDueKeys
{
    /// <summary>A key lights this long before its note's onset, and at least this long in all.</summary>
    public const double LeadMs = 60, MinMs = 100;
    /// <summary>Notes that began longer ago than this are not looked for.</summary>
    public const double ReachMs = 4000;

    /// <summary>Sets in <paramref name="left"/> and <paramref name="right"/> the keys due at virtual time <paramref name="nowV"/>; <paramref name="placed"/> is in virtual onset order.</summary>
    public static void Find(IReadOnlyList<KeyboardModePlaced> placed, double nowV, KeyboardHandsFilter hands, out KeyMask left, out KeyMask right)
    {
        left = default; right = default;
        int lo = 0, hi = placed.Count;
        while (lo < hi) { var mid = (lo + hi) / 2; if (placed[mid].VirtualMs <= nowV + LeadMs) lo = mid + 1; else hi = mid; }
        for (var i = lo - 1; i >= 0; i--)
        {
            var p = placed[i];
            if (p.VirtualMs < nowV - ReachMs) break;
            if (p.VirtualMs + Math.Max(MinMs, p.Note.DurationMs) <= nowV || KeyboardHands.OpacityOf(hands, p.Note.LeftHand) <= 0) continue;
            if (p.Note.LeftHand) left.Set(p.Note.Fret); else right.Set(p.Note.Fret);
        }
    }
}
