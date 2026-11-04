using System.Linq;
using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Core rules behind the track properties tuning card: the working strings (TrackTuningDraft), the grouped searchable preset list (TuningPresetMenu),
/// the ring and vibrate rules (StringRinging) and the capo stepper text.
/// Does not own: the window (TrackPropertiesWindowTests). Pure logic, no UI.
/// Tests: TrackTuningDraft, TuningPresetMenu, StringVibration, StringRingLoop, TrackPropertiesPresenter.
/// </summary>
public class TrackTuningDraftTests
{
    private static readonly int[] Standard = { 64, 59, 55, 50, 45, 40 };

    private static TrackModel Guitar(params (int String, int Fret)[] notes)
    {
        var track = new TrackModel { Kind = TrackKind.Guitar, StringTunings = Standard.ToList(), NumberOfFrets = 24 };
        var measure = new MeasureModel();
        track.Measures.Add(measure);
        for (var i = 0; i < notes.Length; i++)
            measure.Cells[i].Notes.Add(new TabNote { StringIndex = notes[i].String, Fret = notes[i].Fret, MidiValue = track.PitchOf(notes[i].String, notes[i].Fret) });
        return track;
    }

    private static TabNote Note(TrackModel track, int cell) => track.Measures[0].Cells[cell].Notes[0];

    [Fact]
    public void StringCountStaysBetweenThreeAndTen()
    {
        var draft = new TrackTuningDraft(Standard);
        draft.ResizeTo(99);
        Assert.Equal(10, draft.Count);
        draft.AddLow();
        draft.AddHigh();
        Assert.Equal(10, draft.Count);
        draft.ResizeTo(0);
        Assert.Equal(3, draft.Count);
        draft.RemoveLow();
        draft.RemoveHigh();
        Assert.Equal(3, draft.Count);
    }

    [Fact]
    public void AddedLowStringIsAFourthBelowAndThicker()
    {
        var draft = new TrackTuningDraft(Standard);
        draft.AddLow();
        Assert.Equal(35, draft.Tunings[^1]);
        Assert.True(TrackPropertiesPresenter.StringThickness(35) > TrackPropertiesPresenter.StringThickness(40));
        Assert.Equal(-1, draft.Origin[^1]);
    }

    [Fact]
    public void AddingAHighStringKeepsEveryNoteOnItsOwnString()
    {
        var track = Guitar((0, 3), (5, 2));
        var draft = new TrackTuningDraft(track.StringTunings);
        var pitches = new[] { Note(track, 0).MidiValue, Note(track, 1).MidiValue };
        draft.AddHigh();
        draft.Commit(track, keepFrets: true);

        Assert.Equal(7, track.StringTunings.Count);
        Assert.Equal(69, track.StringTunings[0]);
        Assert.Equal((1, 3, pitches[0]), (Note(track, 0).StringIndex, Note(track, 0).Fret, Note(track, 0).MidiValue));
        Assert.Equal((6, 2, pitches[1]), (Note(track, 1).StringIndex, Note(track, 1).Fret, Note(track, 1).MidiValue));
    }

    [Fact]
    public void RemovingTheLowStringWarnsAndMovesNotesToTheFinalTuning()
    {
        var track = Guitar((5, 5), (5, 0), (0, 3));
        var draft = new TrackTuningDraft(track.StringTunings);
        var atStake = draft.NotesAtStake(track, new[] { draft.Count - 1 });
        Assert.Equal(2, atStake.Total);
        Assert.Equal(1, atStake.Unplayable);   // E2 is below every remaining string
        draft.RemoveLow();
        draft.Commit(track, keepFrets: true);

        Assert.Equal(new[] { 64, 59, 55, 50, 45 }, track.StringTunings);
        Assert.Equal((4, 0, 45), (Note(track, 0).StringIndex, Note(track, 0).Fret, Note(track, 0).MidiValue));
        Assert.Equal((4, 0, 45), (Note(track, 1).StringIndex, Note(track, 1).Fret, Note(track, 1).MidiValue));
        Assert.Equal((0, 3, 67), (Note(track, 2).StringIndex, Note(track, 2).Fret, Note(track, 2).MidiValue));
        for (var i = 0; i < 3; i++)
        {
            var n = Note(track, i);
            Assert.Equal(n.MidiValue, track.PitchOf(n.StringIndex, n.Fret));
        }
    }

    [Fact]
    public void RemovingTheHighStringMovesItsNotesAndShiftsTheOthersUp()
    {
        var track = Guitar((0, 5), (1, 0), (5, 2));
        var draft = new TrackTuningDraft(track.StringTunings);
        var pitches = Enumerable.Range(0, 3).Select(i => Note(track, i).MidiValue).ToArray();
        draft.RemoveHigh();
        draft.Commit(track, keepFrets: true);

        Assert.Equal(5, track.StringTunings.Count);
        Assert.Equal(new[] { 59, 55, 50, 45, 40 }, track.StringTunings);
        Assert.Equal((0, 10, 69), (Note(track, 0).StringIndex, Note(track, 0).Fret, Note(track, 0).MidiValue));
        Assert.Equal(4, Note(track, 2).StringIndex);
        for (var i = 0; i < 3; i++) Assert.Equal(pitches[i], track.PitchOf(Note(track, i).StringIndex, Note(track, i).Fret));
    }

    [Fact]
    public void ARetuneWithoutStructureChangeKeepsTodaysRule()
    {
        var track = Guitar((0, 3));
        var draft = new TrackTuningDraft(track.StringTunings);
        draft.Step(0, 2);
        draft.Commit(track, keepFrets: true);
        Assert.Equal((0, 3, 66 + 3), (Note(track, 0).StringIndex, Note(track, 0).Fret, Note(track, 0).MidiValue));

        var other = Guitar((0, 3));
        var keepPitch = new TrackTuningDraft(other.StringTunings);
        keepPitch.Step(0, -2);
        keepPitch.Commit(other, keepFrets: false);
        Assert.Equal((67, 5), (Note(other, 0).MidiValue, Note(other, 0).Fret));
    }

    [Fact]
    public void RetuningUpKeepsFretsOrRefretsTheSamePitch()
    {
        var keepFrets = Guitar((0, 3));
        var standard = new TrackTuningDraft(keepFrets.StringTunings);
        standard.Step(0, 2);
        standard.Commit(keepFrets, keepFrets: true);
        Assert.Equal(new[] { 66, 59, 55, 50, 45, 40 }, keepFrets.StringTunings);
        Assert.Equal((0, 3, 69), (Note(keepFrets, 0).StringIndex, Note(keepFrets, 0).Fret, Note(keepFrets, 0).MidiValue));

        var keepPitch = Guitar((0, 3));
        var refret = new TrackTuningDraft(keepPitch.StringTunings);
        refret.Step(0, 2);
        refret.Commit(keepPitch, keepFrets: false);
        Assert.Equal((0, 1, 67), (Note(keepPitch, 0).StringIndex, Note(keepPitch, 0).Fret, Note(keepPitch, 0).MidiValue));
        Assert.Equal(67, keepPitch.PitchOf(0, 1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemovingALowStringAndRetuningTheNextUsesTheFinalTuning(bool keepFrets)
    {
        var track = Guitar((5, 5));   // A2 at fret 5 of the low E
        var draft = new TrackTuningDraft(track.StringTunings);
        draft.RemoveLow();
        draft.Step(draft.Count - 1, -2);   // A2 string becomes G2
        draft.Commit(track, keepFrets);

        Assert.Equal(new[] { 64, 59, 55, 50, 43 }, track.StringTunings);
        Assert.Equal((4, 2, 45), (Note(track, 0).StringIndex, Note(track, 0).Fret, Note(track, 0).MidiValue));
        Assert.Equal(45, track.PitchOf(Note(track, 0).StringIndex, Note(track, 0).Fret));
    }

    [Fact]
    public void TheWarningAndTheMoveBothUseTheFinalRetunedDestination()
    {
        var track = Guitar((5, 3), (5, 0));   // G2 and E2 on the low E
        var draft = new TrackTuningDraft(track.StringTunings);
        draft.Step(4, -2);                    // A2 string becomes G2
        var atStake = draft.NotesAtStake(track, new[] { 5 });
        Assert.Equal(2, atStake.Total);
        Assert.Equal(1, atStake.Unplayable);   // G2 fits on G2 open; E2 does not

        draft.RemoveLow();
        draft.Commit(track, keepFrets: true);
        Assert.Equal((4, 0, 43), (Note(track, 0).StringIndex, Note(track, 0).Fret, Note(track, 0).MidiValue));
        Assert.Equal((4, 0, 43), (Note(track, 1).StringIndex, Note(track, 1).Fret, Note(track, 1).MidiValue));
    }

    [Fact]
    public void RemovedNotesAtStakeFollowsTheFinalRetuningAndNamesTheNewlyUnplayableNote()
    {
        var track = Guitar((5, 5), (5, 7));   // A2 and B2 on the low E
        var draft = new TrackTuningDraft(track.StringTunings);
        var atRemoval = draft.NotesAtStake(track, new[] { draft.Count - 1 });
        Assert.Equal((2, 0), (atRemoval.Total, atRemoval.Unplayable));   // both fit on the A2 string

        draft.RemoveLow();
        draft.Step(draft.Count - 1, 2);   // A2 string becomes B2
        var final = draft.RemovedNotesAtStake(track);
        Assert.Equal((2, 1), (final.Total, final.Unplayable));
        Assert.Same(Note(track, 0), Assert.Single(final.UnplayableNotes));   // A2 is below the new lowest open string

        // The warning copy is independent: changing it cannot change the draft still waiting for OK.
        var probe = draft.Clone();
        probe.Step(probe.Count - 1, -2);
        Assert.Equal(47, draft.Tunings[^1]);
        Assert.Equal(45, probe.Tunings[^1]);
    }

    [Fact]
    public void RemovalWarningDisclosesTheAffectedCountAndUnavoidablePitchChange()
    {
        var samePitch = TrackPropertiesPresenter.RemovalWarningText(2, 0);
        Assert.Contains("2 notes are affected", samePitch);
        Assert.Contains("same pitch", samePitch);
        Assert.DoesNotContain("pitch will change", samePitch);

        var someChange = TrackPropertiesPresenter.RemovalWarningText(2, 1);
        Assert.Contains("2 notes are affected", someChange);
        Assert.Contains("1 note cannot keep its pitch", someChange);
        Assert.Contains("its pitch will change", someChange);
        Assert.Contains("the other 1 keeps its pitch", someChange);

        var allChange = TrackPropertiesPresenter.RemovalWarningText(1, 1);
        Assert.Contains("1 note is affected", allChange);
        Assert.Contains("No remaining string can play its pitch", allChange);
        Assert.Contains("its pitch will change", allChange);

        var later = TrackPropertiesPresenter.UndisclosedPitchChangeWarningText(1);
        Assert.Contains("1 note", later);
        Assert.Contains("removal warning did not mention", later);
        Assert.Contains("pitch will change", later);
    }

    [Fact]
    public void KeepPitchThatIsNoLongerPlayableUsesTheNearestFret()
    {
        var track = Guitar((0, 0));   // open high E
        var draft = new TrackTuningDraft(track.StringTunings);
        draft.Step(0, 2);             // F#4: MIDI 64 is below the open string
        draft.Commit(track, keepFrets: false);

        Assert.Equal((0, 0, 66), (Note(track, 0).StringIndex, Note(track, 0).Fret, Note(track, 0).MidiValue));
        Assert.Equal(66, track.PitchOf(0, 0));
    }

    [Fact]
    public void RemovedStringNotesKeepTiesAndTheirSlideTrillTargetsWhenPitchIsPlayable()
    {
        var track = Guitar((0, 5), (0, 5));
        var origin = Note(track, 0);
        origin.SlideTargetMidi = 72;
        origin.TrillTargetMidi = 74;
        Note(track, 1).Tied = true;

        var draft = new TrackTuningDraft(track.StringTunings);
        draft.RemoveHigh();
        draft.Commit(track, keepFrets: true);

        Assert.Equal((0, 10, 69), (Note(track, 0).StringIndex, Note(track, 0).Fret, Note(track, 0).MidiValue));
        Assert.Equal((72, 74), (Note(track, 0).SlideTargetMidi, Note(track, 0).TrillTargetMidi));
        Assert.True(Note(track, 1).Tied);
        Assert.Equal((0, 10, 69), (Note(track, 1).StringIndex, Note(track, 1).Fret, Note(track, 1).MidiValue));
    }

    [Fact]
    public void APresetWithAnotherCountResizesAtTheLowEnd()
    {
        var draft = new TrackTuningDraft(Standard);
        draft.Apply(new[] { 64, 59, 55, 50, 45, 40, 35 });
        Assert.Equal(7, draft.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, -1 }, draft.Origin);
        var dropped = draft.SlotsDroppedBy(5);
        Assert.Equal(new[] { 5, 6 }, dropped);
        draft.Apply(new[] { 64, 59, 55, 50, 45 });
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, draft.Origin);
    }

    [Fact]
    public void PresetMenuFiltersByCountSearchesAndGroupsByInstrument()
    {
        var six = TuningPresetMenu.Groups("", 6, allCounts: false, bass: false);
        Assert.Equal("Guitar", six[0].Name);
        Assert.Equal("Bass", six[1].Name);
        Assert.Contains(six, g => g.Name == TuningPresetMenu.LapSteelGroup);
        Assert.All(six.SelectMany(g => g.Entries), e => Assert.Equal(6, e.Strings));
        Assert.Equal("Bass", TuningPresetMenu.Groups("", 6, false, bass: true)[0].Name);

        var drop = TuningPresetMenu.Groups("drop", 6, false, false);
        Assert.All(drop.SelectMany(g => g.Entries), e => Assert.Contains("Drop", e.Name));
        Assert.DoesNotContain(drop.SelectMany(g => g.Entries), e => e.Name == "DADGAD");

        var everything = TuningPresetMenu.Groups("", 6, allCounts: true, bass: false).SelectMany(g => g.Entries);
        Assert.Contains(everything, e => e.Strings == 9);
        Assert.Contains(everything, e => e.Strings == 4);
        Assert.Empty(TuningPresetMenu.Groups("zzzz", 6, true, false));
        Assert.Equal("Drop A (7 strings)", TuningPresetMenu.Groups("drop a", 6, true, false).SelectMany(g => g.Entries).First(e => e.Strings == 7).Text(true));
    }

    [Fact]
    public void AnEditedTuningIsLabelledCustom()
    {
        Assert.Equal("Standard E", TuningPresetMenu.Label(Standard, bass: false));
        var edited = Standard.ToList();
        edited[3] += 1;
        Assert.Equal(TuningPresetMenu.Custom, TuningPresetMenu.Label(edited, bass: false));
        Assert.Equal("Drop D", TuningPresetMenu.Label(new[] { 64, 59, 55, 50, 45, 38 }, bass: false));
    }

    [Fact]
    public void VibrationFadesAndEndsAtRest()
    {
        double Peak(double from, double to) => Enumerable.Range((int)from, (int)(to - from)).Max(ms => System.Math.Abs(StringVibration.Offset(ms)));
        Assert.Equal(0, StringVibration.Offset(-5));
        Assert.Equal(0, StringVibration.Offset(StringVibration.RingMs));
        Assert.True(Peak(0, 200) > Peak(600, 800));
        Assert.True(Peak(600, 800) > Peak(1200, 1500));
        Assert.True(Peak(0, 200) <= StringVibration.PeakDips);
    }

    [Fact]
    public void OnlyOneStringRepeatsAtATime()
    {
        var loop = new StringRingLoop();
        Assert.True(loop.Toggle(2));
        Assert.True(loop.Toggle(4));
        Assert.Equal(4, loop.Looping);
        Assert.False(loop.Toggle(4));
        Assert.Null(loop.Looping);
        loop.Toggle(1);
        loop.Stop();
        Assert.Null(loop.Looping);
    }

    [Theory]
    [InlineData(0, "Off")]
    [InlineData(1, "Fret 1")]
    [InlineData(12, "Fret 12")]
    public void CapoStepperShowsOffOrFret(int capo, string text) => Assert.Equal(text, TrackPropertiesPresenter.CapoText(capo));

    [Fact]
    public void CapoStepStaysBetweenOffAndTwelve()
    {
        Assert.Equal(0, TrackPropertiesPresenter.StepCapo(0, -1));
        Assert.Equal(12, TrackPropertiesPresenter.StepCapo(12, 1));
        Assert.Equal(5, TrackPropertiesPresenter.StepCapo(4, 1));
    }
}
