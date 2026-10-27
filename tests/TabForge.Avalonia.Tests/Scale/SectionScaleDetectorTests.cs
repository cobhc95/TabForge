using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the scale detection on synthetic sections (C major, A minor with a leading tone, a minor pentatonic riff, blues, an ambiguous set), the family and
//   common-scale settings, the section and track the readout reads, the detection cache, and the picks (session, kept in the song, back to auto).
// Does not own: the popup and the status bar (StatusBarScaleTests).
// Tests: this class.
public sealed class SectionScaleDetectorTests
{
    private const int C = 0, D = 2, Eb = 3, E = 4, F = 5, Fs = 6, G = 7, Gs = 8, A = 9, B = 11;

    private static readonly string[] WithHarmonic = { "Major", "Natural Minor", "Major Pentatonic", "Minor Pentatonic", "Blues", "Harmonic Minor", "Melodic Minor" };
    private static readonly string[] WithoutHarmonic = { "Major", "Natural Minor", "Major Pentatonic", "Minor Pentatonic", "Blues", "Melodic Minor" };

    /// <summary>A track whose fret numbers are the pitches themselves (one string tuned to 0), with the notes written as eighths, 16 to a bar.</summary>
    private static TrackModel Track(string name, IEnumerable<int> pitches, int bars = 4, int velocity = 100)
    {
        var track = new TrackModel { Name = name, StringTunings = new List<int> { 0 } };
        for (var bar = 0; bar < bars; bar++) track.Measures.Add(new MeasureModel { Number = bar + 1 });
        var list = pitches.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var cell = track.Measures[i / 16 % bars].Cells[i % 16];
            cell.DurationDenominator = 8;
            cell.Notes.Add(new TabNote { StringIndex = 0, Fret = list[i] + 48, Velocity = velocity });
        }
        return track;
    }

    private static SongProject Song(params TrackModel[] tracks)
    {
        var song = new SongProject();
        song.Tracks.AddRange(tracks);
        return song;
    }

    private static ScaleMatch Best(SongProject song, string[] scales, bool preferCommon)
    {
        var histogram = SectionScaleDetector.Collect(song, song.Tracks, 0, 3);
        return SectionScaleDetector.Rank(histogram, scales, preferCommon)[0];
    }

    [Fact]
    public void ACMajorMelodyIsCMajor()
    {
        var song = Song(Track("Lead", new[] { C, C, D, E, F, G, E, C, A, G, F, E, D, B, C, C }));
        var best = Best(song, WithHarmonic, preferCommon: true);
        Assert.Equal(("C", "Major"), (MusicTheoryService.NoteNames[best.Root], best.Scale));
        Assert.Equal("C major", best.Display());
    }

    [Fact]
    public void AMinorWithALeadingToneIsHarmonicMinorWhenEnabledElseMinor()
    {
        var song = Song(Track("Lead", new[] { A, B, C, D, E, F, E, Gs, A, F, E, A, C, F, B, A }));
        var harmonic = Best(song, WithHarmonic, preferCommon: true);
        Assert.Equal(("A", "Harmonic Minor"), (MusicTheoryService.NoteNames[harmonic.Root], harmonic.Scale));
        var plain = Best(song, WithoutHarmonic, preferCommon: true);
        Assert.Equal(("A", "Natural Minor"), (MusicTheoryService.NoteNames[plain.Root], plain.Scale));
    }

    [Fact]
    public void AMinorPentatonicRiffIsPentatonicOrMinorAndPrefersMinorWhenCommonIsPreferred()
    {
        var song = Song(Track("Riff", new[] { A, C, D, A, E, D, C, A, A, C, D, E, G, E, D, A }));
        var exact = Best(song, WithHarmonic, preferCommon: false);
        Assert.Equal(("A", "Minor Pentatonic"), (MusicTheoryService.NoteNames[exact.Root], exact.Scale));
        var common = Best(song, WithHarmonic, preferCommon: true);
        Assert.Equal("A", MusicTheoryService.NoteNames[common.Root]);
        Assert.Contains(common.Scale, new[] { "Minor Pentatonic", "Natural Minor" });
    }

    [Fact]
    public void ABluesRiffWithTheFlatFiveIsBlues()
    {
        var song = Song(Track("Riff", new[] { A, C, D, Eb, E, G, A, A, C, D, Eb, E, D, C, A, A }));
        var best = Best(song, WithHarmonic, preferCommon: true);
        Assert.Equal(("A", "Blues"), (MusicTheoryService.NoteNames[best.Root], best.Scale));
    }

    [Fact]
    public void AnAmbiguousSetNamesACommonScale()
    {
        var song = Song(Track("Pad", new[] { C, D, E, G, A, C, D, E, G, A, C, D, E, G, A, G }));   // C D E G A: one pentatonic set for C major and A minor
        var best = Best(song, WithHarmonic, preferCommon: true);
        Assert.Contains(best.Scale, new[] { "Major", "Natural Minor" });
    }

    [Fact]
    public void ADisabledFamilyIsNeverNamedAndConfidencesSumToOne()
    {
        var song = Song(Track("Lead", new[] { C, D, E, F, G, A, B, C }));
        var histogram = SectionScaleDetector.Collect(song, song.Tracks, 0, 3);
        var ranked = SectionScaleDetector.Rank(histogram, ScaleFamilies.ScalesOf(new[] { ScaleFamilies.Minor }), preferCommon: true);
        Assert.All(ranked, m => Assert.Equal("Natural Minor", m.Scale));
        Assert.Equal(1, ranked.Sum(m => m.Confidence), 6);
        Assert.Equal(12, ranked.Count);
    }

    [Fact]
    public void TiedNotesDeadNotesAndDrumsDoNotCountAndLongLoudNotesWeighMore()
    {
        var lead = Track("Lead", new[] { C, E, G, C }, bars: 1);
        var before = SectionScaleDetector.Collect(Song(lead), lead.Yield(), 0, 0);
        lead.Measures[0].Cells[4].Notes.Add(new TabNote { StringIndex = 0, Fret = F + 48, Tied = true });
        lead.Measures[0].Cells[5].Notes.Add(new TabNote { StringIndex = 0, Fret = F + 48, Dead = true });
        var after = SectionScaleDetector.Collect(Song(lead), lead.Yield(), 0, 0);
        Assert.Equal(before.Weights[F], after.Weights[F]);
        Assert.Equal(before.NoteCount, after.NoteCount);
        var drums = new TrackModel { Name = "Drums", Kind = TrackKind.Drums, StringTunings = new List<int> { 0 } };
        drums.Measures.Add(new MeasureModel());
        drums.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 36 });
        Assert.Equal(0, SectionScaleDetector.Collect(Song(drums), drums.Yield(), 0, 0).NoteCount);
        var quiet = Track("Quiet", new[] { C }, bars: 1, velocity: 30);
        var loud = Track("Loud", new[] { C }, bars: 1, velocity: 120);
        Assert.True(SectionScaleDetector.Collect(Song(loud), loud.Yield(), 0, 0).Total > SectionScaleDetector.Collect(Song(quiet), quiet.Yield(), 0, 0).Total);
    }

    /// <summary>Bars 1-4 "Verse" in C major on Lead, bars 5-8 "Chorus" in A minor on Lead; Bass plays the same minor notes throughout.</summary>
    internal static SongProject TwoSections()
    {
        var verse = new[] { C, D, E, F, G, A, B, C, C, E, G, E, D, F, A, C };
        var chorus = new[] { A, B, C, D, E, F, G, A, A, C, E, C, B, D, F, A };
        var lead = Track("Lead", Enumerable.Range(0, 128).Select(i => i < 64 ? verse[i % 16] : chorus[i % 16]), bars: 8);
        var bass = Track("Bass", Enumerable.Repeat(new[] { A, C, E, A }, 32).SelectMany(x => x), bars: 8);
        var song = Song(lead, bass);
        song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Verse", LengthBars = 4 });
        song.Markers.Add(new MarkerModel { MeasureIndex = 4, Title = "Chorus" });
        return song;
    }

    [Fact]
    public void TheReadoutFollowsTheSectionOfTheBarAndSaysAuto()
    {
        var song = TwoSections();
        var settings = new ScaleReadoutSettings();
        var engine = new ScaleReadoutEngine();
        var verse = engine.Compute(song, 1, song.Tracks[0], settings);
        Assert.Equal("C major ▾ auto", verse.Text);
        Assert.Equal("Verse  ·  Lead  ·  bars 1–4", verse.Context.Text);
        var chorus = engine.Compute(song, 5, song.Tracks[0], settings);
        Assert.Equal("A minor ▾ auto", chorus.Text);
        Assert.Equal("Chorus  ·  Lead  ·  bars 5–8", chorus.Context.Text);
        Assert.Equal(ScaleReadoutEngine.TopCount, chorus.Top.Count);
    }

    [Fact]
    public void ThePositionAndTheTrackChoiceTheNotesRead()
    {
        var song = TwoSections();
        var engine = new ScaleReadoutEngine();
        var settings = new ScaleReadoutSettings { ListenTo = ScaleListenTo.Pitched };
        Assert.Equal("All pitched tracks", engine.Compute(song, 1, song.Tracks[0], settings).Context.TrackLabel);
        Assert.Equal("A minor ▾ auto", engine.Compute(song, 5, song.Tracks[1], new ScaleReadoutSettings()).Text);   // the bass alone: A minor
        var whole = engine.Compute(song, 1, song.Tracks[0], new ScaleReadoutSettings { ReadFrom = ScaleReadFrom.Song });
        Assert.Equal("Whole song  ·  Lead  ·  bars 1–8", whole.Context.Text);
    }

    [Fact]
    public void SongKeyOnlyShowsTheKeySignatureWithoutAutoAndAFlatKeyUsesFlats()
    {
        var song = TwoSections();
        var engine = new ScaleReadoutEngine();
        var key = new ScaleReadoutSettings { ReadFrom = ScaleReadFrom.Key };
        Assert.Equal("C major ▾", engine.Compute(song, 5, song.Tracks[0], key).Text);
        song.KeySignature = -3;
        Assert.Equal("D# major ▾", engine.Compute(song, 5, song.Tracks[0], key).Text);
        key.MusicalKeyNames = true;
        Assert.Equal("E♭ major ▾", engine.Compute(song, 5, song.Tracks[0], key).Text);
        Assert.Equal("E♭ F G A♭ B♭ C D", string.Join(" ", ScaleFamilies.NotesOf(3, "Major", musical: true)));
        Assert.Equal("D# F G G# A# C D", string.Join(" ", ScaleFamilies.NotesOf(3, "Major")));
        key.MusicalKeyNames = false;
        song.KeySignatureMinor = true;
        Assert.Equal("C minor ▾", engine.Compute(song, 5, song.Tracks[0], key).Text);
        song.KeySignature = 2;
        song.KeySignatureMinor = false;
        Assert.Equal("D major ▾", engine.Compute(song, 5, song.Tracks[0], key).Text);
    }

    [Fact]
    public void NothingIsRecomputedUntilTheSectionTheTrackTheSettingsOrTheNotesChange()
    {
        var song = TwoSections();
        var settings = new ScaleReadoutSettings();
        var engine = new ScaleReadoutEngine();
        engine.Compute(song, 1, song.Tracks[0], settings);
        Assert.Equal(1, engine.DetectionRuns);
        for (var bar = 0; bar < 4; bar++) engine.Compute(song, bar, song.Tracks[0], settings);   // same section and track: cached
        Assert.Equal(1, engine.DetectionRuns);
        engine.Compute(song, 5, song.Tracks[0], settings);
        Assert.Equal(2, engine.DetectionRuns);
        engine.Compute(song, 5, song.Tracks[1], settings);
        Assert.Equal(3, engine.DetectionRuns);
        engine.Compute(song, 5, song.Tracks[1], settings);
        Assert.Equal(3, engine.DetectionRuns);
        settings.PreferCommon = false;
        engine.Compute(song, 5, song.Tracks[1], settings);
        Assert.Equal(4, engine.DetectionRuns);
        song.IsDirty = true;   // a note edit marks the song changed
        engine.Compute(song, 5, song.Tracks[1], settings);
        Assert.Equal(5, engine.DetectionRuns);
    }

    [Fact]
    public void APickSticksToItsSectionAndTrackAndAutoReturnsToDetection()
    {
        var song = TwoSections();
        var settings = new ScaleReadoutSettings();
        var engine = new ScaleReadoutEngine();
        var context = ScaleReadoutEngine.ContextOf(song, 5, song.Tracks[0], settings);
        engine.PickForSession(context, "D Dorian");
        var picked = engine.Compute(song, 6, song.Tracks[0], settings);
        Assert.Equal("D dorian ▾", picked.Text);
        Assert.False(picked.IsAuto);
        Assert.False(picked.PickIsKept);
        Assert.Equal("C major ▾ auto", engine.Compute(song, 1, song.Tracks[0], settings).Text);   // the other section is untouched
        Assert.Equal("A minor ▾ auto", engine.Compute(song, 5, song.Tracks[1], settings).Text);   // so is the other track
        Assert.False(engine.ReturnToAuto(song, context));   // a session pick changes nothing in the song
        Assert.Equal("A minor ▾ auto", engine.Compute(song, 5, song.Tracks[0], settings).Text);
    }

    [Fact]
    public void AKeptPickIsSavedInTheSectionAndSurvivesANewEngine()
    {
        var song = TwoSections();
        var settings = new ScaleReadoutSettings();
        var context = ScaleReadoutEngine.ContextOf(song, 5, song.Tracks[0], settings);
        Assert.True(new ScaleReadoutEngine().KeepPick(song, context, "E Phrygian"));
        Assert.Equal("E Phrygian", song.Markers[1].KeptScales![song.Tracks[0].Id.ToString("N")]);
        var fresh = new ScaleReadoutEngine();
        var result = fresh.Compute(song, 6, song.Tracks[0], settings);
        Assert.Equal("E phrygian ▾", result.Text);
        Assert.True(result.PickIsKept);
        Assert.True(fresh.ReturnToAuto(song, context));
        Assert.Null(song.Markers[1].KeptScales);
        Assert.Equal("A minor ▾ auto", fresh.Compute(song, 6, song.Tracks[0], settings).Text);
        var wholeSong = ScaleReadoutEngine.ContextOf(song, 1, song.Tracks[0], new ScaleReadoutSettings { ReadFrom = ScaleReadFrom.Song });
        Assert.True(fresh.KeepPick(song, wholeSong, "G Major"));
        Assert.Equal("G Major", song.KeptScales![song.Tracks[0].Id.ToString("N")]);
    }

    [Fact]
    public void MidiClipNotesInTheSectionCountToo()
    {
        var song = Song(new TrackModel { Name = "Keys", Kind = TrackKind.Audio });
        song.Tracks[0].Measures.Add(new MeasureModel());
        song.Tracks[0].AudioClips.Add(new AudioClip { Name = "Take", StartSec = 0, SourceLengthSec = 4, FileLengthSec = 4, Notes =
            new List<ClipNote> { new(0, 0.5, 57, 100), new(0.5, 0.5, 60, 100), new(1, 0.5, 64, 100), new(1.5, 0.5, 57, 100) } });
        var histogram = SectionScaleDetector.Collect(song, song.Tracks, 0, 0);
        Assert.Equal(4, histogram.NoteCount);
        Assert.True(histogram.Weights[A] > 0 && histogram.Weights[C] > 0 && histogram.Weights[E] > 0);
    }
}

internal static class TrackExtensions
{
    public static IEnumerable<TrackModel> Yield(this TrackModel track) => new[] { track };
}
