using AlphaTab.Model;

namespace TabForge.Services;

// Owns: the file header of the older binary score formats (.gp3, .gp4, .gp5): version line, song information, lyrics, page setup, tempo, key, the mixer channel table and the order of the sections.
// Does not own: the master bars (LegacyGpWriter.MasterBars), tracks (LegacyGpWriter.Tracks), beats (LegacyGpWriter.Beats) and notes (LegacyGpWriter.Notes), or building the score model (LegacyGpExport).
// Tests: TestLegacyGpExport.
/// <summary>Facts about the song that the score model does not hold: the reverb and chorus send of each track (0..127), in track order.</summary>
internal sealed record LegacyGpSongExtras(IReadOnlyList<int> Reverb, IReadOnlyList<int> Chorus);

/// <summary>
/// Writes a score model as a Guitar Pro 3, 4 or 5 binary file. The field layouts follow the 3-5 reader of alphaTab 1.8.4 (the same layouts the import tests
/// generate); whatever the chosen version cannot hold is simplified or left out and reported to the <see cref="LegacyGpLossLog"/>.
/// </summary>
internal sealed partial class LegacyGpWriter
{
    private readonly LegacyGpBytes _w = new();
    private readonly Score _score;
    private readonly LegacyGpFormat _format;
    private readonly int _version;
    private readonly LegacyGpLossLog _loss;
    private readonly LegacyGpSongExtras _extras;
    private string _track = "whole song";
    private int _bar = -1;

    private bool V4 => _version >= 400;
    private bool V5 => _version >= 500;

    private LegacyGpWriter(Score score, LegacyGpFormat format, LegacyGpLossLog loss, LegacyGpSongExtras extras)
    {
        _score = score; _format = format; _version = LegacyGpFormats.Version(format); _loss = loss; _extras = extras;
    }

    /// <summary>The file bytes for <paramref name="score"/> in <paramref name="format"/>; <paramref name="loss"/> receives everything the format cannot keep.</summary>
    public static byte[] Write(Score score, LegacyGpFormat format, LegacyGpLossLog loss, LegacyGpSongExtras? extras = null) =>
        new LegacyGpWriter(score, format, loss, extras ?? new LegacyGpSongExtras(Array.Empty<int>(), Array.Empty<int>())).Build();

    private void Hit(string feature, string result) => _loss.Hit(feature, result, _track, _bar);

    private byte[] Build()
    {
        AllocateChannels();
        PlanTempos();
        _w.ByteStringPadded($"FICHIER GUITAR PRO v{_version / 100}.{_version % 100:00}", 30);
        WriteInformation();
        if (!V5) _w.Byte(WriteGlobalTripletFeel());
        if (V4) WriteLyrics();
        if (_version >= 510) { _w.Int(100); _w.Int(0); _w.Zeros(11); }          // master volume, reverb and the 10-band equaliser with its gain
        if (V5) WritePageSetup();
        _w.Int((int)Math.Round(_score.Tempo));
        if (_version >= 510) _w.Bool(false);                                       // tempo is shown
        WriteKey();
        WriteChannelTable();
        if (V5) { for (var i = 0; i < 19; i++) _w.Short(-1); _w.Int(0); NoteDirections(); }   // no navigation marks; master reverb
        _w.Int(_score.MasterBars.Count);
        _w.Int(_score.Tracks.Count);
        WriteMasterBars();
        WriteTracks();
        if (V5) _w.Byte(0);
        WriteMeasures();
        return _w.ToArray();
    }

    private void WriteInformation()
    {
        _w.IntByteString(_score.Title); _w.IntByteString(_score.SubTitle); _w.IntByteString(_score.Artist); _w.IntByteString(_score.Album);
        _w.IntByteString(_score.Words);
        if (V5) _w.IntByteString(_score.Music);
        else if (!string.IsNullOrWhiteSpace(_score.Music) && _score.Music != _score.Words) Hit("Separate music and words credits", "one author line is kept (the words credit)");
        _w.IntByteString(_score.Copyright); _w.IntByteString(_score.Tab); _w.IntByteString(_score.Instructions);
        var notices = (_score.Notices ?? "").Split("\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _w.Int(notices.Length);
        foreach (var line in notices) _w.IntByteString(line);
    }

    /// <summary>The 5.x page layout, filled with the defaults the program itself writes (A4, the usual header and footer lines).</summary>
    private void WritePageSetup()
    {
        foreach (var value in new[] { 210, 297, 10, 10, 15, 10, 100 }) _w.Int(value);   // page size, margins, score size in percent
        _w.Short(0x01FF);                                                              // every header and footer line shown
        foreach (var line in new[]
        {
            "%TITLE%", "%SUBTITLE%", "%ARTIST%", "%ALBUM%", "Words by %WORDS%", "Music by %MUSIC%", "Words & Music by %WORDSMUSIC%",
            "Copyright %COPYRIGHT%", "All Rights Reserved - International Copyright Secured", "Page %N%/%P%",
        }) _w.IntByteString(line);
        _w.IntByteString("");                                                          // tempo text
    }

    /// <summary>The single swing flag of a 3.x or 4.x file (on when the first bar swings in eighths); returns it, and reports bars whose feel differs.</summary>
    private int WriteGlobalTripletFeel()
    {
        var on = _score.MasterBars.Count > 0 && _score.MasterBars[0].TripletFeel == TripletFeel.Triplet8th;
        for (var b = 0; b < _score.MasterBars.Count; b++)
            if ((_score.MasterBars[b].TripletFeel == TripletFeel.Triplet8th) != on || _score.MasterBars[b].TripletFeel is not (TripletFeel.NoTripletFeel or TripletFeel.Triplet8th))
            { _bar = b; Hit("Swing feel per bar", $"a {LegacyGpFormats.Extension(_format)} file has one swing setting for the whole song; it follows the first bar"); }
        _bar = -1;
        return on ? 1 : 0;
    }

    private void WriteKey()
    {
        var key = _score.MasterBars.Count > 0 ? (int)_score.MasterBars[0].KeySignature : 0;
        if (V5) { _w.Byte(key); _w.Int(0); }                                           // the key, then the octave
        else { _w.Int(key); if (V4) _w.Byte(0); }
    }

    private void NoteDirections()
    {
        for (var b = 0; b < _score.MasterBars.Count; b++)
            if (_score.MasterBars[b].Directions is { Size: > 0 }) { _bar = b; Hit("Navigation marks (Coda, Segno, D.S., D.C., Fine)", "not written; the repeat signs and endings are kept"); }
        _bar = -1;
    }

    /// <summary>The lyrics of the first track that has any: its number, then five lines each with the bar it starts at. The syllables follow the order of the notes.</summary>
    private void WriteLyrics()
    {
        var tracks = _score.Tracks.Select((t, i) => (Track: t, Index: i)).Where(x => LyricBeats(x.Track).Any()).ToList();
        if (tracks.Count == 0) { _w.Int(0); for (var i = 0; i < 5; i++) { _w.Int(0); _w.IntString(""); } return; }
        var (track, index) = tracks[0];
        if (tracks.Count > 1) Hit("Lyrics on more than one track", "only the first track with lyrics keeps them");
        var beats = LyricBeats(track).ToList();
        var text = new List<string>();
        foreach (var group in beats.OrderBy(x => x.Bar).ThenBy(x => x.Order))
            foreach (var syllable in group.Syllables) text.Add(syllable.Length == 0 ? "_" : syllable);
        _w.Int(index + 1);
        _w.Int(beats.Min(x => x.Bar) + 1); _w.IntString(string.Join(" ", text));
        for (var i = 1; i < 5; i++) { _w.Int(0); _w.IntString(""); }
    }

    private static IEnumerable<(int Bar, int Order, string[] Syllables)> LyricBeats(Track track)
    {
        var staff = track.Staves[0];
        for (var b = 0; b < staff.Bars.Count; b++)
        {
            var order = 0;
            foreach (var beat in staff.Bars[b].Voices[0].Beats)
                if (beat.Lyrics is { Count: > 0 } lines && lines.Any(l => !string.IsNullOrWhiteSpace(l)) && beat.Notes.Count > 0)
                    yield return (b, order++, lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToArray());
        }
    }
}
