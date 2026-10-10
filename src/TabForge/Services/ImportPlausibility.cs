using System.Buffers.Binary;
using System.Text;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the single pass that counts facts a valid imported song cannot contain.
// Does not own: the import conversion.
// Tests: TestImportPlausibility.
/// <summary>
/// One cheap pass over an imported Guitar Pro song that counts facts a valid file cannot produce (a note on a string the track
/// does not have, a pitch outside MIDI, a fret far past any neck, an impossible beat length or tempo, control characters in a name),
/// plus unreadable bytes in a Guitar Pro 3-5 file's own text sections. Those formats have no checksum, so a file with damaged bytes
/// in the middle still opens; this is how that is noticed. It never changes the song. A few odd facts stay silent
/// (<see cref="WarnThreshold"/>); the result is one short open notice.
/// </summary>
public static class ImportPlausibility
{
    /// <summary>A real file with a single odd value must not nag: the notice appears from this many impossible facts.</summary>
    public const int WarnThreshold = 12;

    /// <summary>The frets of any real neck stay below this (Guitar Pro offers up to 24 to 30).</summary>
    internal const int FretCap = 63;

    private static readonly byte[] Gp35Signature = Encoding.ASCII.GetBytes("FICHIER GUITAR PRO v");

    /// <param name="Count">Impossible notes, beats, tempos and names found in the song.</param>
    /// <param name="TextBytes">Unreadable bytes in the file's own text sections (title block, notes, lyrics), Guitar Pro 3-5 only.</param>
    public sealed record Result(int Count, int FirstBar, string FirstTrack, string FirstKind, IReadOnlyDictionary<string, int> ByKind, int TextBytes = 0)
    {
        public static readonly Result None = new(0, 0, "", "", new Dictionary<string, int>());
        public bool ShouldWarn => Count >= WarnThreshold || TextBytes >= WarnThreshold;
    }

    /// <summary>The open notice (a lower-case fragment for the status line, no file paths), or null when the file looks fine.</summary>
    public static string? Notice(SongProject project, byte[]? gp35Bytes = null)
    {
        var result = Scan(project, gp35Bytes);
        if (!result.ShouldWarn) return null;
        var parts = new List<string>();
        if (result.Count >= WarnThreshold)
        {
            var track = result.FirstTrack.Length > 40 ? result.FirstTrack[..40] : result.FirstTrack;
            parts.Add($"{result.Count:N0} notes and beats look impossible (first at bar {result.FirstBar}, track '{track}')");
        }
        if (result.TextBytes >= WarnThreshold) parts.Add($"{result.TextBytes:N0} characters in its text sections are unreadable");
        return $"this file may be damaged: {string.Join("; ", parts)}. It was opened as read";
    }

    public static Result Scan(SongProject project, byte[]? gp35Bytes = null)
    {
        var by = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var count = 0;
        var firstBar = 0;
        var firstTrack = "";
        var firstKind = "";
        var firstOrder = long.MaxValue;   // (bar, track) of the earliest odd fact, so "first" is the first bar in the song, not the first track scanned

        void Hit(string kind, int bar, TrackModel track, int trackIndex)
        {
            count++;
            by[kind] = by.GetValueOrDefault(kind) + 1;
            var order = (long)bar * 1_000 + trackIndex;
            if (order >= firstOrder) return;
            firstOrder = order; firstBar = bar; firstTrack = track.Name; firstKind = kind;
        }

        if (project.Tempo is < InputLimits.MinTempo or > InputLimits.MaxTempo) { count++; by["tempo"] = 1; }
        if (HasControlChars(project.Title) || HasControlChars(project.Artist) || HasControlChars(project.Album)) { count++; by["text"] = by.GetValueOrDefault("text") + 1; }

        for (var t = 0; t < project.Tracks.Count; t++)
        {
            var track = project.Tracks[t];
            if (HasControlChars(track.Name)) Hit("text", 1, track, t);
            var strings = track.StringTunings.Count;
            var drums = track.Kind == TrackKind.Drums;
            for (var m = 0; m < track.Measures.Count; m++)
            {
                var bar = m + 1;
                var measure = track.Measures[m];
                if (measure.TempoChange is { } tc && (tc < InputLimits.MinTempo || tc > InputLimits.MaxTempo)) Hit("tempo", bar, track, t);
                if (measure.TimeSigNum is { } n && (n < 1 || n > InputLimits.MaxTimeSignatureNumerator)) Hit("meter", bar, track, t);
                if (measure.TimeSigDenom is { } d && !IsPowerOfTwo(d, 32)) Hit("meter", bar, track, t);
                if (HasControlChars(measure.SectionName)) Hit("text", bar, track, t);
                ScanCells(measure.Cells);
                if (measure.Voice2Cells.Count > 0) ScanCells(measure.Voice2Cells);

                void ScanCells(List<TabCell> cells)
                {
                    foreach (var cell in cells)
                    {
                        if (cell.Notes.Count == 0 && !cell.IsRest) continue;
                        if (!IsPowerOfTwo(cell.DurationDenominator, 64) || cell.Dots is < 0 or > 2) Hit("duration", bar, track, t);
                        else if (cell.TupletNumerator is < 0 or > 13 || cell.TupletDenominator is < 0 or > 13) Hit("tuplet", bar, track, t);
                        foreach (var point in cell.WhammyPoints)
                            if (Math.Abs(point.Value) > 48 || point.Offset is < 0 or > 60) { Hit("bend", bar, track, t); break; }
                        foreach (var note in cell.Notes)
                        {
                            if (!drums && (note.StringIndex < 0 || note.StringIndex >= Math.Max(1, strings))) Hit("string", bar, track, t);
                            else if (!drums && (note.Fret < 0 || note.Fret > FretCap)) Hit("fret", bar, track, t);
                            else if (note.MidiValue is < 0 or > 127) Hit("pitch", bar, track, t);
                            else if (note.Velocity is < 0 or > 127) Hit("velocity", bar, track, t);
                            else
                                foreach (var point in note.BendPoints)
                                    if (Math.Abs(point.Value) > 48 || point.Offset is < 0 or > 60) { Hit("bend", bar, track, t); break; }
                        }
                    }
                }
            }
        }
        var text = gp35Bytes is null ? 0 : HeaderTextDamage(gp35Bytes);
        return count == 0 && text == 0 ? Result.None : new Result(count, firstBar, firstTrack, firstKind, by, text);
    }

    /// <summary>
    /// Guitar Pro 3-5 only: the title block, notes and lyrics are plain text; a field there that is mostly high bytes and full of
    /// control codes is damage. Stops quietly at anything it does not recognise.
    /// </summary>
    internal static int HeaderTextDamage(byte[] data)
    {
        try
        {
            if (data.Length < 64 || data[0] is < 20 or > 40 || !data.AsSpan(1, Gp35Signature.Length).SequenceEqual(Gp35Signature)) return 0;
            var major = data[1 + Gp35Signature.Length] - '0';
            if (major is < 3 or > 5) return 0;
            var at = 1 + 30;
            var damage = 0;
            bool Field(int length)
            {
                if (length < 0 || length > 65_536 || at + length > data.Length) return false;
                damage += GarbageBytes(data.AsSpan(at, length));
                at += length;
                return true;
            }
            bool Int(out int value)
            {
                value = 0;
                if (at + 4 > data.Length) return false;
                value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
                at += 4;
                return true;
            }
            bool IntByteString()   // int block length, then a length byte and the text (block length - 1 bytes)
            {
                if (!Int(out var block) || block < 1 || block > 65_536 || at + 1 > data.Length) return false;
                at += 1;
                return Field(block - 1);
            }
            for (var i = 0; i < (major >= 5 ? 9 : 8); i++) if (!IntByteString()) return damage;
            if (!Int(out var notices) || notices is < 0 or > 1_000) return damage;
            for (var i = 0; i < notices; i++) if (!IntByteString()) return damage;
            if (major < 5) at++;   // triplet feel
            if (major >= 4)
            {
                if (!Int(out _)) return damage;   // the lyrics' track
                for (var line = 0; line < 5; line++)
                    if (!Int(out _) || !Int(out var length) || !Field(length)) return damage;
            }
            return damage;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Services.Trace.Error(Services.Trace.Import, "import plausibility: " + ex.Message); return 0; }
    }

    /// <summary>
    /// Damaged bytes in one text field, judged in 48-byte windows so a damaged stretch inside a long lyric stands out: a window counts when it is
    /// mostly non-ASCII and holds many C1 / control codes (accents and smart quotes alone never qualify). Returns the non-ASCII bytes of such windows.
    /// </summary>
    private static int GarbageBytes(ReadOnlySpan<byte> text)
    {
        const int Window = 48;
        var damaged = 0;
        for (var start = 0; start + 16 <= text.Length; start += Window)
        {
            var window = text.Slice(start, Math.Min(Window, text.Length - start));
            int high = 0, control = 0;
            foreach (var b in window)
            {
                if (b >= 0x80) high++;
                if (b is >= 0x80 and <= 0x9F || b < 0x20 && b is not (9 or 10 or 13) || b == 0x7F) control++;
            }
            if (high * 2 >= window.Length && control >= 5 && control * 12 >= window.Length) damaged += high;
        }
        return damaged;
    }

    private static bool IsPowerOfTwo(int value, int max) => value >= 1 && value <= max && (value & (value - 1)) == 0;

    private static bool HasControlChars(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var ch in text)
            if (ch < ' ' && ch is not ('\t' or '\n' or '\r')) return true;
        return false;
    }
}
