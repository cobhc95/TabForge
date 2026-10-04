using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Controllers;

/// <summary>What <see cref="TrackClipboardFlow"/> needs from the window.</summary>
public interface ITrackClipboardHost
{
    /// <summary>The document the track list shows.</summary>
    DocumentSession Document { get; }
    /// <summary>Asks before a track is deleted (themed confirmation); true to go on.</summary>
    bool ConfirmDeleteTrack(string trackName);
    /// <summary>Asks before an instrument track becomes an audio track (themed confirmation); true to go on.</summary>
    bool ConfirmConvertToAudio(string trackName);
    /// <summary>The track list changed: refresh the views and the engine, select <paramref name="selectIndex"/>, show <paramref name="status"/>.</summary>
    void TracksChanged(int selectIndex, string status);
    void SetStatus(string text);
}

// Owns: whole-track copy, cut, paste, duplicate and delete (the track row menu and the track-row hotkeys), each one undo step.
// Does not own: the track list drawing, the key routing and the engine sync (the host refreshes after a change).
// Tests: TestTrackRowMenu.
/// <summary>Copies, cuts, pastes, duplicates and deletes whole tracks: notation, clips, mixer settings, FX chain, colour and name.</summary>
public sealed class TrackClipboardFlow
{
    private readonly ITrackClipboardHost _host;
    private readonly ClipboardService _clipboard;
    private readonly TrackController _tracks;

    public TrackClipboardFlow(ITrackClipboardHost host, ClipboardService clipboard, TrackController tracks)
    {
        _host = host;
        _clipboard = clipboard;
        _tracks = tracks;
    }

    public bool CanPaste => _clipboard.HasTrack;

    /// <summary>Runs a <c>TrackRow.*</c> command on the track at <paramref name="index"/>; false for an unknown id or index.</summary>
    public bool RunHotkey(string id, int index)
    {
        var project = _host.Document.Project;
        if (index < 0 || index >= project.Tracks.Count) return false;
        switch (id)
        {
            case "TrackRow.Copy": Copy(index); return true;
            case "TrackRow.Cut": Cut(index); return true;
            case "TrackRow.Paste": Paste(index); return true;
            case "TrackRow.Duplicate": Duplicate(index); return true;
            case "TrackRow.Delete": Delete(index); return true;
            case "Track.ConvertToAudio": ConvertToAudio(index); return true;
            default: return false;
        }
    }

    /// <summary>Instrument track to audio track after a confirmation: the notation becomes a MIDI clip on the track (one undo step).</summary>
    public void ConvertToAudio(int index)
    {
        var track = _host.Document.Project.Tracks[index];
        if (track.IsAudio || !_host.ConfirmConvertToAudio(track.Name)) return;
        if (!_tracks.ConvertInstrumentToAudio(_host.Document, track).Changed) { _host.SetStatus("Could not convert that track"); return; }
        _host.TracksChanged(index, $"{track.Name} is now an audio track; its notation is a MIDI clip on the track");
    }

    public void Copy(int index)
    {
        var track = _host.Document.Project.Tracks[index];
        _clipboard.CopyTrack(track);
        _host.SetStatus($"Copied track '{track.Name}'");
    }

    /// <summary>Copies the track, then removes it (one undo step). The last track stays.</summary>
    public void Cut(int index)
    {
        var project = _host.Document.Project;
        if (project.Tracks.Count <= 1) { _host.SetStatus("Cannot cut the last track"); return; }
        var track = project.Tracks[index];
        _clipboard.CopyTrack(track);
        _tracks.DeleteTrack(_host.Document, index);
        _host.TracksChanged(Math.Min(index, _host.Document.Project.Tracks.Count - 1), $"Cut track '{track.Name}'");
    }

    /// <summary>Inserts the copied track after <paramref name="index"/> under a unique name (one undo step).</summary>
    public void Paste(int index)
    {
        if (_clipboard.TryGetTrack() is not { } copy) { _host.SetStatus("No track has been copied"); return; }
        Insert(index, copy, $"Pasted track '{copy.Name}'");
    }

    /// <summary>Inserts a copy of the track right after itself under a unique name (one undo step).</summary>
    public void Duplicate(int index)
    {
        var original = _host.Document.Project.Tracks[index];
        var holder = new ClipboardService(null);
        holder.CopyTrack(original);
        if (holder.TryGetTrack() is not { } copy) return;
        Insert(index, copy, $"Duplicated track '{original.Name}'");
    }

    /// <summary>Deletes the track after the host's confirmation (one undo step; undo restores it). The last track stays.</summary>
    public void Delete(int index)
    {
        var project = _host.Document.Project;
        if (project.Tracks.Count <= 1) { _host.SetStatus("Cannot delete the last track"); return; }
        var track = project.Tracks[index];
        if (!_host.ConfirmDeleteTrack(track.Name)) return;
        _tracks.DeleteTrack(_host.Document, index);
        _host.TracksChanged(Math.Min(index, _host.Document.Project.Tracks.Count - 1), $"Deleted track '{track.Name}'");
    }

    private void Insert(int after, TrackModel copy, string status)
    {
        copy.Name = UniqueName(_host.Document.Project, copy.Name);
        _tracks.AddTrack(_host.Document, copy, after + 1);
        _host.TracksChanged(after + 1, status);
    }

    /// <summary>"Name copy", then "Name copy 2"...: the first name no track of the song has.</summary>
    public static string UniqueName(SongProject project, string name)
    {
        var baseName = name.Trim().Length == 0 ? "Track" : name.Trim();
        if (project.Tracks.All(t => !string.Equals(t.Name, baseName, StringComparison.Ordinal))) return baseName;
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? $"{baseName} copy" : $"{baseName} copy {n}";
            if (project.Tracks.All(t => !string.Equals(t.Name, candidate, StringComparison.Ordinal))) return candidate;
        }
    }

    /// <summary>
    /// The track-row command a key runs, or null: only while a track row has the focus and no clip is active, so the score and the
    /// timeline keep Ctrl+C, Delete and the others.
    /// </summary>
    public static string? Route(bool trackRowFocused, bool clipContextActive, IReadOnlyDictionary<string, string> trackRowMap, string gesture) =>
        trackRowFocused && !clipContextActive && trackRowMap.TryGetValue(gesture, out var id) ? id : null;
}
