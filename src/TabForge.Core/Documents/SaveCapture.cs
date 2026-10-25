using TabForge.Models;

namespace TabForge.Documents;

// Owns: what a save writes: the song as it was the moment the save started (the immutable undo state plus the few fields undo states leave out),
//   rebuilt into a private song on a worker, and the check whether the live song still equals it.
// Does not own: the writing (DocumentController), the clean baseline (SongDocument.MarkCleanFromCapture), the state chunks (ProjectStateEncoder).
// Tests: TabForge.Avalonia.Tests.SaveCaptureTests.
/// <summary>
/// The UI thread's cheap capture for a save (like the autosave's): <see cref="Take"/> encodes only the bars that changed since the last state and copies
/// the fields the undo state omits (track inputs, Band layout, collapsed mixer groups), so <see cref="Rebuild"/> on a worker yields exactly what the song
/// held, and the person can keep editing while the file is written.
/// </summary>
public sealed class SaveCapture
{
    private readonly (string AudioInput, bool MonitorInput)[] _inputs;
    private readonly BandLayoutData? _band;
    private readonly List<string> _collapsedGroups;

    private SaveCapture(ProjectState state, int editVersion, (string, bool)[] inputs, BandLayoutData? band, List<string> collapsedGroups)
    {
        State = state;
        EditVersion = editVersion;
        _inputs = inputs;
        _band = band;
        _collapsedGroups = collapsedGroups;
    }

    public ProjectState State { get; }
    public int EditVersion { get; }

    /// <summary>UI thread: captures <paramref name="document"/>'s song as it is now.</summary>
    public static SaveCapture Take(SongDocument document)
    {
        var project = document.Project;
        return new SaveCapture(DocumentEdits.CurrentState(document), project.EditVersion, InputsOf(project), project.BandLayout?.Copy(), new List<string>(project.Mixer.CollapsedGroups));
    }

    private static (string, bool)[] InputsOf(SongProject project) => project.Tracks.Select(t => (t.AudioInput, t.MonitorInput)).ToArray();

    /// <summary>Any thread: a private song with everything the capture held (the live song is never read).</summary>
    public SongProject Rebuild()
    {
        var project = new ProjectStateEncoder().Restore(State, null, validate: false);
        if (project.Tracks.Count != _inputs.Length) throw new InvalidOperationException("The captured song does not match its track inputs.");
        project.BandLayout = _band?.Copy();
        project.Mixer.CollapsedGroups = new List<string>(_collapsedGroups);
        for (var index = 0; index < _inputs.Length; index++)
            (project.Tracks[index].AudioInput, project.Tracks[index].MonitorInput) = _inputs[index];
        return project;
    }

    /// <summary>
    /// UI thread: true while the song still holds the captured content: no edit, plug-in state or input change since. Content means what the unsaved-changes
    /// hash covers; the Band layout and the collapsed mixer groups are view state outside it, so a change to them never makes the song unsaved.
    /// </summary>
    public bool Matches(SongDocument document)
    {
        var project = document.Project;
        return project.EditVersion == EditVersion && InputsOf(project).AsSpan().SequenceEqual(_inputs) && DocumentEdits.CurrentState(document).ContentEquals(State);
    }
}
