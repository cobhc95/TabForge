using TabForge.Models;
using TabForge.Ports;

namespace TabForge.Services;

// Owns: the bar-structure edits of a whole song: insert a bar, delete a bar, repeat a bar range (model steps on a SongProject), and each of those
//   plus duplicate as one logical edit of an explicit document (one undo step through IEditTarget.Run).
// Does not own: the range work shared with the timeline (BarRangeEditor), the undo capture (the Documents implementation), dialogs, cursor and status text (the hosts).
// Tests: no dedicated test class.
/// <summary>Insert, delete, duplicate and repeat bars on every track; no state, no toolkit.</summary>
public static class BarStructureEdits
{
    public sealed record BarInserted(int At);

    /// <summary>Inserts an empty bar before bar <paramref name="at"/> on every track, taking meter, key and clef from <paramref name="templateBar"/>; with <paramref name="moveMarkers"/> sections after it move and the one it lands inside grows. Returns the bar index used.</summary>
    public static int InsertBar(SongProject project, int at, int templateBar, bool moveMarkers)
    {
        at = Math.Clamp(at, 0, BarRangeEditor.MaxMeasures(project));
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
            BarRangeEditor.Renumber(track);
        }
        return at;
    }

    /// <summary>Deletes bar <paramref name="bar"/> from every track (<paramref name="allTracks"/>) or from track <paramref name="trackIndex"/> only; the last bar stays. False when nothing was removed.</summary>
    public static bool DeleteBar(SongProject project, int bar, int trackIndex, bool allTracks, bool moveMarkers)
    {
        if (bar < 0) return false;
        if (allTracks)
        {
            if (BarRangeEditor.MaxMeasures(project) <= 1) return false;
            foreach (var track in project.Tracks)
            {
                if (bar < track.Measures.Count && track.Measures.Count > 1) track.Measures.RemoveAt(bar);
                BarRangeEditor.Renumber(track);
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
        BarRangeEditor.Renumber(selectedTrack);
        return true;
    }

    /// <summary>Inserts <paramref name="times"/> copies of bars <paramref name="startBar"/>..<paramref name="endBar"/> right after the range on every track. Returns the bars added (0: a bad range or count).</summary>
    public static int RepeatRange(SongProject project, int startBar, int endBar, int times)
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
            BarRangeEditor.Renumber(track);
        }
        return length * times;
    }

    // ---- document edits: each is one logical edit of an explicit document (one IEditTarget.Run): one undo transaction, one dirty change, one timeline invalidation ----

    /// <summary>Insert as one edit; with <paramref name="fillRests"/> every track's new bar starts as one whole-bar rest.</summary>
    public static EditOutcome<BarInserted> InsertBar(IEditTarget document, int at, int templateBar, bool moveMarkers, bool fillRests = false) =>
        document.Run(project =>
        {
            var added = InsertBar(project, at, templateBar, moveMarkers);
            if (fillRests) for (var t = 0; t < project.Tracks.Count; t++) BarFill.FillBars(project, t, added, added);
            return new BarInserted(added);
        });

    /// <summary>Delete as one edit; the song keeps the bars its clips need.</summary>
    public static EditOutcome DeleteBar(IEditTarget document, int bar, int trackIndex, bool allTracks, bool moveMarkers) =>
        document.Run(project => { var removed = DeleteBar(project, bar, trackIndex, allTracks, moveMarkers); if (removed) SongExtent.EnsureCoversClips(project); return removed; });

    /// <summary>Copies bars <paramref name="first"/>..<paramref name="last"/> to right after themselves in every track; the value is the old-to-new bar mapping (null: the song would get too long).</summary>
    public static EditOutcome<int[]> DuplicateBars(IEditTarget document, int first, int last) =>
        document.Run<int[]>(project => BarRangeEditor.Duplicate(project, first, last));

    /// <summary>Repeat as one edit; <paramref name="times"/> is clamped to 1..16. False when the range or count was refused.</summary>
    public static EditOutcome RepeatRange(IEditTarget document, int first, int last, int times) =>
        document.Run(project => RepeatRange(project, first, last, Math.Clamp(times, 1, 16)) > 0);
}
