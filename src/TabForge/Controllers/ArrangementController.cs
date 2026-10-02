using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Controllers;

public sealed record SectionClipboardSnapshot(List<List<MeasureModel>> Tracks, MarkerModel? Marker);

/// <summary>Model-side bar operations and arrangement clipboard state; dialogs and refreshes stay in WPF.</summary>
public sealed class ArrangementController
{
    // Bar, area and section copies live on the shared ClipboardService (TimelineClips); only the copied section's own
    // marker (title/colour, not score content) stays here, tied to the clip it belongs to.
    private ScoreClip? _sectionClip;
    private MarkerModel? _sectionMarker;

    /// <summary>The marker of the section copied as <paramref name="clip"/>, or null when the clip is not the last copied section.</summary>
    public MarkerModel? SectionMarkerFor(ScoreClip? clip) => clip is not null && ReferenceEquals(clip, _sectionClip) ? _sectionMarker : null;

    public MarkerModel? SectionAt(SongProject project, int bar) => SectionLayout.At(project, bar);

    public SectionClipboardSnapshot? CaptureSectionSnapshot(SongProject project, MarkerModel marker)
    {
        if (!TryGetSectionBounds(project, marker, out var start, out var end)) return null;
        var tracks = project.Tracks.Select(track => Enumerable.Range(start, end - start)
            .Select(bar => bar < track.Measures.Count ? ProjectService.CloneMeasure(track.Measures[bar]) : new MeasureModel())
            .ToList()).ToList();
        return new SectionClipboardSnapshot(tracks, CloneMarker(marker));
    }

    /// <summary>Copies the section's bars (all tracks) as a Bars clip onto the shared clipboard and remembers its marker.</summary>
    public ScoreClip? CopySection(SongProject project, MarkerModel marker, ClipboardService clipboard, out bool systemClipboardWritten)
    {
        systemClipboardWritten = false;
        if (!TryGetSectionBounds(project, marker, out var start, out var end)) return null;
        var clip = TimelineClips.CopySection(project, start, end);
        systemClipboardWritten = clipboard.Copy(clip);
        _sectionClip = clip;
        _sectionMarker = CloneMarker(marker);
        return clip;
    }

    public bool TryGetSectionBounds(SongProject project, MarkerModel marker, out int start, out int end) =>
        SectionLayout.TryGetBounds(project, marker, out start, out end);

    public bool CanDeleteSection(SongProject project, MarkerModel marker, out int start)
    {
        if (!TryGetSectionBounds(project, marker, out start, out var end)) return false;
        return end - start < MaxMeasures(project);
    }

    public int InsertBar(SongProject project, int at, int templateBar, bool moveMarkers)
    {
        at = Math.Clamp(at, 0, MaxMeasures(project));
        if (moveMarkers)
            foreach (var marker in project.Markers)
            {
                if (marker.LengthBars is int length && at > marker.MeasureIndex && at < marker.MeasureIndex + length) marker.LengthBars = length + 1;
                if (marker.MeasureIndex >= at) marker.MeasureIndex++;
            }
        foreach (var track in project.Tracks)
        {
            var templateIndex = Math.Clamp(templateBar, 0, Math.Max(0, track.Measures.Count - 1));
            var template = track.Measures.Count > 0 ? track.Measures[templateIndex] : null;
            var added = new MeasureModel { Number = at + 1 };
            if (template is not null)
            {
                added.TimeSigNum = template.TimeSigNum;
                added.TimeSigDenom = template.TimeSigDenom;
                added.KeySignature = template.KeySignature;
                added.KeySignatureMinor = template.KeySignatureMinor;
                added.Clef = template.Clef;
            }
            track.Measures.Insert(Math.Min(at, track.Measures.Count), added);
            Renumber(track);
        }
        return at;
    }

    public bool TryToggleMeasureProperty(SongProject project, int selectedTrack, int bar,
        Func<MeasureModel, bool> get, Action<MeasureModel, bool> set, out bool value)
    {
        var selected = GetMeasure(project, selectedTrack, bar);
        if (selected is null)
        {
            value = false;
            return false;
        }

        var next = !get(selected);
        value = next;
        return UpdateMeasuresAtBar(project, bar, measure => set(measure, next));
    }

    public bool TryCycleTripletFeel(SongProject project, int selectedTrack, int bar, out string next)
    {
        var selected = GetMeasure(project, selectedTrack, bar);
        if (selected is null)
        {
            next = "None";
            return false;
        }

        var nextValue = TripletFeels.Effective(selected) switch
        {
            TripletFeels.None => TripletFeels.Eighth,
            TripletFeels.Eighth => TripletFeels.Sixteenth,
            _ => TripletFeels.None
        };
        next = nextValue;
        return UpdateMeasuresAtBar(project, bar, measure =>
        {
            measure.TripletFeelKind = nextValue;
            measure.TripletFeel = nextValue != TripletFeels.None;
        });
    }

    public bool TryToggleSimile(SongProject project, int selectedTrack, int bar, int barCount, out bool enabled)
    {
        var selected = GetMeasure(project, selectedTrack, bar);
        if (selected is null || barCount is not (1 or 2) || bar < barCount)
        {
            enabled = false;
            return false;
        }

        var wasSet = barCount == 1 ? selected.SimileOneBar : selected.SimileTwoBar;
        var next = !wasSet;
        enabled = next;
        return UpdateMeasuresAtBar(project, bar, measure =>
        {
            measure.SimileOneBar = next && barCount == 1;
            measure.SimileTwoBar = next && barCount == 2;
        });
    }

    public bool TryToggleLineBreak(SongProject project, int selectedTrack, int bar, bool force, out bool enabled)
    {
        var selected = GetMeasure(project, selectedTrack, bar);
        if (selected is null)
        {
            enabled = false;
            return false;
        }

        var next = force ? !selected.ForceLineBreak : !selected.PreventLineBreak;
        enabled = next;
        return UpdateMeasuresAtBar(project, bar, measure =>
        {
            if (force) measure.ForceLineBreak = next;
            else measure.PreventLineBreak = next;
        });
    }

    /// <summary>
    /// Time signature from <paramref name="bar"/> up to the next change (or on that bar only): see <see cref="BarSignatures"/>.
    /// All tracks change together.
    /// </summary>
    public bool SetTimeSignature(SongProject project, int bar, int numerator, int denominator, bool untilNextChange = true) =>
        BarSignatures.SetTime(project, bar, numerator, denominator, untilNextChange) >= 0;

    /// <summary>Key signature from <paramref name="bar"/> up to the next change (or on that bar only): see <see cref="BarSignatures"/>.</summary>
    public bool SetKeySignature(SongProject project, int bar, int signature, bool minor, bool untilNextChange = true) =>
        BarSignatures.SetKey(project, bar, signature, minor, untilNextChange) >= 0;

    /// <summary>Project settings: the song's own signature, followed by every bar without an override of its own.</summary>
    public void SetSongTimeSignature(SongProject project, int numerator, int denominator) => BarSignatures.SetSongTime(project, numerator, denominator);

    public void SetSongKeySignature(SongProject project, int signature, bool minor) => BarSignatures.SetSongKey(project, signature, minor);

    public bool TryCycleClef(SongProject project, int selectedTrack, int bar, out string clef)
    {
        var selected = GetMeasure(project, selectedTrack, bar);
        if (selected is null)
        {
            clef = "";
            return false;
        }

        var order = Clefs.Cycle;
        var next = order[(Array.IndexOf(order, selected.Clef) + 1 + order.Length) % order.Length];
        clef = next;
        return UpdateMeasuresAtBar(project, bar, measure => measure.Clef = next);
    }

    public bool TrySetTempoChange(SongProject project, int bar, int tempo) =>
        UpdateMeasuresAtBar(project, bar, measure => measure.TempoChange = tempo);

    public bool TrySetRepeatEnd(SongProject project, int bar, int repeatCount) =>
        UpdateMeasuresAtBar(project, bar, measure =>
        {
            measure.RepeatEnd = true;
            measure.RepeatCount = repeatCount;
        });

    public bool TrySetDirections(SongProject project, int bar, string directions, int alternateEnding) =>
        UpdateMeasuresAtBar(project, bar, measure =>
        {
            measure.Directions = directions;
            measure.AlternateEnding = alternateEnding;
            measure.AlternateEndingMask = 0;
        });

    public bool TrySetSectionName(SongProject project, int bar, string sectionName) =>
        UpdateMeasuresAtBar(project, bar, measure => measure.SectionName = sectionName);

    public bool TrySetSimile(SongProject project, int bar, int barCount, bool enabled)
    {
        if (barCount is not (1 or 2)) return false;
        return UpdateMeasuresAtBar(project, bar, measure =>
        {
            if (barCount == 1) measure.SimileOneBar = enabled;
            else measure.SimileTwoBar = enabled;
        });
    }

    public bool TryToggleTripletFeel(SongProject project, int selectedTrack, int bar, out bool enabled) =>
        TryToggleMeasureProperty(project, selectedTrack, bar,
            measure => measure.TripletFeel, (measure, value) => measure.TripletFeel = value, out enabled);

    public bool TryToggleRepeatStart(SongProject project, int selectedTrack, int bar, out bool enabled) =>
        TryToggleMeasureProperty(project, selectedTrack, bar,
            measure => measure.RepeatStart, (measure, value) => measure.RepeatStart = value, out enabled);

    public bool TryToggleDoubleBar(SongProject project, int selectedTrack, int bar, out bool enabled) =>
        TryToggleMeasureProperty(project, selectedTrack, bar,
            measure => measure.IsDoubleBar, (measure, value) => measure.IsDoubleBar = value, out enabled);

    /// <summary>Duplicate bar / bars: see <see cref="BarRangeEditor.Duplicate"/> (every track gets the copy; one new master bar per copied bar).</summary>
    public int[]? DuplicateBars(SongProject project, int startBar, int endBar) => BarRangeEditor.Duplicate(project, startBar, endBar);

    public int RepeatRange(SongProject project, int startBar, int endBar, int times)
    {
        if (startBar < 0 || endBar < startBar || times < 1) return 0;
        var length = endBar - startBar + 1;
        foreach (var track in project.Tracks)
        {
            for (var repetition = 0; repetition < times; repetition++)
            {
                var clones = new List<MeasureModel>();
                for (var bar = startBar; bar <= endBar && bar < track.Measures.Count; bar++)
                    clones.Add(ProjectService.CloneMeasure(track.Measures[bar]));
                var insertAt = Math.Min(endBar + 1 + repetition * length, track.Measures.Count);
                track.Measures.InsertRange(insertAt, clones);
            }
            Renumber(track);
        }
        return length * times;
    }

    private static MeasureModel? GetMeasure(SongProject project, int trackIndex, int bar) =>
        trackIndex >= 0 && trackIndex < project.Tracks.Count && bar >= 0 &&
        bar < project.Tracks[trackIndex].Measures.Count
            ? project.Tracks[trackIndex].Measures[bar]
            : null;

    private static bool UpdateMeasuresAtBar(SongProject project, int bar, Action<MeasureModel> update)
    {
        if (bar < 0) return false;
        var updated = false;
        foreach (var track in project.Tracks)
        {
            if (bar >= track.Measures.Count) continue;
            update(track.Measures[bar]);
            updated = true;
        }
        return updated;
    }

    private static void Renumber(TrackModel track) => BarRangeEditor.Renumber(track);

    public bool DeleteBar(SongProject project, int bar, int trackIndex, bool allTracks, bool moveMarkers)
    {
        if (bar < 0) return false;
        if (allTracks)
        {
            if (MaxMeasures(project) <= 1) return false;
            foreach (var track in project.Tracks)
            {
                if (bar < track.Measures.Count && track.Measures.Count > 1) track.Measures.RemoveAt(bar);
                Renumber(track);
            }
            if (moveMarkers)
                foreach (var marker in project.Markers)
                {
                    if (marker.LengthBars is int length && bar >= marker.MeasureIndex && bar < marker.MeasureIndex + length)
                        marker.LengthBars = Math.Max(1, length - 1);
                    if (marker.MeasureIndex > bar) marker.MeasureIndex--;
                    else if (marker.MeasureIndex == bar) marker.MeasureIndex = Math.Max(0, bar - 1);
                }
            return true;
        }

        if (trackIndex < 0 || trackIndex >= project.Tracks.Count) return false;
        var selectedTrack = project.Tracks[trackIndex];
        if (selectedTrack.Measures.Count <= 1 || bar >= selectedTrack.Measures.Count) return false;
        selectedTrack.Measures.RemoveAt(bar);
        Renumber(selectedTrack);
        return true;
    }

    public static int MaxMeasures(SongProject project) => BarRangeEditor.MaxMeasures(project);

    // ---- document edits: each is one logical edit of an explicit document (DocumentEdits): one undo transaction, one dirty change, one timeline invalidation ----

    public sealed record BarMove(int At, int[] Map);
    public sealed record BarInserted(int At);

    public EditResult<BarInserted> InsertBar(DocumentSession document, int at, int templateBar, bool moveMarkers) =>
        DocumentEdits.Run(document, project => new BarInserted(InsertBar(project, at, templateBar, moveMarkers)));

    public EditResult DeleteBar(DocumentSession document, int bar, int trackIndex, bool allTracks, bool moveMarkers) =>
        DocumentEdits.Run(document, project => DeleteBar(project, bar, trackIndex, allTracks, moveMarkers));

    /// <summary>Copies bars <paramref name="first"/>..<paramref name="last"/> to right after themselves in every track; the value is the old-to-new bar mapping.</summary>
    public EditResult<int[]> DuplicateBars(DocumentSession document, int first, int last) =>
        DocumentEdits.Run<int[]>(document, project => DuplicateBars(project, first, last));

    public EditResult<int[]> DeleteBars(DocumentSession document, int first, int last) =>
        DocumentEdits.Run<int[]>(document, project => BarRangeEditor.Remove(project, first, last));

    public EditResult<BarMove> MoveBars(DocumentSession document, int first, int last, int insertBefore) =>
        DocumentEdits.Run<BarMove>(document, project => BarRangeEditor.Move(project, first, last, insertBefore) is var (at, map) ? new BarMove(at, map) : null);

    /// <summary>Moves a whole section; <paramref name="before"/> is the undo state taken when a drag started (null: taken now).</summary>
    public EditResult<int[]> MoveSection(DocumentSession document, int from, int insertBefore, UndoSnapshot? before = null) =>
        DocumentEdits.Run<int[]>(document, project => SectionReorderService.Move(project, from, insertBefore), before);

    /// <summary>Inserts copied bars (a duplicated or pasted section, with its marker when it has one) before bar <paramref name="at"/> in every track.</summary>
    public EditResult<int[]> InsertSection(DocumentSession document, int at, SectionClipboardSnapshot snapshot)
    {
        if (document.Project.Tracks.Count == 0 || snapshot.Tracks.Count == 0 || snapshot.Tracks.Max(track => track.Count) == 0) return new EditResult<int[]>(false, null, default);
        at = Math.Clamp(at, 0, MaxMeasures(document.Project));
        return DocumentEdits.Run<int[]>(document, project => snapshot.Marker is null
            ? BarRangeEditor.Insert(project, at, snapshot.Tracks)
            : SectionReorderService.Insert(project, at, snapshot.Tracks, snapshot.Marker));
    }

    public EditResult<SectionReorderService.SectionRemoval> DeleteSection(DocumentSession document, MarkerModel marker) =>
        DocumentEdits.Run(document, project => SectionReorderService.Delete(project, marker));

    private static MarkerModel CloneMarker(MarkerModel marker) => new()
    {
        Title = marker.Title,
        ColorHex = marker.ColorHex,
        LockPosition = marker.LockPosition
    };

}
