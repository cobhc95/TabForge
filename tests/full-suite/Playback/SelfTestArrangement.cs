using System.Diagnostics;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    private static void TestArrangementFollowGeometry()
    {
        const double viewport = 900;
        const double offset = 1800;
        foreach (var measureWidth in new[] { 12.0, 30.0, 60.0, 90.0 })
        {
            var threeBars = measureWidth * 3;
            var triggerDistance = ArrangementFollowGeometry.RightTriggerDistance(viewport, threeBars);
            var thresholdX = offset + viewport - triggerDistance;
            Check($"arrangement follow threshold at {measureWidth:0} px/bar stays three bars or less from edge",
                !ArrangementFollowGeometry.ShouldAdvance(thresholdX - 0.1, offset, viewport, threeBars) &&
                ArrangementFollowGeometry.ShouldAdvance(thresholdX, offset, viewport, threeBars) &&
                triggerDistance <= threeBars + 0.001 && triggerDistance <= viewport * 0.45 + 0.001);
        }

        Check("arrangement follow advances by half a viewport",
            Math.Abs(ArrangementFollowGeometry.AdvanceOffset(offset, viewport, 5000) - (offset + viewport * 0.5)) < 0.001);
        Check("arrangement follow clamps at the end of the timeline",
            Math.Abs(ArrangementFollowGeometry.AdvanceOffset(offset, viewport, 2100) - 2100) < 0.001);
        Check("backward seeking recenters the playhead and clamps at zero",
            Math.Abs(ArrangementFollowGeometry.OffsetForBackwardSeek(offset + 600, viewport) - (offset + 150)) < 0.001 &&
            ArrangementFollowGeometry.OffsetForBackwardSeek(200, viewport) == 0);

        CheckTimelineGeometryCache();
        CheckArrangementSectionHitGeometry();
        CheckSectionReorder();
        CheckBarRangeEditor();
        CheckSectionLayout();
        CheckInstrumentNamingAndSectionColours();
        TestExplicitSectionColourCopies();
    }

    private static void CheckInstrumentNamingAndSectionColours()
    {
        Check("an extended-range bass or guitar is named by its string count; standard ones are not",
            InstrumentNaming.ForStringCount("Electric Bass (Finger)", TrackKind.Bass, 5) == "Electric Bass (Finger) (5 strings)" &&
            InstrumentNaming.ForStringCount("Electric Bass (Finger) (5 strings)", TrackKind.Bass, 4) == "Electric Bass (Finger)" &&
            InstrumentNaming.ForStringCount("Distortion Guitar", TrackKind.Guitar, 7) == "Distortion Guitar (7 strings)" &&
            InstrumentNaming.ForStringCount("Distortion Guitar (7 strings)", TrackKind.Guitar, 8) == "Distortion Guitar (8 strings)");
        Check("violins, contrabass, pianos and synths are never renamed by string count",
            InstrumentNaming.ForStringCount("Violin", TrackKind.Guitar, 4) == "Violin" &&
            InstrumentNaming.ForStringCount("Contrabass", TrackKind.Bass, 5) == "Contrabass" &&
            InstrumentNaming.ForStringCount("Acoustic Grand Piano", TrackKind.Keys, 7) == "Acoustic Grand Piano" &&
            InstrumentNaming.ForStringCount("Synth Bass 1", TrackKind.Bass, 5) == "Synth Bass 1");

        var markers = new List<MarkerModel>
        {
            new() { Title = "Verse 1", ColorHex = "#2E74B5" }, new() { Title = "Chorus", ColorHex = "#3F9B4F" },
            new() { Title = "Verse 2", ColorHex = "#C24B5A" },
        };
        var own = SectionColours.Resolve(markers, matchSimilar: false, System.Windows.Media.Colors.Gray);
        var ownVerse2 = own[markers[2]];
        var shared = SectionColours.Resolve(markers, matchSimilar: true, System.Windows.Media.Colors.Gray);
        Check("sections of one type share a colour, and the section list reads the timeline's colour",
            shared[markers[0]] == shared[markers[2]] && own[markers[0]] != ownVerse2 &&
            SectionColorValueConverter.SectionColour(markers[2]) == shared[markers[2]]);
    }

    private static void CheckSectionLayout()
    {
        // A: bars 0-3, B: bars 4-5 then gap bars 6-7, C: bars 8-9.
        static SongProject Song()
        {
            var p = new SongProject();
            var track = new TrackModel();
            for (var b = 0; b < 10; b++) track.Measures.Add(new MeasureModel { Number = b + 1, TempoChange = 100 + b });
            p.Tracks.Add(track);
            p.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "A" });
            p.Markers.Add(new MarkerModel { MeasureIndex = 4, Title = "B", LengthBars = 2 });
            p.Markers.Add(new MarkerModel { MeasureIndex = 8, Title = "C" });
            return p;
        }
        static int[] Tempos(SongProject p) => p.Tracks[0].Measures.Select(m => (m.TempoChange ?? 0) - 100).ToArray();
        var song = Song();
        Check("section bounds follow the visible block; gap bars belong to no section",
            SectionLayout.TryGetBounds(song, song.Markers[1], out var s, out var e) && s == 4 && e == 6 &&
            SectionLayout.At(song, 5)?.Title == "B" && SectionLayout.At(song, 6) is null && SectionLayout.At(song, 3)?.Title == "A");

        var deleted = Song();
        var removal = SectionReorderService.Delete(deleted, deleted.Markers[1]);
        Check("deleting a resized section leaves its gap bars and does not widen the previous section",
            removal is not null && Tempos(deleted).SequenceEqual(new[] { 0, 1, 2, 3, 6, 7, 8, 9 }) &&
            SectionLayout.TryGetBounds(deleted, deleted.Markers[0], out _, out var aEnd) && aEnd == 4 &&
            deleted.Markers[1].Title == "C" && deleted.Markers[1].MeasureIndex == 6);

        var moved = Song();
        var map = SectionReorderService.Move(moved, 1, 0);
        Check("moving a resized section carries only its visible bars; its gap stays in place",
            map is not null && Tempos(moved).SequenceEqual(new[] { 4, 5, 0, 1, 2, 3, 6, 7, 8, 9 }) &&
            moved.Markers.Select(m => (m.Title, m.MeasureIndex)).SequenceEqual(new[] { ("B", 0), ("A", 2), ("C", 8) }) &&
            SectionLayout.At(moved, 6) is null && map[4] == 0 && map[6] == 6);

        var plain = Song();
        plain.Markers[1].LengthBars = null;
        Check("moving normal sections is unchanged",
            SectionReorderService.Move(plain, 2, 0) is not null &&
            Tempos(plain).SequenceEqual(new[] { 8, 9, 0, 1, 2, 3, 4, 5, 6, 7 }));

        var resized = Song();
        SectionLayout.ResizeEdge(resized, resized.Markers[1], rightEdge: true, 9);
        Check("growing a section over its gap then into the next section pushes the next section",
            resized.Markers[1].LengthBars is null && resized.Markers[2].MeasureIndex == 9 &&
            SectionLayout.TryGetBounds(resized, resized.Markers[1], out _, out var bEnd) && bEnd == 9);
        var shrunk = Song();
        SectionLayout.ResizeEdge(shrunk, shrunk.Markers[0], rightEdge: true, 2);
        Check("shrinking a section leaves gap bars", shrunk.Markers[0].LengthBars == 2 && SectionLayout.At(shrunk, 3) is null);

        var relabel = Song();
        var tempoBefore = Tempos(relabel);
        // A 0-3, B 4-5, free bars 6-7, C 8-9: B may slide right and C left into the free bars; A cannot move.
        Check("a plain section drag moves the section as a block into free bars only; the bars stay",
            SectionLayout.MoveRange(relabel, relabel.Markers[1]) == (4, 6) &&
            SectionLayout.MoveRange(relabel, relabel.Markers[2]) == (6, 8) &&
            SectionLayout.MoveRange(relabel, relabel.Markers[0]) is null &&
            !SectionLayout.MoveMarker(relabel, relabel.Markers[0], 6) && relabel.Markers[0].MeasureIndex == 0 &&
            Tempos(relabel).SequenceEqual(tempoBefore));
        var slide = Song();
        var slid = SectionLayout.MoveMarker(slide, slide.Markers[1], 9);
        Check("the moved section keeps its length, leaves a gap, and the previous section does not grow into it",
            slid && slide.Markers[1].MeasureIndex == 6 &&
            SectionLayout.TryGetBounds(slide, slide.Markers[1], out var bs, out var be) && be - bs == 2 &&
            SectionLayout.TryGetBounds(slide, slide.Markers[0], out _, out var aEnd2) && aEnd2 == 4 &&
            SectionLayout.At(slide, 5) is null && Tempos(slide).SequenceEqual(tempoBefore));
        var defaultKeys = HotkeyCatalog.BuildMap(new HotkeySettings());
        Check("M adds a section (default key map)", defaultKeys.TryGetValue("M", out var mAction) && mAction == "Section.Add");

        var json = "{\"Tracks\":[{\"Measures\":[{\"Cells\":[{\"BeamMode\":\"force\",\"StemDirection\":\"Sideways\"},{\"StemDirection\":\"Invert\"}]}]}]}";
        var restored = ProjectService.Restore(json);
        var cells = restored.Tracks[0].Measures[0].Cells;
        Check("beam/stem names load case-insensitively; unknown values fall back to Auto",
            cells[0].BeamMode == BeamMode.Force && cells[0].StemDirection == StemDirection.Auto && cells[1].StemDirection == StemDirection.Invert);
        var written = ProjectService.Snapshot(restored);
        Check("beam/stem are still written as their names (file format unchanged)",
            written.Contains("\"BeamMode\": \"Force\"") && written.Contains("\"StemDirection\": \"Invert\"") && !written.Contains("\"StemDirection\": \"Auto\""));
    }

    private static void CheckBarRangeEditor()
    {
        static SongProject Song()
        {
            var p = new SongProject();
            for (var t = 0; t < 2; t++)
            {
                var track = new TrackModel();
                for (var b = 0; b < 8; b++) track.Measures.Add(new MeasureModel { Number = b + 1, TempoChange = 100 + b });
                p.Tracks.Add(track);
            }
            p.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "A" });
            p.Markers.Add(new MarkerModel { MeasureIndex = 4, Title = "B", LengthBars = 2 });
            return p;
        }
        var moved = Song();
        var result = BarRangeEditor.Move(moved, 1, 2, 6);
        Check("bar range move lands the bars before the target and maps every old bar",
            result is { At: 4 } r && r.Map.SequenceEqual(new[] { 0, 4, 5, 1, 2, 3, 6, 7 }) &&
            moved.Tracks.All(t => t.Measures.Select(m => m.TempoChange ?? 0).SequenceEqual(new[] { 100, 103, 104, 105, 101, 102, 106, 107 })) &&
            moved.Tracks.All(t => t.Measures.Select(m => m.Number).SequenceEqual(Enumerable.Range(1, 8))));
        Check("bar range move rejects a target inside the range and leaves the song untouched",
            BarRangeEditor.Move(moved, 1, 2, 2) is null && BarRangeEditor.Move(moved, 1, 2, 3) is null);
        var removed = Song();
        var map = BarRangeEditor.Remove(removed, 1, 2);
        Check("bar range remove shifts later sections and keeps a resized section length",
            map is not null && map[1] == -1 && map[3] == 1 && removed.Markers[1].MeasureIndex == 2 &&
            removed.Markers[1].LengthBars == 2 && removed.Tracks.All(t => t.Measures.Count == 6));
        var emptied = Song();
        Check("bar range remove keeps at least one bar",
            BarRangeEditor.Remove(emptied, 0, 20) is not null && emptied.Tracks.All(t => t.Measures.Count == 1));
        var inserted = Song();
        BarRangeEditor.Insert(inserted, 2, BarRangeEditor.Capture(inserted, 0, 0));
        Check("bar range insert clones bars into every track and shifts later sections",
            inserted.Tracks.All(t => t.Measures.Count == 9 && t.Measures[2].TempoChange == 100 && !ReferenceEquals(t.Measures[2], t.Measures[0])) &&
            inserted.Markers[1].MeasureIndex == 5);

        var feel = new MeasureModel();
        Check("triplet feel: none, legacy flag = 8th swing, explicit kind wins",
            TripletFeels.Effective(feel) == TripletFeels.None &&
            TripletFeels.Effective(new MeasureModel { TripletFeel = true }) == TripletFeels.Eighth &&
            TripletFeels.Effective(new MeasureModel { TripletFeel = true, TripletFeelKind = TripletFeels.Sixteenth }) == TripletFeels.Sixteenth);
        Check("palm mute accepts the legacy PM alias in any case",
            TechniqueNames.HasPalmMute(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pm" }) &&
            TechniqueNames.IsPalmMute("PalmMute") && !TechniqueNames.IsPalmMute("LetRing"));
        var sized = Song();
        var plain = Song();
        plain.Markers[1].LengthBars = null;
        Check("resized section length survives a project snapshot round trip",
            ProjectService.RestoreBytes(ProjectService.SnapshotBytes(sized)).Markers[1].LengthBars == 2 &&
            ProjectService.RestoreBytes(ProjectService.SnapshotBytes(plain)).Markers[1].LengthBars is null);
        var hostile = Song();
        hostile.Markers[1].LengthBars = -5;
        var rejected = false;
        try { ProjectService.RestoreBytes(ProjectService.SnapshotBytes(hostile)); }
        catch (System.IO.InvalidDataException) { rejected = true; }
        Check("an out-of-range section length is rejected by validation", rejected);
    }

    private static void TestExplicitSectionColourCopies()
    {
        var project = new SongProject();
        project.Tracks.Add(new TrackModel
        {
            Name = "Track",
            Measures = Enumerable.Range(0, 6).Select(index => new MeasureModel { Number = index + 1, Cells = new List<TabCell> { new() } }).ToList()
        });
        var explicitMarker = new MarkerModel { MeasureIndex = 2, Title = "Verse", ColorHex = "#C24B5A", ColorIsExplicit = true };
        var other = new MarkerModel { MeasureIndex = 4, Title = "Chorus", ColorHex = "#3F9B4F" };
        project.Markers.AddRange(new[] { explicitMarker, other });

        SectionReorderService.Move(project, from: 2, insertBefore: 4);
        Check("section reorder keeps an explicit section colour",
            explicitMarker.ColorIsExplicit && explicitMarker.ColorHex == "#C24B5A" && !other.ColorIsExplicit);

        var section = new List<List<MeasureModel>> { new() { new MeasureModel { Number = 1, Cells = new List<TabCell> { new() } } } };
        SectionReorderService.Insert(project, 6, section, explicitMarker);
        var copy = project.Markers.FirstOrDefault(marker => marker.MeasureIndex == 6);
        Check("section duplicate keeps an explicit section colour",
            copy is not null && copy.ColorIsExplicit && copy.ColorHex == "#C24B5A");
    }

    private static void CheckSectionReorder()
    {
        static TrackModel TrackWithBars(int count) => new()
        {
            Name = $"Track {count}",
            Measures = Enumerable.Range(0, count).Select(index => new MeasureModel
            {
                Number = index + 1,
                Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList()
            }).ToList()
        };

        var project = new SongProject();
        var main = TrackWithBars(6);
        var shortTrack = TrackWithBars(4);
        var emptyTrack = TrackWithBars(0);
        var shortVerseBar = shortTrack.Measures[2];
        var chorusBar = main.Measures[4];
        chorusBar.TimeSigNum = 3;
        chorusBar.TimeSigDenom = 4;
        chorusBar.TempoChange = 151;
        chorusBar.SectionName = "Chorus label";
        var bend = new TabNote { StringIndex = 0, Fret = 7, MidiValue = 71, Techniques = { "Bend", "PalmMute" } };
        chorusBar.Cells[0].Notes.Add(bend);
        main.Measures[5].Cells[0].Notes.Add(new TabNote
        {
            StringIndex = 0, Fret = 7, MidiValue = 71, Tied = true, Techniques = { "PalmMute" }
        });
        project.Tracks.Add(main);
        project.Tracks.Add(shortTrack);
        project.Tracks.Add(emptyTrack);
        var intro = new MarkerModel { MeasureIndex = 0, Title = "Intro", ColorHex = "#101010" };
        var verse = new MarkerModel { MeasureIndex = 2, Title = "Verse", ColorHex = "#202020" };
        var chorus = new MarkerModel { MeasureIndex = 4, Title = "Chorus", ColorHex = "#303030" };
        project.Markers.AddRange(new[] { intro, verse, chorus });
        var before = ProjectService.SnapshotBytes(project);

        var move = SectionReorderService.Move(project, from: 2, insertBefore: 1);
        Check("whole-section reorder maps every old bar deterministically",
            move is not null && move.SequenceEqual(new[] { 0, 1, 4, 5, 2, 3 }));
        Check("section reorder carries complete measure semantics and note techniques",
            ReferenceEquals(main.Measures[2], chorusBar) && main.Measures[2].TempoChange == 151 &&
            main.Measures[2].TimeSigNum == 3 && main.Measures[2].TimeSigDenom == 4 &&
            main.Measures[2].SectionName == "Chorus label" && ReferenceEquals(main.Measures[2].Cells[0].Notes[0], bend) &&
            bend.Techniques.Contains("Bend") && bend.Techniques.Contains("PalmMute") && main.Measures[3].Cells[0].Notes[0].Tied);
        Check("section metadata is reordered with its preserved length",
            project.Markers.SequenceEqual(new[] { intro, chorus, verse }) && chorus.MeasureIndex == 2 && verse.MeasureIndex == 4);
        Check("short and empty tracks remain bar-aligned after section movement",
            project.Tracks.All(track => track.Measures.Count == 6) &&
            ReferenceEquals(shortTrack.Measures[4], shortVerseBar) &&
            emptyTrack.Measures[0].Cells.All(cell => cell.Notes.Count == 0));
        Check("playback bar remaps compose across consecutive reorders",
            move is not null && SectionReorderService.ComposeBarRemap(new[] { 0, 1, 2, 3, 4, 5 }, move).SequenceEqual(move));
        Check("undo and redo playback remaps rebase to a newly compiled timeline",
            move is not null && SectionReorderService.RebaseBarRemap(move, new[] { 0, 1, 2, 3, 4, 5 }, 6)
                .SequenceEqual(new[] { 0, 1, 4, 5, 2, 3 }) &&
            SectionReorderService.RebaseBarRemap(move, move, 6).SequenceEqual(new[] { 0, 1, 2, 3, 4, 5 }));

        var after = ProjectService.SnapshotBytes(project);
        var undo = ProjectService.RestoreBytes(before);
        var redo = ProjectService.RestoreBytes(after);
        Check("section move snapshots restore for atomic undo and redo",
            undo.Markers.OrderBy(marker => marker.MeasureIndex).Select(marker => marker.Title)
                .SequenceEqual(new[] { "Intro", "Verse", "Chorus" }) &&
            redo.Markers.OrderBy(marker => marker.MeasureIndex).Select(marker => marker.Title)
                .SequenceEqual(new[] { "Intro", "Chorus", "Verse" }) &&
            redo.Tracks[0].Measures[2].TempoChange == 151);

        var lockedProject = ProjectService.RestoreBytes(before);
        lockedProject.Markers.Single(marker => marker.Title == "Verse").LockPosition = true;
        var untouchedBar = lockedProject.Tracks[0].Measures[4];
        var lockedReload = ProjectService.RestoreBytes(ProjectService.SnapshotBytes(lockedProject));
        Check("section position locks default off and persist through save/reload",
            !intro.LockPosition && lockedReload.Markers.Single(marker => marker.Title == "Verse").LockPosition);
        Check("locked sections cannot be dragged or shifted by another section move",
            SectionReorderService.Move(lockedProject, from: 2, insertBefore: 1) is null &&
            SectionReorderService.Move(lockedProject, from: 1, insertBefore: 0) is null &&
            ReferenceEquals(lockedProject.Tracks[0].Measures[4], untouchedBar));

        CheckSectionContentOperations();
    }

    private static void CheckSectionContentOperations()
    {
        static TrackModel Track(int count) => new()
        {
            Measures = Enumerable.Range(0, count).Select(index => new MeasureModel { Number = index + 1 }).ToList()
        };

        var project = new SongProject();
        var main = Track(6);
        var shortTrack = Track(4);
        var emptyTrack = Track(0);
        var sourceNote = new TabNote { StringIndex = 0, Fret = 3, MidiValue = 60 };
        main.Measures[2].Cells[0].Notes.Add(sourceNote);
        main.Measures[2].TempoChange = 145;
        project.Tracks.AddRange(new[] { main, shortTrack, emptyTrack });
        project.Markers.AddRange(new[]
        {
            new MarkerModel { MeasureIndex = 0, Title = "Intro" },
            new MarkerModel { MeasureIndex = 2, Title = "Verse", ColorHex = "#123456", LockPosition = true },
            new MarkerModel { MeasureIndex = 4, Title = "Chorus" }
        });
        var copiedTracks = project.Tracks.Select(track => track.Measures.Skip(2).Take(2).ToList()).ToList();
        var sectionMarker = new MarkerModel { Title = "Verse", ColorHex = "#123456", LockPosition = true };

        var insertedMap = SectionReorderService.Insert(project, 4, copiedTracks, sectionMarker);
        var extendedPlaybackMap = insertedMap is null ? Array.Empty<int>()
            : SectionReorderService.ComposeBarRemapWithInsertions(Enumerable.Range(0, 6).ToArray(), insertedMap, 8);
        var duplicateMarker = project.Markers.SingleOrDefault(marker => marker.MeasureIndex == 4);
        var independentNote = main.Measures[4].Cells[0].Notes.FirstOrDefault();
        if (independentNote is not null) independentNote.MidiValue = 48;
        Check("section duplicate inserts immediately after the selected range and shifts later markers",
            insertedMap is not null && insertedMap.SequenceEqual(new[] { 0, 1, 2, 3, 6, 7 }) &&
            duplicateMarker?.Title == "Verse" && project.Markers.Single(marker => marker.Title == "Chorus").MeasureIndex == 6);
        Check("section copy preserves measure metadata and creates independent nested note objects",
            independentNote is { MidiValue: 48 } && sourceNote.MidiValue == 60 &&
            !ReferenceEquals(main.Measures[2], main.Measures[4]) &&
            !ReferenceEquals(sourceNote, independentNote) && main.Measures[4].TempoChange == 145);
        Check("section insertion keeps all tracks aligned and gives the copied marker its own reference",
            project.Tracks.All(track => track.Measures.Count == 8) && duplicateMarker is not null &&
            !ReferenceEquals(duplicateMarker, sectionMarker) && duplicateMarker.LockPosition);
        Check("inserted bars receive distinct playback timeline ids",
            extendedPlaybackMap.SequenceEqual(new[] { 0, 1, 2, 3, 6, 7, 4, 5 }));

        if (duplicateMarker is not null)
        {
            var removal = SectionReorderService.Delete(project, duplicateMarker);
            Check("section delete removes all copied bars and returns a live continuation mapping",
                removal is { ContinueAtBar: 4 } &&
                removal.OldToNewBar.SequenceEqual(new[] { 0, 1, 2, 3, -1, -1, 4, 5 }) &&
                project.Tracks.All(track => track.Measures.Count == 6) &&
                project.Markers.Single(marker => marker.Title == "Chorus").MeasureIndex == 4);
        }

        var lastSectionProject = new SongProject();
        lastSectionProject.Tracks.Add(Track(2));
        var onlySection = new MarkerModel { MeasureIndex = 0, Title = "Only" };
        lastSectionProject.Markers.Add(onlySection);
        Check("section deletion protects the last remaining score bars",
            SectionReorderService.Delete(lastSectionProject, onlySection) is null &&
            lastSectionProject.Tracks[0].Measures.Count == 2);
    }

    private static void CheckArrangementSectionHitGeometry()
    {
        var project = new SongProject();
        project.Tracks.Add(new TrackModel
        {
            Measures = Enumerable.Range(0, 8).Select(_ => new MeasureModel()).ToList()
        });
        project.Markers.AddRange(new[]
        {
            new MarkerModel { MeasureIndex = 0, Title = "Intro" },
            new MarkerModel { MeasureIndex = 1, Title = "Verse 1" },
            new MarkerModel { MeasureIndex = 6, Title = "Break" }
        });
        var timeline = new TrackTimeline { Project = project, MeasureWidth = 24 };
        var nearBlockBottom = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight - 4;

        Check("section header hit target covers the colored block beyond its label",
            timeline.SectionStartBarAt(22, nearBlockBottom) == 0);
        Check("section header hit target selects the first bar of a long section",
            timeline.SectionStartBarAt(140, ArrangementPanel.RulerHeight + 2) == 1);
        Check("one-bar and final sections remain clickable across their full width",
            timeline.SectionStartBarAt(190, nearBlockBottom) == 6);
        Check("section-icon hit testing is distinct from timeline hit testing",
            timeline.SectionMarkerAt(22, nearBlockBottom) == 0 &&
            timeline.SectionMarkerAt(140, ArrangementPanel.RulerHeight + 2) == 1 &&
            timeline.SectionMarkerAt(24, ArrangementPanel.RulerHeight + 8) == -1 &&
            timeline.SectionMarkerAt(40, ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + 2) == -1);
        Check("section hit testing excludes the ruler and gaps between blocks",
            timeline.SectionStartBarAt(24, ArrangementPanel.RulerHeight + 8) == -1 &&
            timeline.SectionStartBarAt(22, ArrangementPanel.RulerHeight) == -1);
    }

    private static void CheckTimelineGeometryCache()
    {
        var project = new SongProject { TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        var measures = Enumerable.Range(0, 6).Select(_ => new MeasureModel()).ToList();
        measures[1].TimeSigNum = 3;
        measures[1].TimeSigDenom = 4;
        measures[3].TimeSigNum = 6;
        measures[3].TimeSigDenom = 8;
        measures[4].TimeSigNum = 2;
        measures[4].TimeSigDenom = 4;
        project.Tracks.Add(new TrackModel { Measures = measures });
        var geometry = new TimelineGeometry(project, 32);

        var matches = geometry.BarCount == 6 && geometry.Matches(project, 32);
        for (var bar = 0; bar < geometry.BarCount; bar++)
        {
            var expectedWidth = MusicTime.BarWidth(project, bar, 32);
            var expectedX = MusicTime.BarX(project, bar, 32);
            matches &= Math.Abs(geometry.WidthOfBar(bar) - expectedWidth) < 0.001 &&
                       Math.Abs(geometry.XOfBar(bar) - expectedX) < 0.001 &&
                       Math.Abs(geometry.RightOfBar(bar) - (expectedX + expectedWidth)) < 0.001 &&
                       geometry.BarAt(expectedX + expectedWidth * 0.5) == bar;
        }
        matches &= Math.Abs(geometry.TotalWidth - MusicTime.TotalWidth(project, 32)) < 0.001 &&
                   geometry.BarAt(-1) == 0 && geometry.BarAt(geometry.TotalWidth) == geometry.BarCount - 1;
        Check("canonical timeline geometry matches mixed-meter bar boundaries and binary hit mapping", matches);

        measures[2].TimeSigNum = 5;
        measures[2].TimeSigDenom = 4;
        Check("timeline geometry shape validation detects a time-signature width change",
            !geometry.Matches(project, 32));
        measures[2].TimeSigNum = null;
        measures[2].TimeSigDenom = null;
        measures[2].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3 });
        Check("timeline geometry shape validation ignores score-content edits", geometry.Matches(project, 32));

        const int queryCount = 15_000;
        var benchmarkProject = new SongProject();
        benchmarkProject.Tracks.Add(new TrackModel
        {
            Measures = Enumerable.Range(0, 1024).Select(index => new MeasureModel
            {
                TimeSigNum = index % 3 == 0 ? 3 : 4,
                TimeSigDenom = 4,
                Cells = new List<TabCell>()
            }).ToList()
        });
        var benchmarkGeometry = new TimelineGeometry(benchmarkProject, 30);
        var queries = new double[queryCount];
        for (var index = 0; index < queries.Length; index++)
            queries[index] = ((index * 7919L) % 1_000_003) / 1_000_003.0 * benchmarkGeometry.TotalWidth;

        var scanSum = 0;
        var timer = Stopwatch.StartNew();
        foreach (var x in queries)
        {
            var position = 0.0;
            var found = Math.Max(0, benchmarkGeometry.BarCount - 1);
            for (var bar = 0; bar < benchmarkGeometry.BarCount; bar++)
            {
                var width = MusicTime.BarWidth(benchmarkProject, bar, 30);
                if (x >= position && x < position + width) { found = bar; break; }
                position += width;
            }
            scanSum += found;
        }
        var linearMs = timer.Elapsed.TotalMilliseconds;

        var binarySum = 0;
        timer.Restart();
        foreach (var x in queries) binarySum += benchmarkGeometry.BarAt(x);
        var binaryMs = timer.Elapsed.TotalMilliseconds;
        Check("timeline geometry benchmark returns identical results to the prior sequential scan", scanSum == binarySum);
        Log.Add($"  PERF  BarAt {queryCount:N0} queries over 1,024 mixed-width bars: linear {linearMs:0.###} ms; cached binary {binaryMs:0.###} ms; speedup {(binaryMs > 0 ? linearMs / binaryMs : 0):0.##}x");
    }
}
