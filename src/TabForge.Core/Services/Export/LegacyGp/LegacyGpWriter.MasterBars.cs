using AlphaTab.Model;

namespace TabForge.Services;

// Owns: the bar headers of the older binary formats: time signature, repeats, alternate endings, section markers, key and swing feel.
// Does not own: the bars' contents (LegacyGpWriter.Beats) or the tempo changes (LegacyGpWriter.Beats).
// Tests: TestLegacyGpExport.
internal sealed partial class LegacyGpWriter
{
    private void WriteMasterBars()
    {
        int previousNumerator = 0, previousDenominator = 0, previousKey = 0;
        var previousMinor = false;
        var earlierEndings = 0;
        for (var b = 0; b < _score.MasterBars.Count; b++)
        {
            var bar = _score.MasterBars[b];
            _bar = b; _track = "whole song";
            var numerator = (int)bar.TimeSignatureNumerator; var denominator = (int)bar.TimeSignatureDenominator;
            var key = (int)bar.KeySignature; var minor = bar.KeySignatureType == KeySignatureType.Minor;
            var repeatEnd = bar.RepeatCount > 0;
            var endings = (int)bar.AlternateEndings & 0xFF;
            if (bar.IsRepeatStart) earlierEndings = 0;
            var hasSection = bar.Section is { } section && !string.IsNullOrEmpty(section.Text);
            var keyChanged = b == 0 ? key != 0 || minor : key != previousKey || minor != previousMinor;
            var flags = (numerator != previousNumerator ? 0x01 : 0) | (denominator != previousDenominator ? 0x02 : 0) | (bar.IsRepeatStart ? 0x04 : 0)
                | (repeatEnd ? 0x08 : 0) | (endings != 0 ? 0x10 : 0) | (hasSection ? 0x20 : 0) | (keyChanged ? 0x40 : 0);
#pragma warning disable CS0618 // alphaTab 1.8 marks the double bar obsolete, but its model still carries it and the older formats store it.
            if (bar.IsDoubleBar) flags |= 0x80;
#pragma warning restore CS0618
            _w.Byte(flags);
            if ((flags & 0x01) != 0) _w.Byte(numerator);
            if ((flags & 0x02) != 0) _w.Byte(denominator);
            if (repeatEnd) _w.Byte(V5 ? (int)bar.RepeatCount : Math.Max(1, (int)bar.RepeatCount - 1));   // 5.x counts the plays, the older formats the repeats
            if (!V5 && endings != 0) _w.Byte(EndingNumber(endings, earlierEndings, b));
            if (hasSection) { _w.IntByteString(bar.Section?.Text); _w.Byte(255); _w.Byte(0); _w.Byte(0); _w.Byte(0); }
            if (keyChanged) { _w.Byte(key); _w.Byte(minor ? 1 : 0); }
            if (V5)
            {
                if ((flags & 0x03) != 0) _w.Bytes(new byte[] { 2, 2, 2, 2 });         // how eighth notes are beamed
                _w.Byte(endings);
                _w.Byte(FeelByte(bar.TripletFeel));
                _w.Byte(0);
            }
            earlierEndings |= endings;
            NoteBarLosses(bar);
            previousNumerator = numerator; previousDenominator = denominator; previousKey = key; previousMinor = minor;
        }
        _bar = -1;
    }

    /// <summary>The 3.x and 4.x ending number: endings 1 up to the highest one this bar belongs to, less those earlier bars already hold.</summary>
    private int EndingNumber(int endings, int earlier, int bar)
    {
        var highest = 0;
        for (var i = 0; i < 8; i++) if ((endings & (1 << i)) != 0) highest = i + 1;
        var rebuilt = ((1 << highest) - 1) & ~earlier;
        if (rebuilt != endings) { _bar = bar; Hit("Alternate endings that skip a number", "the ending covers every number up to the highest one"); }
        return highest;
    }

    private int FeelByte(TripletFeel feel)
    {
        if (feel is TripletFeel.Triplet8th) return 1;
        if (feel is TripletFeel.Triplet16th) return 2;
        if (feel != TripletFeel.NoTripletFeel) Hit("Dotted and Scottish swing feels", "written as straight time");
        return 0;
    }

    private void NoteBarLosses(MasterBar bar)
    {
        if (bar.IsAnacrusis) Hit("Pickup bar mark", "not written; the bar keeps its length");
        if (bar.IsFreeTime) Hit("Free time bars", "written as bars with their time signature");
        if (bar.Fermata is { } fermatas && fermatas.Keys().Any()) Hit("Fermata", "not written");
        if (bar.Section is { } section && !string.IsNullOrEmpty(section.Marker) && string.IsNullOrEmpty(section.Text)) Hit("Section letter without a name", "not written");
        if (bar.RepeatCount > 255) Hit("Repeat counts above 255", "written as 255");
    }
}
