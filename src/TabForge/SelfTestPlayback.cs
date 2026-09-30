using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

/// <summary>
/// Deterministic musical-logic regression tests (part of <see cref="SelfTest"/>).
/// These cover the playback compiler, not the devices: ties, tuplets, tempo, channels, loops,
/// seeking, determinism, export and playhead mapping.
/// </summary>
public static partial class SelfTest
{
    private static void Near(string name, double expected, double actual, double tolerance = 0.5) =>
        Check(name, Math.Abs(expected - actual) <= tolerance, $"expected {expected:0.###}, got {actual:0.###}");

    private static SongProject SingleTrack(int bars = 1, int bpm = 120)
    {
        var p = new SongProject { Tempo = bpm };
        p.Tracks.Add(new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(bars) });
        return p;
    }

    private static TabCell Beat(SongProject p, int track, int bar, int cell, int denominator, int midi, int str = 0, int fret = 0)
    {
        var c = p.Tracks[track].Measures[bar].Cells[cell];
        c.DurationDenominator = denominator;
        c.Notes.Add(new TabNote { StringIndex = str, Fret = fret, MidiValue = midi });
        return c;
    }

    private static void TestPlaybackDepth()
    {
        TestTieMerging();
        TestTupletOnsets();
        TestDottedDuration();
        TestTempoChange();
        TestChordSimultaneity();
        TestChannelAllocation();
        TestLoopBounds();
        TestSeekOffsets();
        TestDeterminism();
        TestMetronomeAndVelocity();
        TestLetRing();
        TestRepeatEndings();
        TestPlayheadMapper();
        TestMidiExportTicks();
        TestChordVisualisation();
        TestEditingSelectionVisualisation();
        TestNoteTimelineQueries();
        TestEditorScrollGeometry();
        TestHotkeys();
        TestEngineParity();
        TestLiveArrangementReorder();
        TestLiveMetronomeSettings();
        TestLoopPositionUpdates();
        TestMixFadeAndAudiblePlayhead();
        TestGmLevelCalibration();
        TestDocumentPlaybackSwitchPolicy();
        TestTechniqueTranslation();
        TestReferenceABar56PlaybackState();
        TestPlayheadGeometry();
    }

    private static void TestLiveMetronomeSettings()
    {
        var project = SingleTrack(5, 400);
        var output = new RecordingMidiOutput();
        using var engine = new PlaybackEngine(output);
        var timelineChanges = 0;
        engine.TimelineChanged += _ => Interlocked.Increment(ref timelineChanges);
        engine.Start(project, new PlaybackOptions { LoopStartBar = 1, LoopEndBar = 1 }, _ => { }, () => { });
        var timeline = engine.Timeline;
        var candidateAccents = timeline?.Events.Where(e => e.IsMetronome && e.IsMetronomeAccent)
            .Select(e => e.TimeMs).Distinct().Take(5).ToArray() ?? Array.Empty<double>();
        Check("live metronome timeline contains marked downbeat candidates",
            candidateAccents.Length >= 2, string.Join(",", candidateAccents.Select(time => time.ToString("0.0"))));
        engine.SetMetronomeSettings(true, 100, 100, 76, 33, 34, 1);
        Thread.Sleep(185);
        var classicClick = output.Messages.Any(message => (message.Status & 0xF0) == 0x90 &&
            message.Data1 == 34 && message.Data2 == 123);

        engine.SetMetronomeSettings(true, 50, 100, 50, 76, 77, 4);
        engine.SetLoop(true);
        Thread.Sleep(1450);
        var messages = output.Messages.ToArray();
        var woodblockEvents = messages.Where(message => (message.Status & 0xF0) == 0x90 && message.Data2 > 0 &&
                message.Data1 is 76 or 77)
            .Select(message => $"{message.Data1}:{message.Data2}").ToArray();
        Check("live metronome volume and sound updates preserve the running timeline",
            engine.IsPlaying && ReferenceEquals(timeline, engine.Timeline) && timelineChanges == 1);
        Check("live metronome settings dispatch classic click velocity",
            classicClick, "expected 123-velocity regular click (lifted to full channel level)");
        Check("live metronome settings apply woodblock sound, level and subdivision",
            messages.Any(message => (message.Status & 0xF0) == 0x90 && message.Data1 == 77 && message.Data2 is > 0 and < 84) &&
            messages.Any(message => (message.Status & 0xF0) == 0x90 && message.Data1 == 76 && message.Data2 > 0),
            $"woodblock={string.Join(",", woodblockEvents)}; accents={string.Join(",", candidateAccents.Select(time => time.ToString("0.0")))}");
        Check("loop playback remains active and wraps while metronome controls change",
            engine.IsPlaying && engine.CurrentBar == 1 && ReferenceEquals(timeline, engine.Timeline));
        engine.SetMetronomeSettings(false, 50, 100, 50, 76, 77, 4);
        Check("metronome can be toggled off without restarting playback",
            engine.IsPlaying && ReferenceEquals(timeline, engine.Timeline) && timelineChanges == 1);
    }

    private static void TestLoopPositionUpdates()
    {
        var project = SingleTrack(bars: 3, bpm: 400);
        Beat(project, 0, 0, 0, 4, 60);
        Beat(project, 0, 1, 0, 4, 62);
        var output = new TimedMidiOutput();
        using var engine = new PlaybackEngine(output);
        var positions = new List<double>();
        var positionsGate = new object();
        var startedAt = Stopwatch.GetTimestamp();
        engine.Start(project, new PlaybackOptions
        {
            Loop = true,
            LoopStartBar = 0,
            LoopEndBar = 1,
            Speed = 4
        }, position =>
        {
            lock (positionsGate) positions.Add(position.ElapsedMs);
        }, () => { });

        Thread.Sleep(850);
        engine.Stop();

        double[] reported;
        lock (positionsGate) reported = positions.ToArray();
        var wrap = -1;
        for (var i = 1; i < reported.Length; i++)
        {
            if (reported[i] + 1 < reported[i - 1]) { wrap = i; break; }
        }
        var advancedAfterWrap = wrap >= 0 && reported.Skip(wrap + 1).Take(8).Any(time => time > 40);
        Check("playback position keeps advancing after an area-loop wrap",
            wrap >= 0 && advancedAfterWrap,
            $"positions={reported.Length}; wrapIndex={wrap}; afterWrap={string.Join(",", reported.Skip(Math.Max(0, wrap)).Take(8).Select(time => time.ToString("0")))}");

        var attacks = output.Messages
            .Where(message => (message.Status & 0xF0) == 0x90 && message.Data2 > 0 && message.Data1 is 60 or 62)
            .Select(message => Stopwatch.GetElapsedTime(startedAt, message.Timestamp).TotalMilliseconds)
            .ToArray();
        var largestGap = attacks.Zip(attacks.Skip(1), (first, second) => second - first).DefaultIfEmpty(double.PositiveInfinity).Max();
        Check("loop audio resumes without a scheduler-sized pause at each wrap",
            attacks.Length >= 4 && largestGap < 220,
            $"attacks={attacks.Length}; largest inter-attack gap={largestGap:0.0} ms");
    }

    private static void TestLiveArrangementReorder()
    {
        var project = SingleTrack(bars: 3, bpm: 400);
        Beat(project, 0, 0, 0, 1, 60);
        Beat(project, 0, 1, 0, 4, 65);
        Beat(project, 0, 2, 0, 4, 72);
        project.Markers.AddRange(new[]
        {
            new MarkerModel { MeasureIndex = 0, Title = "Intro" },
            new MarkerModel { MeasureIndex = 1, Title = "Pre-Chorus" },
            new MarkerModel { MeasureIndex = 2, Title = "Chorus" }
        });

        var output = new RecordingMidiOutput();
        using var engine = new PlaybackEngine(output);
        using var finished = new ManualResetEventSlim();
        engine.Start(project, new PlaybackOptions { Speed = 4 }, _ => { }, finished.Set);
        var started = SpinWait.SpinUntil(() => output.Messages.Any(message =>
            (message.Status & 0xF0) == 0x90 && message.Data1 == 60 && message.Data2 > 0), TimeSpan.FromSeconds(2));
        Check("live arrangement reorder: current Intro note has started", started);
        if (!started) return;

        Thread.Sleep(20);
        var mapping = SectionReorderService.Move(project, from: 0, insertBefore: 2);
        if (mapping is not null) engine.RefreshArrangement(project, mapping);
        Check("live arrangement reorder: section move produced its bar mapping", mapping is not null);
        if (mapping is null) return;

        Check("live arrangement reorder: playback finishes after following the new order",
            finished.Wait(TimeSpan.FromSeconds(3)));
        var attacks = output.Messages.Where(message => (message.Status & 0xF0) == 0x90 && message.Data2 > 0)
            .Select(message => message.Data1).Where(pitch => pitch is 60 or 65 or 72).ToArray();
        Check("live arrangement reorder: current note is not restarted and new Chorus follows Intro",
            attacks.SequenceEqual(new[] { 60, 72 }), string.Join(" → ", attacks));
        Check("live arrangement reorder: Intro release from the original bar is preserved",
            output.Messages.Any(message => (message.Status & 0xF0) == 0x80 && message.Data1 == 60));

        var deleteProject = SingleTrack(bars: 3, bpm: 400);
        Beat(deleteProject, 0, 0, 0, 1, 60);
        Beat(deleteProject, 0, 1, 0, 4, 65);
        Beat(deleteProject, 0, 2, 0, 4, 72);
        var intro = new MarkerModel { MeasureIndex = 0, Title = "Intro" };
        deleteProject.Markers.AddRange(new[]
        {
            intro,
            new MarkerModel { MeasureIndex = 1, Title = "Verse" },
            new MarkerModel { MeasureIndex = 2, Title = "Chorus" }
        });
        var deleteOutput = new RecordingMidiOutput();
        using var deleteEngine = new PlaybackEngine(deleteOutput);
        using var deleteFinished = new ManualResetEventSlim();
        deleteEngine.Start(deleteProject, new PlaybackOptions { Speed = 4 }, _ => { }, deleteFinished.Set);
        var deleteStarted = SpinWait.SpinUntil(() => deleteOutput.Messages.Any(message =>
            (message.Status & 0xF0) == 0x90 && message.Data1 == 60 && message.Data2 > 0), TimeSpan.FromSeconds(2));
        Check("live section deletion: current section has started", deleteStarted);
        if (!deleteStarted) return;
        Thread.Sleep(20);
        var removal = SectionReorderService.Delete(deleteProject, intro);
        if (removal is not null)
            deleteEngine.RefreshArrangement(deleteProject, removal.OldToNewBar, removal.ContinueAtBar);
        Check("live section deletion: active section content is removed from the future route",
            removal is not null && removal.OldToNewBar[0] == -1);
        if (removal is null) return;
        Check("live section deletion: remaining sections play after the current bar ends",
            deleteFinished.Wait(TimeSpan.FromSeconds(3)));
        var deleteAttacks = deleteOutput.Messages.Where(message => (message.Status & 0xF0) == 0x90 && message.Data2 > 0)
            .Select(message => message.Data1).Where(pitch => pitch is 60 or 65 or 72).ToArray();
        Check("live section deletion: current note is uninterrupted, then Verse and Chorus continue",
            deleteAttacks.SequenceEqual(new[] { 60, 65, 72 }), string.Join(" → ", deleteAttacks));

        var duplicateProject = SingleTrack(bars: 3, bpm: 400);
        Beat(duplicateProject, 0, 0, 0, 1, 60);
        Beat(duplicateProject, 0, 1, 0, 4, 65);
        Beat(duplicateProject, 0, 2, 0, 4, 72);
        var duplicateIntro = new MarkerModel { MeasureIndex = 0, Title = "Intro" };
        duplicateProject.Markers.AddRange(new[]
        {
            duplicateIntro,
            new MarkerModel { MeasureIndex = 1, Title = "Verse" },
            new MarkerModel { MeasureIndex = 2, Title = "Chorus" }
        });
        var duplicateOutput = new RecordingMidiOutput();
        using var duplicateEngine = new PlaybackEngine(duplicateOutput);
        using var duplicateFinished = new ManualResetEventSlim();
        duplicateEngine.Start(duplicateProject, new PlaybackOptions { Speed = 4 }, _ => { }, duplicateFinished.Set);
        var duplicateStarted = SpinWait.SpinUntil(() => duplicateOutput.Messages.Any(message =>
            (message.Status & 0xF0) == 0x90 && message.Data1 == 60 && message.Data2 > 0), TimeSpan.FromSeconds(2));
        Check("live section duplicate: current section has started", duplicateStarted);
        if (!duplicateStarted) return;
        Thread.Sleep(20);
        var introCopy = new List<List<MeasureModel>> { duplicateProject.Tracks[0].Measures.Take(1).ToList() };
        var insertMap = SectionReorderService.Insert(duplicateProject, 1, introCopy, duplicateIntro);
        var timelineMap = insertMap is null ? Array.Empty<int>()
            : SectionReorderService.ComposeBarRemapWithInsertions(new[] { 0, 1, 2 }, insertMap, 4);
        if (insertMap is not null) duplicateEngine.RefreshArrangement(duplicateProject, timelineMap);
        Check("live section duplicate: copy has its own timeline mapping", timelineMap.SequenceEqual(new[] { 0, 2, 3, 1 }));
        Check("live section duplicate: playback completes through the inserted section", duplicateFinished.Wait(TimeSpan.FromSeconds(3)));
        var duplicateAttacks = duplicateOutput.Messages.Where(message => (message.Status & 0xF0) == 0x90 && message.Data2 > 0)
            .Select(message => message.Data1).Where(pitch => pitch is 60 or 65 or 72).ToArray();
        Check("live section duplicate: original finishes once, then its independent copy plays next",
            duplicateAttacks.SequenceEqual(new[] { 60, 60, 65, 72 }), string.Join(" → ", duplicateAttacks));
    }


    private static DocumentSession StartTabPlaybackSession()
    {
        var project = SingleTrack(8, 120);
        Beat(project, 0, 0, 0, 4, 60);
        var session = new DocumentSession(new PlaybackEngine(new NullMidiOutput())) { Project = project };
        session.Playback.Engine.Start(project, new PlaybackOptions(),
            session.Playback.ReportPosition, session.Playback.MarkFinished);
        session.Playback.IsPlayingVisual = true;
        session.Playback.PlayheadMs = 765;
        return session;
    }

    private static void TestDocumentPlaybackSwitchPolicy()
    {
        foreach (var (preference, action) in new[]
                 {
                     (TabPlaybackActions.ContinuePlayingPrevious, TabPlaybackAction.Continue),
                     (TabPlaybackActions.PausePrevious, TabPlaybackAction.Pause),
                     (TabPlaybackActions.StopPrevious, TabPlaybackAction.Stop)
                 })
        {
            var previous = StartTabPlaybackSession();
            var selected = new DocumentSession(new PlaybackEngine(new NullMidiOutput()));
            try
            {
                TabPlaybackSwitchPolicy.Apply(new[] { previous, selected }, selected, preference);
                var expectedPlaying = action != TabPlaybackAction.Stop;
                var expectedPaused = action == TabPlaybackAction.Pause;
                var expectedPosition = action == TabPlaybackAction.Stop ? 0 : 765;
                Check($"tab switch policy applies {preference.ToLowerInvariant()}",
                    previous.Playback.Engine.IsPlaying == expectedPlaying &&
                    previous.Playback.Engine.IsPaused == expectedPaused &&
                    previous.Playback.IsPlayingVisual == expectedPlaying &&
                    previous.Playback.PlayheadMs == expectedPosition);
            }
            finally
            {
                previous.DisposePlayback();
                selected.DisposePlayback();
            }
        }
    }

    /// <summary>The playhead overlay geometry must be inside the playing measure and never null while playing.</summary>
    private static void TestPlayheadGeometry()
    {
        var editor = new TabEditorControl();
        var project = new SongProject { Tempo = 120 };
        project.Tracks.Add(new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(16) });
        editor.Project = project;
        editor.SelectedTrackIndex = 0;
        editor.SetPosition(0, 0, 0);

        Check("playhead: no overlay geometry while stopped", editor.PlayheadGeometry() is null);

        editor.SetPlayhead(1, 0);
        editor.PlaybackFraction = 0.5;
        editor.PlaybackActive = true;
        var geometry = editor.PlayheadGeometry();
        Check("playhead: overlay geometry exists while playing", geometry is not null);
        if (geometry is { } g)
        {
            Check("playhead: caret has a positive vertical span", g.Bottom > g.Top + 10, $"{g.Top}..{g.Bottom}");
            Check("playhead: caret x is on the page", g.X > 0 && g.X < 2000, $"x={g.X}");
        }
        editor.PlaybackActive = false;
        Check("playhead: stored position is hidden again when stopped", editor.PlayheadGeometry() is null);
        editor.ClearPlayhead();
        Check("playhead: explicit clear removes the score highlight position",
            editor.PlaybackMeasure == -1 && editor.PlaybackCell == -1 && editor.PlayheadGeometry() is null);

        var s0 = editor.SystemTopForMeasure(0);
        var s8 = editor.SystemTopForMeasure(8);
        Check("scroll: later wrapped system is below the first", s0 > 0 && s8 - s0 > 100, $"{s0} -> {s8}");
    }

    /// <summary>Techniques that used to be display-only must actually reach MIDI.</summary>
    private static void TestTechniqueTranslation()
    {
        // --- swing (triplet feel) ---
        var swing = SingleTrack();
        swing.Tracks[0].Measures[0].TripletFeel = true;
        for (var i = 0; i < 16; i += 2) Beat(swing, 0, 0, i, 8, 60 + i % 5);
        var swingTl = MidiTimelineBuilder.Build(swing, new PlaybackOptions());
        var swingNotes = swingTl.Notes.OrderBy(n => n.OnsetMs).ToList();
        Near("swing: first eighth stays on the beat", 0, swingNotes[0].OnsetMs);
        Near("swing: off-beat eighth is pushed late", 333.33, swingNotes[1].OnsetMs, 1.0);
        Near("swing: first eighth is lengthened", 333.33, swingNotes[0].DurationMs, 1.0);
        Near("swing: the pair still spans one beat", 500.0, swingNotes[0].DurationMs + swingNotes[1].DurationMs, 1.0);

        // --- legato slide: a pitch-bend ramp to the next note on the same string ---
        var slide = SingleTrack();
        Beat(slide, 0, 0, 0, 4, 69).Notes[0].Techniques.Add("Slide");
        Beat(slide, 0, 0, 4, 4, 73);
        var slideTl = MidiTimelineBuilder.Build(slide, new PlaybackOptions());
        var slideBends = slideTl.Events.Where(e => (e.Status & 0xF0) == 0xE0).ToList();
        Check("slide: emits a pitch-bend ramp", slideBends.Count >= 4, $"{slideBends.Count} bend events");
        Check("slide: the ramp reaches above centre", slideBends.Any(e => e.Data2 > 0x40), "no upward bend");

        // --- bend curve ---
        var bend = SingleTrack();
        var bendNote = Beat(bend, 0, 0, 0, 4, 69).Notes[0];
        bendNote.Techniques.Add("Bend");
        bendNote.BendPoints.Add(new BendPointModel { Offset = 0, Value = 0 });
        bendNote.BendPoints.Add(new BendPointModel { Offset = 0.5, Value = 4 });
        bendNote.BendPoints.Add(new BendPointModel { Offset = 1, Value = 4 });
        var bendTl = MidiTimelineBuilder.Build(bend, new PlaybackOptions());
        var bends = bendTl.Events.Where(e => (e.Status & 0xF0) == 0xE0).ToList();
        Check("bend: follows the imported curve", bends.Count >= 4 && bends.Any(e => e.Data2 > 0x40), $"{bends.Count} bend events");
        var bendEnd = bends.Where(e => Math.Abs(e.TimeMs - bendTl.Notes[0].EndMs) < 0.001).ToList();
        Check("bend: pitch wheel returns exactly to centre at the curve endpoint",
            bendEnd.Count > 0 && bendEnd[^1].Data1 == 0 && bendEnd[^1].Data2 == 64,
            string.Join(" -> ", bendEnd.Select(e => $"{e.Data1}:{e.Data2}")));

        // --- harmonic pitch: alphaTab's RealValue already contains the harmonic, so playback must not
        //     shift it again (it used to add 12, which doubled the octave on real harmonics). ---
        var harm = SingleTrack();
        Beat(harm, 0, 0, 0, 4, 74).Notes[0].Techniques.Add("ArtificialHarmonic");   // RealValue 74, fretted 62
        var harmTl = MidiTimelineBuilder.Build(harm, new PlaybackOptions());
        Check("harmonic plays the imported sounding pitch unchanged",
            harmTl.Events.Any(e => e.IsNoteOn && e.Data1 == 74), $"pitches: {string.Join(",", harmTl.Events.Where(e => e.IsNoteOn).Select(e => e.Data1))}");
        Check("harmonic does not double the octave",
            !harmTl.Events.Any(e => e.IsNoteOn && e.Data1 == 86));

        var trill = SingleTrack();
        var trillCell = Beat(trill, 0, 0, 0, 4, 60);
        trillCell.Notes[0].Techniques.UnionWith(new[] { "Trill", "Vibrato" });
        trillCell.Notes[0].TrillTargetMidi = 67;
        trillCell.Notes[0].TrillDurationDenominator = 16;
        var trillTimeline = MidiTimelineBuilder.Build(trill, new PlaybackOptions());
        Check("trill: uses the imported target pitch and subdivision speed",
            trillTimeline.Events.Where(e => e.IsNoteOn).Select(e => e.Data1).SequenceEqual(new[] { 60, 67, 60, 67 }),
            string.Join(",", trillTimeline.Events.Where(e => e.IsNoteOn).Select(e => e.Data1)));
        Check("trill and vibrato coexist on repeated attacks",
            trillTimeline.Events.Count(e => !e.IsSetup && (e.Status & 0xF0) == 0xE0) > 8);

        var tremolo = SingleTrack();
        var tremoloCell = Beat(tremolo, 0, 0, 0, 4, 60);
        tremoloCell.Notes[0].Techniques.Add("TremoloPick");
        tremoloCell.TremoloPickDenominator = 32;
        var tremoloTimeline = MidiTimelineBuilder.Build(tremolo, new PlaybackOptions());
        Check("tremolo picking: attack count follows the imported picking speed",
            tremoloTimeline.Events.Count(e => e.IsNoteOn) == 8 &&
            tremoloTimeline.Events.Where(e => e.IsNoteOn).All(e => e.Data1 == 60),
            $"attacks={tremoloTimeline.Events.Count(e => e.IsNoteOn)}");

        var combined = SingleTrack();
        var combinedCell = Beat(combined, 0, 0, 0, 4, 64);
        combinedCell.Notes[0].Techniques.UnionWith(new[] { "Bend", "Vibrato", "Slide" });
        combinedCell.Notes[0].SlideTargetMidi = 67;
        combinedCell.Notes[0].BendPoints.AddRange(new[]
        {
            new BendPointModel { Offset = 0, Value = 0 },
            new BendPointModel { Offset = 60, Value = 4 }
        });
        combinedCell.WhammyPoints.AddRange(new[]
        {
            new BendPointModel { Offset = 0, Value = 0 },
            new BendPointModel { Offset = 30, Value = -2 },
            new BendPointModel { Offset = 60, Value = 0 }
        });
        var combinedTimeline = MidiTimelineBuilder.Build(combined, new PlaybackOptions());
        var combinedNoteEvent = combinedTimeline.Notes.Single();
        var combinedWheels = combinedTimeline.Events.Where(e => !e.IsSetup && e.TrackIndex == 0 &&
            (e.Status & 0xF0) == 0xE0 && e.TimeMs > combinedNoteEvent.OnsetMs &&
            e.TimeMs < combinedNoteEvent.EndMs).ToList();
        Check("bend, slide, whammy and vibrato are emitted as one combined pitch stream",
            combinedWheels.Count > 8 && combinedWheels.Select(e => e.TimeMs).Distinct().Count() == combinedWheels.Count &&
            combinedTimeline.Events.Where(e => !e.IsSetup && e.TrackIndex == 0 && (e.Status & 0xF0) == 0xB0)
                .All(e => e.Data1 != 1));

        TestVibratoPitchWheel();
    }

    private static void TestVibratoPitchWheel()
    {
        static (ScoreTimeline Timeline, NoteEvent Note, List<ScoreEvent> Wheel) VibratoSong(string technique)
        {
            var project = SingleTrack();
            var note = Beat(project, 0, 0, 0, 4, 64).Notes[0];
            note.Techniques.Add(technique);
            var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
            var sounding = timeline.Notes.Single();
            var wheel = timeline.Events.Where(e => !e.IsSetup && e.TrackIndex == 0 &&
                (e.Status & 0xF0) == 0xE0 && e.TimeMs >= sounding.OnsetMs && e.TimeMs <= sounding.EndMs).ToList();
            return (timeline, sounding, wheel);
        }

        var normal = VibratoSong("Vibrato");
        var wide = VibratoSong("WideVibrato");
        static double PeakSemitone(List<ScoreEvent> events) => events
            .Select(e => Math.Abs((((e.Data2 << 7) | e.Data1) - 8192) / (8192.0 / 12)))
            .DefaultIfEmpty(0).Max();

        Check("vibrato: pitch wheel oscillates within a shallow normal range",
            normal.Wheel.Count > 4 && PeakSemitone(normal.Wheel) is > 0.05 and <= 0.4,
            $"{normal.Wheel.Count} points, peak {PeakSemitone(normal.Wheel):0.###} semitones");
        Check("wide vibrato: wider than normal, still shallow",
            PeakSemitone(wide.Wheel) > PeakSemitone(normal.Wheel) && PeakSemitone(wide.Wheel) <= 1.0,
            $"normal {PeakSemitone(normal.Wheel):0.###}, wide {PeakSemitone(wide.Wheel):0.###}");

        foreach (var (name, result) in new[] { ("normal", normal), ("wide", wide) })
        {
            var end = result.Wheel.Where(e => Math.Abs(e.TimeMs - result.Note.EndMs) < 0.001).ToList();
            Check($"{name} vibrato: final pitch wheel is exactly centred at note end",
                end.Count > 0 && end[^1].Data1 == 0 && end[^1].Data2 == 64,
                string.Join(" -> ", end.Select(e => $"{e.Data1}:{e.Data2}")));
            Check($"{name} vibrato: does not drive CC1",
                !result.Timeline.Events.Any(e => !e.IsSetup && e.TrackIndex == 0 &&
                    (e.Status & 0xF0) == 0xB0 && e.Data1 == 1));
        }
    }

    private static void TestReferenceABar56PlaybackState()
    {
        TestVibratoBarStartSynthetic();

        var path = LocalReferenceSongs.Resolve("reference-a");
        if (path is null)
        {
            Skip("bar 56 regression against the supplied reference song A GP5 (optional local extra)",
                "local reference song A not present");
            return;
        }

        SongProject project;
        try { project = GuitarProImporter.Import(path); }
        catch (Exception ex)
        {
            Check("bar 56 regression: supplied GP5 imports", false, ex.Message);
            return;
        }

        static (int Cell, int Fret, int GpString, int Midi)[] ImportedAtBar(TrackModel track)
            => track.Measures[55].Cells.SelectMany((cell, index) => cell.Notes.Select(note =>
                    (Cell: index, Note: note)))
                .OrderBy(item => item.Cell)
                .Select(item => (item.Cell, item.Note.Fret, track.StringTunings.Count - item.Note.StringIndex, item.Note.MidiValue))
                .ToArray();

        var leadOne = ImportedAtBar(project.Tracks[2]);
        var leadTwo = ImportedAtBar(project.Tracks[3]);
        var expectedLeadOne = new[] { (0, 14, 3, 62), (8, 12, 2, 55), (12, 10, 2, 53) };
        var expectedLeadTwo = new[] { (0, 10, 3, 58), (8, 8, 2, 51), (12, 7, 2, 50) };
        Check("bar 56 import: Lead Guitar 1 preserves GP fret/string/base pitches",
            project.Tracks[2].Name == "Lead Guitar 1" && leadOne.SequenceEqual(expectedLeadOne),
            string.Join("; ", leadOne.Select(n => $"cell{n.Cell}: fret{n.Fret}/string{n.GpString}/MIDI{n.Midi}")));
        Check("bar 56 import: Lead Guitar 2 preserves GP fret/string/base pitches",
            project.Tracks[3].Name == "Lead Guitar 2" && leadTwo.SequenceEqual(expectedLeadTwo),
            string.Join("; ", leadTwo.Select(n => $"cell{n.Cell}: fret{n.Fret}/string{n.GpString}/MIDI{n.Midi}")));

        var simultaneous = new[] { 2, 3 }.SelectMany(trackIndex =>
                project.Tracks[trackIndex].Measures[54].Cells.SelectMany((cell, cellIndex) =>
                    cell.Notes.Select(note => (TrackIndex: trackIndex, CellIndex: cellIndex, Note: note))))
            .Where(item => item.Note.Techniques.Contains("ArtificialHarmonic") &&
                           item.Note.Techniques.Contains("Vibrato") && item.Note.BendPoints.Count > 0)
            .ToList();
        Check("bar 55 import: artificial harmonic, vibrato and bend curve coexist on the same notes",
            simultaneous.Count == 2 && simultaneous.All(item => item.Note.Techniques.Contains("Bend")),
            $"combined notes={simultaneous.Count}");
        var combinedLabels = simultaneous.Count == 0 ? "" : TabEditorControl.TechniqueLabel(simultaneous.Select(item => item.Note));
        Check("bar 55 TAB label retains the simultaneous harmonic, vibrato and bend marks",
            combinedLabels.Contains("A.H.", StringComparison.Ordinal) &&
            !combinedLabels.Contains("~", StringComparison.Ordinal) /* vibrato is a wavy line now */ &&
            combinedLabels.Contains("b", StringComparison.Ordinal), combinedLabels);

        var typedBends = new[] { 2, 3 }.SelectMany(trackIndex =>
                project.Tracks[trackIndex].Measures[54].Cells.SelectMany(cell => cell.Notes))
            .Where(note => note.Techniques.Contains("ArtificialHarmonic") && note.BendPoints.Count > 0)
            .ToList();
        Check("bar 55 import: GP5 bend versus prebend kinds are preserved with their curves",
            typedBends.Count == 4 && typedBends.Count(note => note.BendTypeName == "Bend") == 2 &&
            typedBends.Count(note => note.BendTypeName == "Prebend") == 2 &&
            typedBends.All(note => note.BendStyleName == "Default"));

        var laterHarmonicBends = new[] { 2, 3 }.SelectMany(trackIndex =>
                project.Tracks[trackIndex].Measures[107].Cells.SelectMany((cell, cellIndex) =>
                    cell.Notes.Select(note => (TrackIndex: trackIndex, CellIndex: cellIndex, Note: note))))
            .Where(item => item.Note.Techniques.Contains("ArtificialHarmonic") && item.Note.BendPoints.Count > 0)
            .ToList();
        Check("bar 108 import: later artificial harmonics retain their independent bend curves",
            laterHarmonicBends.Count == 2 && laterHarmonicBends.All(item => item.Note.Techniques.Contains("Bend")),
            $"combined notes={laterHarmonicBends.Count}");

        var importedSlideIns = project.Tracks.SelectMany(track => track.Measures)
            .SelectMany(measure => measure.Cells).SelectMany(cell => cell.Notes)
            .Count(note => note.Techniques.Contains("SlideInBelow") || note.Techniques.Contains("SlideInAbove"));
        Check("Reference song A GP5 import retains slide-in direction", importedSlideIns > 0,
            $"slide-in notes={importedSlideIns}");

        var leadOneNotes = project.Tracks[2].Measures[55].Cells.SelectMany(cell => cell.Notes).ToList();
        Check("bar 56 import: Lead Guitar 1 vibrato is not misidentified as bend/harmonic",
            leadOneNotes[0].Techniques.Contains("Vibrato") && leadOneNotes.All(note =>
                !note.Techniques.Contains("WideVibrato") && !note.Techniques.Contains("Harmonic") &&
                !note.Techniques.Contains("ArtificialHarmonic") && note.BendPoints.Count == 0));

        var leadOneBar67 = project.Tracks[2].Measures[66].Cells.SelectMany(cell => cell.Notes).ToList();
        var expectedBar67Pitches = Enumerable.Range(0, 4).SelectMany(_ => new[] { 67, 48, 60, 63 }).ToArray();
        Check("bar 67 investigation: Lead Guitar 1 imports the expected plain MIDI pitches",
            leadOneBar67.Select(note => note.MidiValue).SequenceEqual(expectedBar67Pitches),
            string.Join(",", leadOneBar67.Select(note => note.MidiValue)));
        Check("bar 67 investigation: no local bend, vibrato or harmonic explains the pitch issue",
            leadOneBar67.All(note => note.BendPoints.Count == 0 &&
                !note.Techniques.Contains("Vibrato") && !note.Techniques.Contains("WideVibrato") &&
                !note.Techniques.Contains("Harmonic") && !note.Techniques.Contains("ArtificialHarmonic")));

        const int fadeOriginBar = 155;
        const int fadeTieBar = 156;
        var fadeOriginCell = project.Tracks[0].Measures[fadeOriginBar].Cells.FindIndex(cell =>
            cell.Notes.Any(note => note.Techniques.Contains("FadeIn")));
        var fadeTieCell = project.Tracks[0].Measures[fadeTieBar].Cells.FindIndex(cell =>
            cell.Notes.Any(note => note.Tied));
        var fadeOrigin = fadeOriginCell >= 0 ? project.Tracks[0].Measures[fadeOriginBar].Cells[fadeOriginCell] : null;
        var fadeTie = fadeTieCell >= 0 ? project.Tracks[0].Measures[fadeTieBar].Cells[fadeTieCell] : null;
        Check("bars 156-157 import: GP5 fade-in flag and tied chord destinations survive in the model",
            fadeOrigin is not null && fadeOrigin.Notes.Count == 2 &&
            fadeOrigin.Notes.All(note => note.Techniques.Contains("FadeIn")) &&
            fadeTie is not null && fadeTie.Notes.Count == 2 && fadeTie.Notes.All(note => note.Tied),
            $"fade notes={fadeOrigin?.Notes.Count ?? 0}, tie destinations={fadeTie?.Notes.Count ?? 0}");

        var graceCellIndex = project.Tracks[2].Measures[115].Cells.FindIndex(cell =>
            cell.Notes.Any(note => note.IsGraceNote));
        if (graceCellIndex >= 0)
        {
            var graceCell = project.Tracks[2].Measures[115].Cells[graceCellIndex];
            var graceNote = graceCell.Notes.Single(note => note.IsGraceNote);
            var principalNote = graceCell.Notes.FirstOrDefault(note => !note.IsGraceNote);
            Check("bar 116 import: grace note stays distinct from its principal note and duration",
                principalNote is not null && graceCell.DurationDenominator == 1 &&
                graceNote.MidiValue == 58 && principalNote.MidiValue == 60 &&
                Math.Abs(graceNote.GraceOnsetOffsetSlots + 0.5) < 0.001,
                $"duration=1/{graceCell.DurationDenominator}, grace MIDI={graceNote.MidiValue}, principal MIDI={principalNote?.MidiValue}, offset={graceNote.GraceOnsetOffsetSlots:0.###}; " +
                string.Join(" | ", project.Tracks[2].Measures[115].Cells.Select((cell, index) =>
                    cell.Notes.Count == 0 ? "" : $"c{index}@{cell.RhythmicPosition}:" +
                        string.Join(",", cell.Notes.Select(note => $"{note.MidiValue}{(note.IsGraceNote ? "g" : "")}")))
                    .Where(value => value.Length > 0)));
        }
        else Check("bar 116 import: grace note remains a separate model note", false);

        var continuous = MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = false });
        var direct = MidiTimelineBuilder.Build(project, new PlaybackOptions { StartBar = 55, RepeatExpansion = false });
        if (fadeOrigin is not null && fadeTie is not null && fadeOriginCell >= 0 && fadeTieCell >= 0)
        {
            var fadeTimeline = MidiTimelineBuilder.Build(project,
                new PlaybackOptions { StartBar = fadeOriginBar, RepeatExpansion = false });
            var sustainedNotes = fadeTimeline.Notes.Where(note => note.TrackIndex == 0 &&
                note.Bar == fadeOriginBar && note.Cell == fadeOriginCell).ToList();
            var destinationBar = fadeTimeline.Bars.FirstOrDefault(bar => bar.Bar == fadeTieBar);
            var destinationCellEnd = MusicTime.CellSlots(fadeTie) *
                ((destinationBar.EndMs - destinationBar.StartMs) / MusicTime.BarSlots(project, fadeTieBar));
            var expectedEnd = destinationBar.StartMs + destinationCellEnd;
            var fadeNote = sustainedNotes.FirstOrDefault();
            var sustainedPitches = sustainedNotes.Select(note => note.Midi).ToHashSet();
            var sustainedAttackCount = fadeNote is null ? 0 : fadeTimeline.Events.Count(item =>
                !item.IsSetup && item.TrackIndex == 0 && item.IsNoteOn &&
                sustainedPitches.Contains(item.Data1) && item.TimeMs >= fadeNote.OnsetMs - 0.001 &&
                item.TimeMs < expectedEnd - 0.001);
            var envelope = fadeNote is null ? new List<ScoreEvent>() : fadeTimeline.Events
                .Where(item => !item.IsSetup && item.TrackIndex == 0 && (item.Status & 0xF0) == 0xB0 &&
                    item.Data1 == 11 && item.TimeMs >= fadeNote.OnsetMs - 0.001 &&
                    item.TimeMs <= expectedEnd + 0.001)
                .OrderBy(item => item.TimeMs).ToList();
            var firstBar = fadeTimeline.Bars.FirstOrDefault(bar => bar.Bar == fadeOriginBar);
            var atFirstBarEnd = envelope.LastOrDefault(item => item.TimeMs <= firstBar.EndMs + 0.001);
            Check("bars 156-157 playback: tied notes sustain without a fresh attack",
                sustainedNotes.Count == 2 && fadeTimeline.TieMerges >= 2 &&
                sustainedAttackCount == 2 &&
                Math.Abs(sustainedNotes[0].EndMs - expectedEnd) < 2 &&
                fadeTimeline.Notes.All(note => note.TrackIndex != 0 || note.Bar != fadeTieBar || note.Cell != fadeTieCell),
                $"merged={fadeTimeline.TieMerges}, sustained chord notes={sustainedNotes.Count}, attacks={sustainedAttackCount}, expected end={expectedEnd:0.##}");
            Check("bars 156-157 playback: fade-in ramps expression across the full tied duration",
                envelope.Count > 10 && envelope[0].Data2 == 0 && envelope[^1].Data2 == 127 &&
                atFirstBarEnd is not null && atFirstBarEnd.Data2 is > 0 and < 127 &&
                envelope.Zip(envelope.Skip(1), (before, after) => after.Data2 >= before.Data2).All(monotonic => monotonic),
                $"controller points={envelope.Count}, first-bar-end level={atFirstBarEnd?.Data2}, end={envelope.LastOrDefault()?.Data2}");
        }
        else
        {
            Check("bars 156-157 playback: fade/tie data is available for the regression", false);
        }
        if (graceCellIndex >= 0)
        {
            var graceEvents = continuous.Notes.Where(note => note.TrackIndex == 2 && note.Bar == 115 &&
                note.Cell == graceCellIndex).OrderBy(note => note.OnsetMs).ToList();
            var graceBar = continuous.Bars.FirstOrDefault(bar => bar.Bar == 115);
            Check("bar 116 playback: grace attack precedes its principal note without shortening the bar",
                graceEvents.Count == 2 && graceEvents[0].Midi == 58 && graceEvents[1].Midi == 60 &&
                graceEvents[0].OnsetMs < graceEvents[1].OnsetMs &&
                Math.Abs(graceEvents[1].EndMs - graceBar.EndMs) < 2,
                string.Join("; ", graceEvents.Select(note => $"MIDI{note.Midi}@{note.OnsetMs:0.##}+{note.DurationMs:0.##}")));
        }
        if (simultaneous.FirstOrDefault(item => item.TrackIndex == 2) is var combo && combo.Note is not null)
        {
            var compiled = continuous.Notes.FirstOrDefault(note => note.TrackIndex == combo.TrackIndex &&
                note.Bar == 54 && note.Cell == combo.CellIndex && note.StringIndex == combo.Note.StringIndex);
            if (compiled is not null)
            {
                var residuals = continuous.Events.Where(e => !e.IsSetup && e.TrackIndex == combo.TrackIndex &&
                        (e.Status & 0xF0) == 0xE0 && e.TimeMs >= compiled.OnsetMs + compiled.DurationMs * 0.6 &&
                        e.TimeMs <= compiled.OnsetMs + compiled.DurationMs * 0.9)
                    .Select(e =>
                    {
                        var offset = (e.TimeMs - compiled.OnsetMs) / compiled.DurationMs;
                        var semitones = (((e.Data2 << 7) | e.Data1) - 8192) / (8192.0 / 12);
                        return semitones - offset; // The reference's curve in this passage rises linearly by one semitone.
                    }).ToArray();
                Check("bar 55 playback: vibrato oscillates on top of the imported bend curve",
                    residuals.Length > 3 && residuals.Min() < -0.08 && residuals.Max() > 0.08,
                    residuals.Length == 0 ? "no combined pitch-wheel samples" :
                    $"residual range {residuals.Min():0.###}..{residuals.Max():0.###}");
            }
            else Check("bar 55 playback: combined harmonic note reaches MIDI timeline", false);
        }
        var continuousNote = continuous.Notes.Single(note => note.TrackIndex == 2 && note.Bar == 55 && note.Cell == 0);
        var directNote = direct.Notes.Single(note => note.TrackIndex == 2 && note.Bar == 55 && note.Cell == 0);
        Check("bar 56 playback: imported vibrato note keeps MIDI 62 in both start modes",
            continuousNote.Midi == 62 && directNote.Midi == 62,
            $"continuous={continuousNote.Midi}, direct={directNote.Midi}");

        static (int Wheel, int Modulation) StateAt(ScoreTimeline timeline, int track, double atMs)
        {
            var events = timeline.Events.Where(e => e.TrackIndex == track && e.TimeMs <= atMs + 0.001).ToList();
            var wheel = events.LastOrDefault(e => (e.Status & 0xF0) == 0xE0);
            var modulation = events.LastOrDefault(e => (e.Status & 0xF0) == 0xB0 && e.Data1 == 1);
            return (wheel is null ? -1 : (wheel.Data2 << 7) | wheel.Data1, modulation?.Data2 ?? -1);
        }

        static (double Offset, int Kind, int Data1, int Data2)[] ExpressionStream(ScoreTimeline timeline, NoteEvent note)
            => timeline.Events.Where(e => !e.IsSetup && e.TrackIndex == note.TrackIndex &&
                    e.TimeMs >= note.OnsetMs - 0.001 && e.TimeMs <= note.EndMs + 0.001 &&
                    ((e.Status & 0xF0) == 0xE0 || ((e.Status & 0xF0) == 0xB0 && e.Data1 == 1)))
                .OrderBy(e => Math.Round(e.TimeMs - note.OnsetMs, 3))
                .ThenBy(e => e.Status & 0xF0).ThenBy(e => e.Data1).ThenBy(e => e.Data2)
                .Select(e => (Math.Round(e.TimeMs - note.OnsetMs, 3), e.Status & 0xF0, e.Data1, e.Data2))
                .Distinct()
                .ToArray();

        Check("bar 56 start: direct seek and continuous playback have identical pitch/controller state",
            StateAt(continuous, 2, continuousNote.OnsetMs) == StateAt(direct, 2, directNote.OnsetMs),
            $"continuous={StateAt(continuous, 2, continuousNote.OnsetMs)}, direct={StateAt(direct, 2, directNote.OnsetMs)}");
        var continuousExpression = ExpressionStream(continuous, continuousNote);
        var directExpression = ExpressionStream(direct, directNote);
        Check("bar 56 start: direct seek and continuous vibrato expression streams match",
            continuousExpression.SequenceEqual(directExpression),
            $"continuous={continuousExpression.Length} points, direct={directExpression.Length} points; " +
            $"continuous-only={string.Join(",", continuousExpression.Except(directExpression).Select(point => point.Offset))}; " +
            $"direct-only={string.Join(",", directExpression.Except(continuousExpression).Select(point => point.Offset))}");
    }

    /// <summary>Synthetic stand-in for the reference song A bar 56 case: a vibrato note at the start of a later bar sounds the same pitch and expression whether playback starts at that bar or runs through from bar 1.</summary>
    private static void TestVibratoBarStartSynthetic()
    {
        var project = SingleTrack(bars: 3);
        var bar = 1;
        var note = Beat(project, 0, bar, 0, 4, 62, str: 2, fret: 14).Notes[0];
        note.Techniques.Add("Vibrato");
        Beat(project, 0, 0, 0, 4, 60);
        var continuous = MidiTimelineBuilder.Build(project, new PlaybackOptions { RepeatExpansion = false });
        var direct = MidiTimelineBuilder.Build(project, new PlaybackOptions { StartBar = bar, RepeatExpansion = false });
        var c = continuous.Notes.Single(n => n.Bar == bar && n.Cell == 0);
        var d = direct.Notes.Single(n => n.Bar == bar && n.Cell == 0);
        Check("synthetic bar-start vibrato: note keeps its MIDI pitch in both start modes", c.Midi == 62 && d.Midi == 62,
            $"continuous={c.Midi}, direct={d.Midi}");

        static (double Offset, int Kind, int Data1, int Data2)[] Stream(ScoreTimeline timeline, NoteEvent n)
            => timeline.Events.Where(e => !e.IsSetup && e.TrackIndex == n.TrackIndex &&
                    e.TimeMs >= n.OnsetMs - 0.001 && e.TimeMs <= n.EndMs + 0.001 &&
                    ((e.Status & 0xF0) == 0xE0 || ((e.Status & 0xF0) == 0xB0 && e.Data1 == 1)))
                .Select(e => (Math.Round(e.TimeMs - n.OnsetMs, 3), e.Status & 0xF0, e.Data1, e.Data2))
                .Distinct().OrderBy(x => x.Item1).ThenBy(x => x.Item2).ThenBy(x => x.Item3).ThenBy(x => x.Item4).ToArray();
        var cs = Stream(continuous, c);
        var ds = Stream(direct, d);
        Check("synthetic bar-start vibrato: direct seek and continuous expression streams match",
            cs.Length > 4 && cs.SequenceEqual(ds), $"continuous={cs.Length} points, direct={ds.Length} points");
    }

    /// <summary>Playback behaviours of the compiled sequence: palm-mute length, channel setup and neutral resets.</summary>
    private static void TestEngineParity()
    {
        // Palm mute: a palm-muted note is capped at a quarter note.
        var p = SingleTrack();
        var cell = Beat(p, 0, 0, 0, 1, 60);                       // whole note
        cell.Notes[0].Techniques.Add("PalmMute");
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        Near("palm mute: capped at a quarter note", 500, tl.Notes[0].DurationMs);

        // Channel setup resets the pitch wheel so a bend left behind cannot transpose the next passage.
        var p2 = SingleTrack();
        Beat(p2, 0, 0, 0, 4, 60);
        var tl2 = MidiTimelineBuilder.Build(p2, new PlaybackOptions());
        Check("setup: pitch wheel is centred for every track",
            tl2.ChannelSetup.Any(e => (e.Status & 0xF0) == 0xE0 && e.Data2 == 0x40),
            $"{tl2.ChannelSetup.Count} setup events");
        Check("setup: modulation wheel is neutral on every arm",
            tl2.ChannelSetup.Any(e => e.TrackIndex == 0 && (e.Status & 0xF0) == 0xB0 && e.Data1 == 1 && e.Data2 == 0));
        var bendRange = tl2.ChannelSetup.Where(e => e.TrackIndex == 0 && (e.Status & 0xF0) == 0xB0 &&
            e.Data1 is 101 or 100 or 6 or 38).Select(e => (e.Data1, e.Data2)).ToArray();
        Check("setup: pitch-bend range is explicitly configured to +/-12 semitones",
            bendRange.SequenceEqual(new[] { (101, 0), (100, 0), (6, 12), (38, 0), (101, 127), (100, 127) }),
            string.Join(",", bendRange.Select(value => $"CC{value.Data1}={value.Data2}")));
        var emittedBendRange = tl2.Events.Where(e => e.TrackIndex == 0 && (e.Status & 0xF0) == 0xB0 &&
            e.Data1 is 101 or 100 or 6 or 38).Select(e => (e.Data1, e.Data2)).ToArray();
        Check("setup: pitch-bend RPN order survives event sorting",
            emittedBendRange.SequenceEqual(new[] { (101, 0), (100, 0), (6, 12), (38, 0), (101, 127), (100, 127) }),
            string.Join(",", emittedBendRange.Select(value => $"CC{value.Data1}={value.Data2}")));
        Check("setup: program, mix, modulation, bend range and centre are sent",
            tl2.ChannelSetup.Count(e => e.TrackIndex == 0) == 14, $"{tl2.ChannelSetup.Count(e => e.TrackIndex == 0)}");

        TestStalePitchResetRecovery();
        TestStaleResetSchedulerDispatch();
    }

    private static void TestStalePitchResetRecovery()
    {
        var output = new RecordingMidiOutput();
        using var engine = new PlaybackEngine(output);
        var timeline = new ScoreTimeline();
        var setup = new ScoreEvent { TimeMs = 0, DeviceId = -1, Status = 0xE0, Data1 = 0, Data2 = 64, IsSetup = true };
        var modulationSetup = new ScoreEvent { TimeMs = 0, DeviceId = -1, Status = 0xB0, Data1 = 1, Data2 = 0, IsSetup = true };
        timeline.ChannelSetup.Add(setup);
        timeline.ChannelSetup.Add(modulationSetup);
        timeline.Events.Add(setup);
        timeline.Events.Add(modulationSetup);
        timeline.Events.Add(new ScoreEvent { TimeMs = 10, DeviceId = -1, Status = 0xE0, Data1 = 32, Data2 = 80 });
        timeline.Events.Add(new ScoreEvent { TimeMs = 30, DeviceId = -1, Status = 0xB0, Data1 = 1, Data2 = 127 });
        timeline.Events.Add(new ScoreEvent { TimeMs = 80, DeviceId = -1, Status = 0xB0, Data1 = 1, Data2 = 0 });
        timeline.Events.Add(new ScoreEvent { TimeMs = 100, DeviceId = -1, Status = 0xE0, Data1 = 0, Data2 = 64 });
        var followingNote = new ScoreEvent { TimeMs = 310, DeviceId = -1, Status = 0x90, Data1 = 60, Data2 = 100 };
        timeline.Events.Add(followingNote);

        Check("stale neutral pitch reset is treated as essential",
            PlaybackEngine.IsEssentialReleaseOrReset(new ScoreEvent { Status = 0xE0, Data1 = 0, Data2 = 64 }));
        Check("stale modulation-off reset is treated as essential",
            PlaybackEngine.IsEssentialReleaseOrReset(new ScoreEvent { Status = 0xB0, Data1 = 1, Data2 = 0 }));

        // Both expressions were already active; simulate a stall which makes their neutral events stale.
        output.Send(-1, 0xE0, 32, 80);
        output.Send(-1, 0xB0, 1, 127);
        engine.RestoreChannelStateAt(timeline, 0, 300);
        output.Send(followingNote.DeviceId, followingNote.Status, followingNote.Data1, followingNote.Data2);
        var restoredBend = output.Messages.LastOrDefault(message => (message.Status & 0xF0) == 0xE0);
        Check("stale bend reset: missed centre is restored before following notes",
            restoredBend is { Data1: 0, Data2: 64 },
            restoredBend is { } bend ? $"restored {bend.Data1}:{bend.Data2}" : "no pitch-wheel state restored");
        var modulationValues = output.Messages.Where(message => message.Status == 0xB0 && message.Data1 == 1)
            .Select(message => message.Data2).ToArray();
        Check("stale controller reset: modulation is explicitly restored to off",
            modulationValues.Length == 2 && modulationValues[^1] == 0,
            $"sent {string.Join(",", output.Messages.Where(message => message.Status == 0xB0 && message.Data1 == 1).Select(message => message.Data2))}");
        var resetIndex = output.Messages.FindLastIndex(message => (message.Status & 0xF0) == 0xE0 && message.Data1 == 0 && message.Data2 == 64);
        var noteIndex = output.Messages.FindLastIndex(message => message.Status == followingNote.Status && message.Data1 == followingNote.Data1);
        Check("stale bend reset: following note is sent after pitch is centred", resetIndex >= 0 && noteIndex > resetIndex);
        Check("stale state recovery leaves both expressions neutral",
            modulationValues.LastOrDefault() == 0 && restoredBend is { Data1: 0, Data2: 64 });
    }

    private static void TestStaleResetSchedulerDispatch()
    {
        var project = SingleTrack(bpm: 400);
        var first = Beat(project, 0, 0, 0, 16, 60).Notes[0];
        first.Techniques.Add("Vibrato");
        Beat(project, 0, 0, 8, 16, 62);

        var output = new StallingMidiOutput(firstPitch: 60, followingPitch: 62);
        using var engine = new PlaybackEngine(output);
        engine.Start(project, new PlaybackOptions(), _ => { }, () => { });
        Check("stale scheduler reset: first note reaches the controllable output",
            output.FirstAttack.Wait(TimeSpan.FromSeconds(2)));
        if (!output.FirstAttack.IsSet) return;

        // Hold the scheduler on the first attack long enough for the short note's centre reset to
        // become stale, but not long enough to trigger the scheduler's full timeline resync path.
        Thread.Sleep(190);
        output.ReleaseFirstAttack.Set();
        Check("stale scheduler reset: following note is eventually dispatched",
            output.FollowingAttack.Wait(TimeSpan.FromSeconds(2)));
        if (!output.FollowingAttack.IsSet) return;

        var messages = output.Snapshot();
        var firstAttack = Array.FindIndex(messages, message => (message.Status & 0xF0) == 0x90 && message.Data1 == 60 && message.Data2 > 0);
        var followingAttack = Array.FindIndex(messages, message => (message.Status & 0xF0) == 0x90 && message.Data1 == 62 && message.Data2 > 0);
        var staleCentre = Array.FindIndex(messages, Math.Max(0, firstAttack + 1), message =>
            (message.Status & 0xF0) == 0xE0 && message.Data1 == 0 && message.Data2 == 64);
        Check("stale scheduler reset: stale pitch centre is dispatched before the following attack",
            firstAttack >= 0 && staleCentre > firstAttack && followingAttack > staleCentre,
            $"first={firstAttack}, centre={staleCentre}, following={followingAttack}");
    }

    /// <summary>
    /// Jumping to bar 1 while playing must sound the bar's first note even when the device resets
    /// (queued by the restart) are slow: the clock must not run during the reset wait.
    /// </summary>
    private static void TestSeekWhilePlayingSoundsFirstNote()
    {
        var project = SingleTrack(bars: 3, bpm: 120);
        Beat(project, 0, 0, 0, 4, 60);
        Beat(project, 0, 1, 0, 4, 62);
        Beat(project, 0, 2, 0, 4, 64);

        var output = new SlowResetMidiOutput();
        using var engine = new PlaybackEngine(output);
        engine.Start(project, new PlaybackOptions { StartBar = 2 }, _ => { }, () => { });
        Check("seek while playing: bar 3 first note is heard", output.WaitForAttack(64, 3000));

        output.ResetDelayMs = 400; // a driver flushing sounding notes
        output.ClearAttacks();
        engine.Seek(project, 0, 0);
        Check("seek while playing: first note of the target bar is dispatched despite slow resets",
            output.WaitForAttack(60, 5000));
    }

    private sealed class SlowResetMidiOutput : IMidiOutput
    {
        private readonly object _gate = new();
        private readonly List<int> _attacks = new();
        public volatile int ResetDelayMs;
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2)
        {
            if ((status & 0xF0) != 0x90 || data2 == 0) return;
            lock (_gate) _attacks.Add(data1);
        }
        public void ClearAttacks() { lock (_gate) _attacks.Clear(); }
        public bool WaitForAttack(int pitch, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                lock (_gate) if (_attacks.Contains(pitch)) return true;
                Thread.Sleep(5);
            }
            return false;
        }
        public void ResetAll() { var d = ResetDelayMs; if (d > 0) Thread.Sleep(d); }
        public void Close() { }
        public void Dispose() { }
    }

    private sealed class StallingMidiOutput(int firstPitch, int followingPitch) : IMidiOutput
    {
        private readonly object _gate = new();
        private readonly List<(int DeviceId, int Status, int Data1, int Data2)> _messages = new();
        private int _blocked;

        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public ManualResetEventSlim FirstAttack { get; } = new();
        public ManualResetEventSlim ReleaseFirstAttack { get; } = new();
        public ManualResetEventSlim FollowingAttack { get; } = new();

        public void Send(int deviceId, int status, int data1, int data2)
        {
            lock (_gate) _messages.Add((deviceId, status, data1, data2));
            if ((status & 0xF0) != 0x90 || data2 == 0) return;
            if (data1 == firstPitch && Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                FirstAttack.Set();
                ReleaseFirstAttack.Wait();
            }
            if (data1 == followingPitch) FollowingAttack.Set();
        }

        public (int DeviceId, int Status, int Data1, int Data2)[] Snapshot()
        {
            lock (_gate) return _messages.ToArray();
        }

        public void ResetAll() { }
        public void Close() { }
        public void Dispose()
        {
            ReleaseFirstAttack.Set();
        }
    }

    private sealed class RecordingMidiOutput : IMidiOutput
    {
        private readonly object _gate = new();
        private readonly List<(int DeviceId, int Status, int Data1, int Data2)> _messages = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public List<(int DeviceId, int Status, int Data1, int Data2)> Messages { get { lock (_gate) return _messages.ToList(); } }
        public void Send(int deviceId, int status, int data1, int data2)
        {
            lock (_gate) _messages.Add((deviceId, status, data1, data2));
        }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private sealed class TimedMidiOutput : IMidiOutput
    {
        private readonly object _gate = new();
        private readonly List<(long Timestamp, int Status, int Data1, int Data2)> _messages = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public (long Timestamp, int Status, int Data1, int Data2)[] Messages
        {
            get { lock (_gate) return _messages.ToArray(); }
        }
        public void Send(int deviceId, int status, int data1, int data2)
        {
            lock (_gate) _messages.Add((Stopwatch.GetTimestamp(), status, data1, data2));
        }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    /// <summary>
    /// Guards the key map after the hotkey audit: shifted shortcuts that used to be shadowed by the
    /// plain handlers (Shift+1 staccato, Shift+- tenuto, Shift+. fade, main '/' triplet).
    /// </summary>
    private static void TestHotkeys()
    {
        static TabEditorControl Editor(out SongProject project, out TrackModel track)
        {
            var editor = new TabEditorControl();
            project = new SongProject { Tempo = 120 };
            track = new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(4) };
            project.Tracks.Add(track);
            editor.Project = project;
            editor.SelectedTrackIndex = 0;
            editor.SetPosition(0, 0, 0);
            return editor;
        }

        // The same chain the window runs on a key press: the editor gets first refusal, then the gesture
        // is looked up in the default key map and the command runs. Nothing here calls a feature directly.
        var defaultKeys = HotkeyCatalog.BuildMap(new HotkeySettings());
        bool PressKey(TabEditorControl editor, Key key, ModifierKeys mods) =>
            editor.TryHandleKey(key, mods) ||
            (defaultKeys.TryGetValue(WpfHotkeyGestureAdapter.Format(key, mods), out var id) && editor.TryRunNoteCommand(id));

        // + / - change the note value; the plain '-' must not swallow the shifted variants.
        var e1 = Editor(out _, out _);
        e1.SetDuration(8);
        e1.TryHandleKey(Key.OemPlus, ModifierKeys.None);
        Eq("hotkey: + shortens the note", 16, e1.CurrentDurationDenominator);
        e1.TryHandleKey(Key.OemMinus, ModifierKeys.None);
        Eq("hotkey: - lengthens the note", 8, e1.CurrentDurationDenominator);
        e1.ReversePlusMinusDuration = true;
        e1.TryHandleKey(Key.OemPlus, ModifierKeys.None);
        Eq("hotkey: reversed + lengthens", 4, e1.CurrentDurationDenominator);
        e1.TryHandleKey(Key.OemMinus, ModifierKeys.None);
        e1.ReversePlusMinusDuration = false;
        Check("hotkey: Shift+- toggles tenuto (not duration)", PressKey(e1, Key.OemMinus, ModifierKeys.Shift) && e1.CurrentCell()!.Tenuto);
        Eq("hotkey: Shift+- left the duration unchanged", 8, e1.CurrentDurationDenominator);

        // Shift+1 toggles staccato and does not type fret 1.
        var e2 = Editor(out _, out _);
        Check("hotkey: Shift+1 toggles staccato", PressKey(e2, Key.D1, ModifierKeys.Shift) && e2.CurrentCell()!.Staccato);
        Eq("hotkey: Shift+1 types no fret", 0, e2.CurrentCell()!.Notes.Count);

        // Plain digits still write frets (EnterFret auto-advances, so read the cell it wrote to).
        var e3 = Editor(out _, out var t3);
        e3.SetDuration(4);
        e3.TryHandleKey(Key.D5, ModifierKeys.None);
        var written = t3.Measures[0].Cells[0].Notes;
        Check("hotkey: digit writes a fret", written.Count == 1 && written[0].Fret == 5);

        // Shift+. / Shift+, toggle the fades (previously shadowed by the plain dot handler).
        var e4 = Editor(out _, out _);
        e4.SetDuration(4);
        e4.EnterFret(3, autoAdvance: false);
        var note4 = e4.CurrentCell()!.Notes.FirstOrDefault();
        Check("hotkey: Shift+. toggles fade out",
            PressKey(e4, Key.OemPeriod, ModifierKeys.Shift) && note4 is not null && note4.Techniques.Contains(TechniqueNames.FadeOut));
        Check("hotkey: Shift+, toggles fade in",
            PressKey(e4, Key.OemComma, ModifierKeys.Shift) && note4 is not null && note4.Techniques.Contains(TechniqueNames.FadeIn));

        // The main '/' key toggles triplets (only the shifted/numpad variants used to work).
        var e5 = Editor(out _, out _);
        Check("hotkey: / toggles triplet", PressKey(e5, Key.OemQuestion, ModifierKeys.None) && e5.CurrentCell()!.IsTriplet);
        Check("hotkey presets: TuxGuitar rebinds and the classic preset matches the TabForge defaults",
            HotkeyPresets.Override(HotkeyPresets.TuxGuitar, "Note.Dot") == "Multiply" &&
            HotkeyPresets.Override(HotkeyPresets.GuitarPro5, "Note.Dot") is null);

        // Shift+Up/Down shift the pitch by a semitone.
        var e6 = Editor(out _, out _);
        e6.SetDuration(4);
        e6.EnterFret(5, autoAdvance: false);
        var note6 = e6.CurrentCell()!.Notes.FirstOrDefault();
        e6.TryHandleKey(Key.Up, ModifierKeys.Shift);
        Check("hotkey: Shift+Up raises the pitch a semitone", note6 is not null && note6.Fret == 6);
        e6.TryHandleKey(Key.Down, ModifierKeys.Shift);
        Check("hotkey: Shift+Down lowers it back", note6 is not null && note6.Fret == 5);

        // Ctrl + / - insert and delete beats.
        var e7 = Editor(out _, out var t7);
        e7.SetDuration(4);
        e7.EnterFret(1, autoAdvance: false);
        e7.TryHandleKey(Key.OemPlus, ModifierKeys.Control);
        Eq("hotkey: Ctrl++ inserts a beat without changing the bar length", 16, t7.Measures[0].Cells.Count);
        e7.TryHandleKey(Key.OemMinus, ModifierKeys.Control);
        Eq("hotkey: Ctrl+- deletes a beat without changing the bar length", 16, t7.Measures[0].Cells.Count);

        // Ctrl+A selects the whole track (TuxGuitar "select all").
        var e8 = Editor(out _, out _);
        e8.SetPosition(2, 3, 0);
        e8.SelectAll();
        Check("hotkey: Ctrl+A selects the whole track", e8.HasSelection && e8.CaptureClip(out _) is not null);
    }

    private static void TestEditorScrollGeometry()
    {
        // The window scrolls the score with the editor's own geometry; a mismatch (the old code used a
        // different system height) made auto-scroll drift during playback.
        var editor = new TabEditorControl();
        var project = new SongProject { Tempo = 120 };
        project.Tracks.Add(new TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(24) });
        editor.Project = project;
        editor.SelectedTrackIndex = 0;

        var s0 = editor.ScrollOffsetForMeasure(0);
        var s8 = editor.ScrollOffsetForMeasure(7);
        var s16 = editor.ScrollOffsetForMeasure(14);
        Check("scroll: the first system starts near the top", s0 >= 0 && s0 < s8, $"s0={s0} s8={s8}");
        Check("scroll: systems are evenly spaced",
            Math.Abs((s8 - s0) - (s16 - s8)) < 0.001, $"{s8 - s0} vs {s16 - s8}");
        Check("scroll: a system step is a full system tall", s8 - s0 > 100, $"{s8 - s0}");

        var horizontalGeometry = editor.PlaybackHorizontalGeometry(0, 0.5);
        Check("sheet follow: playback geometry follows the scaled system layout",
            horizontalGeometry is { SystemIndex: 0, BarWidth: > 0, SystemRight: > 0 });

        var belowThreshold = ScoreHorizontalFollow.NextOffset(0, 1000, 900, 100, 4000);
        var firstChunk = ScoreHorizontalFollow.NextOffset(0, 1000, 925, 100, 4000);
        var beforeNextThreshold = ScoreHorizontalFollow.NextOffset(500, 1000, 1424, 100, 4000);
        var secondChunk = ScoreHorizontalFollow.NextOffset(500, 1000, 1425, 100, 4000);
        Check("sheet follow: waits until 0.75 bar before the viewport edge, then advances half a viewport",
            belowThreshold is null && Math.Abs(firstChunk.GetValueOrDefault(-1) - 500) < 0.001);
        Check("sheet follow: holds position between chunk thresholds",
            beforeNextThreshold is null && Math.Abs(secondChunk.GetValueOrDefault(-1) - 1000) < 0.001);
        var noLookAhead = ScoreHorizontalFollow.NextOffset(0, 1000, 950, 100, 3000, 0);
        var oneBarLookAhead = ScoreHorizontalFollow.NextOffset(0, 1000, 950, 100, 3000, 1);
        Check("sheet follow: configured anticipation changes the horizontal trigger",
            noLookAhead is null && Math.Abs(oneBarLookAhead.GetValueOrDefault(-1) - 500) < 0.001);

        var wideBarChunk = ScoreHorizontalFollow.NextOffset(0, 1000, 600, 2000, 4000);
        var wideBarHold = ScoreHorizontalFollow.NextOffset(500, 1000, 600, 2000, 4000);
        Check("sheet follow: an over-wide bar cannot cause repeated jumps at one playhead position",
            Math.Abs(wideBarChunk.GetValueOrDefault(-1) - 500) < 0.001 && wideBarHold is null);
        var seekBack = ScoreHorizontalFollow.NextOffset(1000, 1000, 500, 100, 3000);
        Check("sheet follow: a backward seek brings the playhead back into view",
            Math.Abs(seekBack.GetValueOrDefault(-1) - 250) < 0.001);

        var verticalHold = ScoreVerticalFollow.NextOffset(0, 800, 430, 200, 4000);
        var verticalAdvance = ScoreVerticalFollow.NextOffset(0, 800, 700, 200, 4000);
        var verticalSeekBack = ScoreVerticalFollow.NextOffset(600, 800, 420, 200, 4000);
        var verticalLongSeek = ScoreVerticalFollow.NextOffset(0, 800, 2500, 200, 5000);
        var verticalEndClamp = ScoreVerticalFollow.NextOffset(4000, 800, 4800, 200, 5000);
        var verticalHighTriggerHold = ScoreVerticalFollow.NextOffset(0, 1000, 700, 100, 2000, 20, 80);
        var verticalEarlyTrigger = ScoreVerticalFollow.NextOffset(0, 1000, 700, 100, 2000, 20, 70);
        Check("sheet follow: vertical follow holds between system thresholds and advances into a lower row",
            verticalHold is null && Math.Abs(verticalAdvance.GetValueOrDefault(-1) - 260) < 0.001);
        Check("sheet follow: vertical follow tracks backward and catches up after a long seek",
            Math.Abs(verticalSeekBack.GetValueOrDefault(-1) - 260) < 0.001 &&
            Math.Abs(verticalLongSeek.GetValueOrDefault(-1) - 2060) < 0.001);
        Check("sheet follow: vertical target clamps to the final scrollable extent",
            Math.Abs(verticalEndClamp.GetValueOrDefault(-1) - 4200) < 0.001);
        Check("sheet follow: configured vertical trigger changes when a system advances",
            verticalHighTriggerHold is null && Math.Abs(verticalEarlyTrigger.GetValueOrDefault(-1) - 100) < 0.001);
    }

    private static void TestNoteTimelineQueries()
    {
        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var notes = tl.NotesFor(0);
        Eq("note timeline: 8 notes", 8, notes.Length);

        // Quarter notes: 0-500, 500-1000, ...
        var sounding = NoteTimeline.SoundingAt(notes, 600);
        Eq("note timeline: exactly one note sounds at 600 ms", 1, sounding.Count);
        Eq("note timeline: it is the note that started at 500 ms", 500.0, sounding[0].OnsetMs);

        var ended = NoteTimeline.RecentlyEnded(notes, 600, 2600, 4);
        Check("note timeline: recently-ended finds the note that finished at 500 ms",
            ended.Count == 1 && Math.Abs(ended[0].EndMs - 500) < 0.001, $"count {ended.Count}");

        Eq("note timeline: first index after 600 ms is the note at 1000 ms", 2, NoteTimeline.FirstIndexAfter(notes, 600));
        Check("note timeline: a boundary between 400 and 600 ms is detected",
            NoteTimeline.AnyBoundaryBetween(notes, 400, 600));
        Check("note timeline: no boundary between 600 and 900 ms",
            !NoteTimeline.AnyBoundaryBetween(notes, 600, 900));

        // The score and the fretboard must agree: both read these queries.
        var state = InstrumentVisualizer.Build(p, p.Tracks[0], tl, 600, true, false, 4, false, false, null);
        var visualCurrent = state.Notes.Where(n => n.Role == VisualRole.Current).Select(n => n.OnsetMs).Distinct().ToList();
        Check("note timeline: the fretboard 'now' set equals the shared query",
            visualCurrent.Count == 1 && Math.Abs(visualCurrent[0] - 500) < 0.001, string.Join(",", visualCurrent));
    }

    private static void TestChordVisualisation()
    {
        // A chord must appear as one movement: every note at the same onset shares its role, so the
        // fretboard never animates a chord as a strummed sequence of separate notes.
        var p = SingleTrack();
        var chord = Beat(p, 0, 0, 0, 4, 60);
        chord.Notes.Add(new TabNote { StringIndex = 1, Fret = 4, MidiValue = 64 });
        chord.Notes.Add(new TabNote { StringIndex = 2, Fret = 7, MidiValue = 67 });
        Beat(p, 0, 0, 4, 4, 69);

        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var state = InstrumentVisualizer.Build(p, p.Tracks[0], tl, 10, true, false, 4, false, false, null);
        Eq("chord visualisation: all three chord notes are 'now' together",
            3, state.Notes.Count(n => n.Role == VisualRole.Current));
        Eq("chord visualisation: the next movement is a single onset group",
            1, state.Notes.Count(n => n.Role == VisualRole.Next));
        Check("chord visualisation: the movement path is one node per onset",
            state.Notes.Where(n => n.Role == VisualRole.Current).Select(n => n.OnsetMs).Distinct().Count() == 1);
    }

    private static void TestEditingSelectionVisualisation()
    {
        var p = SingleTrack();
        var chord = Beat(p, 0, 0, 0, 4, 60, 0, 0);
        chord.Notes.Add(new TabNote { StringIndex = 1, Fret = 4, MidiValue = 64 });
        chord.Notes.Add(new TabNote { StringIndex = 2, Fret = 7, MidiValue = 67 });

        var selected = InstrumentVisualizer.BuildEditingSelection(p.Tracks[0], chord, false, false, null);
        Eq("edit selection: all chord tones stay highlighted", 3,
            selected.Notes.Count(n => n.Role == VisualRole.Selected));
        Check("edit selection: notes are not playback current notes",
            selected.Notes.All(n => n.Role != VisualRole.Current) && !selected.IsPlaying && !selected.IsPaused);
        Check("edit selection: selected notes remain full-emphasis without a playback timeline",
            selected.Notes.All(n => n.Emphasis == 1 && !n.Released));

        var emptyBeat = InstrumentVisualizer.BuildEditingSelection(p.Tracks[0], new TabCell { IsRest = true }, false, false, null);
        Check("edit selection: a rest does not retain highlights from the previous beat", emptyBeat.Notes.Count == 0);
    }

    private static void TestTieMerging()
    {
        // A tie destination must not attack: the origin sustains through it.
        var p = SingleTrack();
        Beat(p, 0, 0, 0, 4, 64);
        Beat(p, 0, 0, 4, 4, 64).IsTied = true;
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        Eq("tie: one attack", 1, tl.Events.Count(e => e.IsNoteOn));
        Eq("tie: one note event", 1, tl.Notes.Count);
        Near("tie: sustains through both beats", 1000, tl.Notes[0].DurationMs);

        // A chain of three tied beats.
        var chain = SingleTrack();
        Beat(chain, 0, 0, 0, 4, 64);
        Beat(chain, 0, 0, 4, 4, 64).IsTied = true;
        Beat(chain, 0, 0, 8, 4, 64).IsTied = true;
        var chainTl = MidiTimelineBuilder.Build(chain, new PlaybackOptions());
        Eq("tie chain: one attack", 1, chainTl.Events.Count(e => e.IsNoteOn));
        Near("tie chain: 3 * 500 ms", 1500, chainTl.Notes[0].DurationMs);

        // A tie across a barline (origin at bar-1 beat 4, destination at bar-2 beat 1).
        var across = SingleTrack(2);
        Beat(across, 0, 0, 12, 4, 64);
        Beat(across, 0, 1, 0, 4, 64).IsTied = true;
        var acrossTl = MidiTimelineBuilder.Build(across, new PlaybackOptions());
        Eq("cross-bar tie: one attack", 1, acrossTl.Events.Count(e => e.IsNoteOn));
        Near("cross-bar tie: starts at 1500 ms", 1500, acrossTl.Notes[0].OnsetMs);
        Near("cross-bar tie: ends at 2500 ms", 2500, acrossTl.Notes[0].EndMs);

        // No tie: two attacks.
        var plain = SingleTrack();
        Beat(plain, 0, 0, 0, 4, 64);
        Beat(plain, 0, 0, 4, 4, 64);
        Eq("no tie: two attacks", 2, MidiTimelineBuilder.Build(plain, new PlaybackOptions()).Events.Count(e => e.IsNoteOn));
    }

    private static void TestTupletOnsets()
    {
        // Three eighth-note triplets fill one quarter (500 ms at 120 bpm): onsets 0, 166.67, 333.33.
        var p = SingleTrack();
        for (var i = 0; i < 3; i++)
        {
            var c = Beat(p, 0, 0, i, 8, 60 + i);
            c.IsTriplet = true;
        }
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var onsets = tl.Notes.OrderBy(n => n.OnsetMs).Select(n => n.OnsetMs).ToArray();
        Eq("triplet: three notes", 3, onsets.Length);
        Near("triplet onset 0", 0, onsets[0]);
        Near("triplet onset 1", 500.0 / 3.0, onsets[1]);
        Near("triplet onset 2", 1000.0 / 3.0, onsets[2]);
        Near("triplet duration", 500.0 / 3.0, tl.Notes.OrderBy(n => n.OnsetMs).First().DurationMs);
    }

    private static void TestDottedDuration()
    {
        var p = SingleTrack();
        var c = Beat(p, 0, 0, 0, 4, 64);
        c.Dots = 1;
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        Near("dotted quarter = 750 ms", 750, tl.Notes[0].DurationMs);
    }

    private static void TestTempoChange()
    {
        var p = SingleTrack(2, 120);
        p.Tracks[0].Measures[1].TempoChange = 60;
        for (var b = 0; b < 2; b++) Beat(p, 0, b, 0, 4, 64);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        Near("tempo change: bar 1 starts at 2000 ms", 2000, tl.Bars[1].StartMs);
        Near("tempo change: bar 2 lasts 4000 ms at 60 bpm", 4000, tl.Bars[1].EndMs - tl.Bars[1].StartMs);
        Near("tempo change: total 6000 ms", 6000, tl.TotalMs);
    }

    private static void TestChordSimultaneity()
    {
        var p = SingleTrack();
        var cell = Beat(p, 0, 0, 0, 4, 60);
        for (var i = 1; i < 6; i++)
            cell.Notes.Add(new TabNote { StringIndex = i, Fret = 1, MidiValue = 60 + i });
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        Eq("chord: six note-ons", 6, tl.Events.Count(e => e.IsNoteOn));
        var distinct = tl.Events.Where(e => e.IsNoteOn).Select(e => e.TimeMs).Distinct().Count();
        Eq("chord: all six share one onset", 1, distinct);
    }

    private static void TestChannelAllocation()
    {
        var p = new SongProject { Tempo = 120 };
        for (var t = 0; t < 2; t++)
        {
            var g = new TrackModel { Name = $"G{t}", Kind = TrackKind.Guitar, MidiChannel = 0, Measures = TemplateFactory.Measures(1) };
            g.Measures[0].Cells[0].DurationDenominator = 4;
            g.Measures[0].Cells[0].Notes.Add(new TabNote { Fret = 0, MidiValue = 64 + t });
            p.Tracks.Add(g);
        }
        var drums = new TrackModel { Name = "Dr", Kind = TrackKind.Drums, MidiChannel = 0, Measures = TemplateFactory.Measures(1) };
        drums.Measures[0].Cells[0].DurationDenominator = 4;
        drums.Measures[0].Cells[0].Notes.Add(new TabNote { Fret = 0, MidiValue = 38 });
        p.Tracks.Add(drums);

        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var channels = tl.Events.Where(e => e.IsNoteOn).Select(e => e.Status & 0x0F).Distinct().OrderBy(c => c).ToArray();
        Check("channel allocation: three distinct channels", channels.Length == 3, string.Join(",", channels));
        Check("channel allocation: drums are on channel 9", channels.Contains(9), string.Join(",", channels));
        Check("channel allocation: program changes use the same channels",
            tl.Events.Where(e => (e.Status & 0xF0) == 0xC0).Select(e => e.Status & 0x0F).Distinct().Count() == 3);
    }

    private static void TestLoopBounds()
    {
        var p = SingleTrack(4);
        var opt = new PlaybackOptions { Loop = true, LoopStartBar = 1, LoopEndBar = 2 };
        var tl = MidiTimelineBuilder.Build(p, opt);
        var (start, end, ok) = PlaybackOrder.LoopBounds(tl, p, opt);
        Check("loop: bounds available", ok);
        Near("loop: starts at bar 2 (2000 ms)", 2000, start);
        Near("loop: ends after bar 3 (6000 ms)", 6000, end);

        opt.LoopStartCell = 4;
        opt.LoopEndCell = 7;
        (start, end, ok) = PlaybackOrder.LoopBounds(tl, p, opt);
        Check("loop: selected-cell bounds available", ok);
        Near("loop: starts at the selected cell within bar 2", 2500, start);
        Near("loop: ends after the selected final cell within bar 3", 5000, end);
    }

    private static void TestSeekOffsets()
    {
        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions { StartBar = 1, StartCell = 4 });
        Near("seek: play-from is the cell offset", 500, tl.PlayFromMs);
        var onsets = tl.Notes.OrderBy(n => n.OnsetMs).Select(n => n.OnsetMs).ToArray();
        Eq("seek: skips the beats before the cell", 3, onsets.Length);
        Near("seek: first sounding note is at the cursor", 500, onsets[0]);
        Check("seek: no note starts before the play-from point", onsets.All(o => o >= tl.PlayFromMs - 0.001));
    }

    private static void TestDeterminism()
    {
        var p = SingleTrack(3);
        for (var b = 0; b < 3; b++)
            for (var c = 0; c < 16; c += 2) Beat(p, 0, b, c, 8, 60 + ((b + c) % 12));

        var a = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var b2 = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        Check("determinism: same event count", a.Events.Count == b2.Events.Count, $"{a.Events.Count} vs {b2.Events.Count}");
        var same = a.Events.Count == b2.Events.Count && a.Events.Zip(b2.Events).All(pair =>
            Math.Abs(pair.First.TimeMs - pair.Second.TimeMs) < 1e-9 &&
            pair.First.Status == pair.Second.Status &&
            pair.First.Data1 == pair.Second.Data1 &&
            pair.First.Data2 == pair.Second.Data2);
        Check("determinism: identical event stream on rebuild", same);
    }

    private static void TestMetronomeAndVelocity()
    {
        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions { Metronome = true });
        var clicks = tl.Events.Where(e => e.IsNoteOn && (e.Data1 == 34 || e.Data1 == 33)).OrderBy(e => e.TimeMs).ToList();
        Eq("metronome: 8 clicks over 2 bars", 8, clicks.Count);
        Eq("metronome: first beat of a bar is accented (bell)", 34, clicks[0].Data1);
        Eq("metronome: second beat is a plain click", 33, clicks[1].Data1);

        var ghost = SingleTrack();
        Beat(ghost, 0, 0, 0, 4, 60).Notes[0].Ghost = true;
        Beat(ghost, 0, 0, 4, 4, 60).Notes[0].Velocity = 100;
        var tl2 = MidiTimelineBuilder.Build(ghost, new PlaybackOptions());
        var ons = tl2.Events.Where(e => e.IsNoteOn).OrderBy(e => e.TimeMs).ToList();
        Check("ghost note is quieter than a normal note", ons[0].Data2 < ons[1].Data2, $"{ons[0].Data2} vs {ons[1].Data2}");
    }

    private static void TestLetRing()
    {
        // A let-ring note keeps ringing past its written value, until the next note on the same
        // string - but the ring is capped so it can never become a multi-second drone.
        var p = SingleTrack();
        Beat(p, 0, 0, 0, 8, 60).Notes[0].Techniques.Add("LetRing");   // eighth, 250 ms
        Beat(p, 0, 0, 8, 4, 64);                                      // quarter at 1000 ms
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var notes = tl.Notes.OrderBy(n => n.OnsetMs).ToList();
        var next = notes[1].OnsetMs;
        Check("let-ring: first note rings past its written value", notes[0].EndMs > 260, $"end {notes[0].EndMs:0.#}");
        Check("let-ring: first note stops before the next attack", notes[0].EndMs <= next + 0.001, $"end {notes[0].EndMs:0.#} vs {next}");
        Check("let-ring: the ring is capped (no drone)", notes[0].DurationMs <= 2000, $"dur {notes[0].DurationMs:0.#}");
        Eq("let-ring: each note still attacks", 2, tl.Events.Count(e => e.IsNoteOn));

        // A long run of let-ring notes on one string must not accumulate into a 10-second note.
        var run = SingleTrack();
        for (var n = 0; n < 16; n++) Beat(run, 0, 0, n, 16, 60).Notes[0].Techniques.Add("LetRing");
        var runTl = MidiTimelineBuilder.Build(run, new PlaybackOptions());
        Check("let-ring: a dense run never sustains absurdly long",
            runTl.Notes.All(n => n.DurationMs <= 2000),
            $"max {runTl.Notes.Max(n => n.DurationMs):0.#} ms");

        var repeatedRing = SingleTrack();
        var repeatedRingCell = Beat(repeatedRing, 0, 0, 0, 4, 60);
        repeatedRingCell.Notes[0].Techniques.UnionWith(new[] { "LetRing", "TremoloPick" });
        repeatedRingCell.TremoloPickDenominator = 32;
        var repeatedRingTimeline = MidiTimelineBuilder.Build(repeatedRing, new PlaybackOptions());
        var repeatedRingNote = repeatedRingTimeline.Notes.Single();
        var repeatedOffTimes = repeatedRingNote.OffEventIndices
            .Select(index => repeatedRingTimeline.Events[index].TimeMs).OrderBy(time => time).ToArray();
        Check("let-ring with tremolo picking extends only the final repeated release",
            repeatedOffTimes.Length == 8 && repeatedOffTimes[0] < 100 &&
            repeatedOffTimes[^1] > 900 && repeatedOffTimes[^1] <= 1000.001,
            string.Join(",", repeatedOffTimes.Select(time => time.ToString("0.#"))));
    }

    private static void TestRepeatEndings()
    {
        var p = SingleTrack(4);
        p.Tracks[0].Measures[0].RepeatStart = true;
        p.Tracks[0].Measures[1].AlternateEnding = 1;
        p.Tracks[0].Measures[2].AlternateEnding = 2;
        p.Tracks[0].Measures[3].RepeatEnd = true;
        p.Tracks[0].Measures[3].RepeatCount = 2;
        var order = MidiTimelineBuilder.BuildPlaybackOrder(p, new PlaybackOptions());
        // An ending bracket runs to the repeat close, so the close bar after ending 2 belongs to ending 2.
        Check("alternate endings: first pass uses ending 1, second uses ending 2 (bracket runs to the close)",
            order.SequenceEqual(new[] { 0, 1, 0, 2, 3 }), string.Join(",", order));

        // Standard layout: |: 0 | 1 [1. 2 | 3 :|] [2. 4 ] | 5 -- ending 1 spans bars 2-3, ending 2 follows the close.
        var standard = SingleTrack(6);
        standard.Tracks[0].Measures[0].RepeatStart = true;
        standard.Tracks[0].Measures[2].AlternateEnding = 1;
        standard.Tracks[0].Measures[3].RepeatEnd = true;
        standard.Tracks[0].Measures[3].RepeatCount = 2;
        standard.Tracks[0].Measures[4].AlternateEnding = 2;
        var standardOrder = MidiTimelineBuilder.BuildPlaybackOrder(standard, new PlaybackOptions());
        Check("a first-ending bracket spans every bar up to the repeat close",
            standardOrder.SequenceEqual(new[] { 0, 1, 2, 3, 0, 1, 4, 5 }), string.Join(",", standardOrder));

        // Multi-pass ending (1.2.3.) plays on each of its passes; a close with no open repeats from bar 1.
        var multi = SingleTrack(4);
        multi.Tracks[0].Measures[1].AlternateEnding = 1;
        multi.Tracks[0].Measures[1].AlternateEndingMask = 0b011;
        multi.Tracks[0].Measures[1].RepeatEnd = true;
        multi.Tracks[0].Measures[1].RepeatCount = 3;
        multi.Tracks[0].Measures[2].AlternateEnding = 3;
        var multiOrder = MidiTimelineBuilder.BuildPlaybackOrder(multi, new PlaybackOptions());
        Check("a 1.2. ending plays on passes 1 and 2, and a close with no open repeats from the start",
            multiOrder.SequenceEqual(new[] { 0, 1, 0, 1, 0, 2, 3 }), string.Join(",", multiOrder));

        // A bar that opens and closes its own repeat (x4) plays four times in a row, then the next repeat.
        var single = SingleTrack(4);
        single.Tracks[0].Measures[0].RepeatStart = true;
        single.Tracks[0].Measures[0].RepeatEnd = true;
        single.Tracks[0].Measures[0].RepeatCount = 4;
        single.Tracks[0].Measures[1].RepeatStart = true;
        single.Tracks[0].Measures[2].RepeatEnd = true;
        var singleOrder = MidiTimelineBuilder.BuildPlaybackOrder(single, new PlaybackOptions());
        Check("a one-bar repeat plays its bar on every pass",
            singleOrder.SequenceEqual(new[] { 0, 0, 0, 0, 1, 2, 1, 2, 3 }), string.Join(",", singleOrder));

        // Two opens before one close: the repeat goes back to the later open. More than 8 passes are kept.
        var nested = SingleTrack(5);
        nested.Tracks[0].Measures[1].RepeatStart = true;
        nested.Tracks[0].Measures[2].RepeatStart = true;
        nested.Tracks[0].Measures[3].RepeatEnd = true;
        nested.Tracks[0].Measures[3].RepeatCount = 12;
        var nestedOrder = MidiTimelineBuilder.BuildPlaybackOrder(nested, new PlaybackOptions());
        var expectedNested = new List<int> { 0, 1 };
        for (var pass = 0; pass < 12; pass++) expectedNested.AddRange(new[] { 2, 3 });
        expectedNested.Add(4);
        Check("a repeat goes back to the most recent open and can play 12 times",
            nestedOrder.SequenceEqual(expectedNested), string.Join(",", nestedOrder));

        // "x2" close under a 1.2.3. ending plays twice, then the 4th ending (Aces High, Revelations, I Disappear).
        var counted = SingleTrack(4);
        counted.Tracks[0].Measures[0].RepeatStart = true;
        counted.Tracks[0].Measures[1].AlternateEnding = 1;
        counted.Tracks[0].Measures[1].AlternateEndingMask = 0b0111;
        counted.Tracks[0].Measures[1].RepeatEnd = true;
        counted.Tracks[0].Measures[1].RepeatCount = 2;
        counted.Tracks[0].Measures[2].AlternateEnding = 4;
        var countedOrder = MidiTimelineBuilder.BuildPlaybackOrder(counted, new PlaybackOptions());
        Check("a close plays its own repeat count, then the next ending plays",
            countedOrder.SequenceEqual(new[] { 0, 1, 0, 1, 2, 3 }), string.Join(",", countedOrder));

        // Several closes in one repeat, each with its own ending (The Call of Ktulu): 1st, 2nd, then on.
        var closes = SingleTrack(6);
        closes.Tracks[0].Measures[0].RepeatStart = true;
        closes.Tracks[0].Measures[1].AlternateEnding = 1;
        closes.Tracks[0].Measures[1].RepeatEnd = true;
        closes.Tracks[0].Measures[2].AlternateEnding = 2;
        closes.Tracks[0].Measures[2].RepeatEnd = true;
        closes.Tracks[0].Measures[3].AlternateEnding = 3;
        var closesOrder = MidiTimelineBuilder.BuildPlaybackOrder(closes, new PlaybackOptions());
        Check("each close of a repeat with several endings sends playback back once",
            closesOrder.SequenceEqual(new[] { 0, 1, 0, 2, 0, 3, 4, 5 }), string.Join(",", closesOrder));

        // The 2nd ending bar also opens the next repeat: it plays as the ending, then as that repeat's first bar.
        var chained = SingleTrack(5);
        chained.Tracks[0].Measures[0].RepeatStart = true;
        chained.Tracks[0].Measures[1].AlternateEnding = 1;
        chained.Tracks[0].Measures[1].RepeatEnd = true;
        chained.Tracks[0].Measures[2].AlternateEnding = 2;
        chained.Tracks[0].Measures[2].RepeatStart = true;
        chained.Tracks[0].Measures[3].RepeatEnd = true;
        var chainedOrder = MidiTimelineBuilder.BuildPlaybackOrder(chained, new PlaybackOptions());
        Check("an ending bar that opens the next repeat is repeated with it",
            chainedOrder.SequenceEqual(new[] { 0, 1, 0, 2, 3, 2, 3, 4 }), string.Join(",", chainedOrder));

        // Tempo lasts until the next change; a change halfway through a bar applies from its beat onwards.
        var tempo = SingleTrack(3);
        tempo.Tracks[0].Measures[0].TempoChange = 120;
        tempo.Tracks[0].Measures[0].MidBarTempos = new List<TempoPoint> { new(8, 60) };
        var halfSlowMs = MusicTime.OffsetMs(tempo.Tracks[0].Measures[0], 16, 120);
        Check("a mid-bar tempo change slows the rest of the bar (4/4: 2 beats at 120 + 2 at 60 = 3 s)",
            Math.Abs(halfSlowMs - 3000) < 0.5, $"{halfSlowMs:0.#} ms");
        Check("the tempo after a mid-bar change carries on into later bars",
            MusicTime.TempoAt(tempo, 1) == 60 && MusicTime.TempoAt(tempo, 2) == 60, $"{MusicTime.TempoAt(tempo, 1)}/{MusicTime.TempoAt(tempo, 2)}");
    }

    private static void TestPlayheadMapper()
    {
        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var atHalf = PlayheadMapper.Map(tl, 1000, -1, -1);
        Eq("playhead: bar 1 at 1000 ms", 0, atHalf.Bar);
        Eq("playhead: cell 8 at half of a 4/4 bar", 8, atHalf.Cell);

        var threeFour = SingleTrack();
        threeFour.Tracks[0].Measures[0].TimeSigNum = 3;
        threeFour.Tracks[0].Measures[0].TimeSigDenom = 4;
        Beat(threeFour, 0, 0, 0, 4, 60);
        var tl34 = MidiTimelineBuilder.Build(threeFour, new PlaybackOptions());
        Near("playhead: 3/4 bar lasts 1500 ms", 1500, tl34.TotalMs);
        var third = PlayheadMapper.Map(tl34, 500, -1, -1);
        Eq("playhead: cell 4 at one third of a 3/4 bar", 4, third.Cell);
    }

    private static void TestMidiExportTicks()
    {
        var p = TwoBarSong(120);
        var tl = MidiTimelineBuilder.Build(p, new PlaybackOptions());
        var path = Path.Combine(Path.GetTempPath(), "tabforge-selftest-playback.mid");
        MidiExportService.Export(p, path);
        var events = SmfParser.Read(path);
        var noteOns = events.Count(e => (e.Status & 0xF0) == 0x90 && e.Data2 > 0);
        var expected = tl.Events.Count(e => e.IsNoteOn);
        Eq("export: every played note-on is written", expected, noteOns);
        Check("export: events are spread over the timeline, not all at tick 0",
            events.Select(e => e.Tick).Distinct().Count() > 4, $"{events.Select(e => e.Tick).Distinct().Count()} distinct ticks");
        Check("export: the last event is well past bar 1", events.Max(e => e.Tick) > 960, $"{events.Max(e => e.Tick)}");
        try { File.Delete(path); } catch { }
    }

    /// <summary>Minimal Standard MIDI File reader for the export test (channel messages only).</summary>
    private static class SmfParser
    {
        public readonly record struct Event(int Tick, int Status, int Data1, int Data2);

        public static List<Event> Read(string path)
        {
            var data = File.ReadAllBytes(path);
            var result = new List<Event>();
            var pos = 0;
            int ReadInt(int size) { var v = 0; for (var i = 0; i < size; i++) v = (v << 8) | data[pos++]; return v; }
            ReadInt(4); ReadInt(4); ReadInt(2); var tracks = ReadInt(2); ReadInt(2);
            for (var t = 0; t < tracks; t++)
            {
                ReadInt(4);
                var length = ReadInt(4);
                var end = pos + length;
                var tick = 0;
                var running = 0;
                while (pos < end)
                {
                    var delta = 0;
                    int b;
                    do { b = data[pos++]; delta = (delta << 7) | (b & 0x7F); } while ((b & 0x80) != 0);
                    tick += delta;
                    int status = data[pos];
                    if (status < 0x80) { status = running; } else { pos++; running = status; }
                    if (status == 0xFF)
                    {
                        var type = data[pos++];
                        var len = 0;
                        do { b = data[pos++]; len = (len << 7) | (b & 0x7F); } while ((b & 0x80) != 0);
                        pos += len;
                        if (type == 0x2F) break;
                        continue;
                    }
                    if (status is 0xF0 or 0xF7)
                    {
                        var len = 0;
                        do { b = data[pos++]; len = (len << 7) | (b & 0x7F); } while ((b & 0x80) != 0);
                        pos += len;
                        continue;
                    }
                    var kind = status & 0xF0;
                    var d1 = data[pos++];
                    var d2 = (kind == 0xC0 || kind == 0xD0) ? 0 : data[pos++];
                    result.Add(new Event(tick, status, d1, d2));
                }
                pos = end;
            }
            return result;
        }
    }

    // Re-applying unchanged settings while playing (every click in Settings did this) must not restart
    // the engine: a restart recompiles and reseeks, which is an audible hiccup.
    private static void TestNoOpOptionChangesDoNotRestartPlayback()
    {
        var project = TemplateFactory.Blank();
        using var engine = new PlaybackEngine(new NullMidiOutput());
        var compiles = 0;
        engine.TimelineChanged += _ => compiles++;
        engine.Start(project, new PlaybackOptions { Speed = 1.0, CountInBars = 1 }, _ => { }, () => { }, startPaused: true);
        var afterStart = compiles;
        engine.SetSpeed(project, 1.0);
        engine.UpdateOptions(project, o => o.CountInBars = 1);
        Check("unchanged speed / options while playing do not recompile or restart", compiles == afterStart, $"{compiles - afterStart} restarts");
        engine.SetSpeed(project, 0.5);
        engine.UpdateOptions(project, o => o.CountInBars = 2);
        Check("a real speed or option change still applies immediately", compiles == afterStart + 2, $"{compiles - afterStart} restarts");
        engine.Stop();
    }

    // Count-in: pressing Play with count-in on (metronome off) must click a bar before the music starts.
    private static void TestCountInIsHeard()
    {
        var project = TemplateFactory.Blank();
        project.Tempo = 240;
        using var engine = new PlaybackEngine(new NullMidiOutput());
        engine.StartDiagnostics();
        engine.Start(project, new PlaybackOptions { CountIn = true, CountInBars = 1, Metronome = false }, _ => { }, () => { });
        Thread.Sleep(1400);
        engine.Stop();
        var clicks = engine.DispatchLog.Count(r => r.IsNoteOn && (r.Status & 0x0F) == 9);
        Check("count-in clicks are sent when Play starts with count-in on (metronome off)", clicks >= 3, $"{clicks} count-in clicks");
    }
}