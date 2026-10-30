using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Presenters;

// Owns: the piano roll's state for the one track it shows: the notes as read from the track's source, the selection, the tool, the note
//       clipboard and the tab prompt of this visit; and committing an edit: each finished gesture or command is one DocumentEdits step (one undo
//       step), after the tab prompt when a tab track's pitch or timing changes for the first time this visit (Cancel applies nothing). After a
//       commit the notes are read back and the edited notes stay selected.
// Does not own: drawing, pointer and keys (the app's PianoRollView), the edits themselves (PianoRollEdits), the note storage (the sources),
//       the prompt window (the caller's confirm function) or the refresh after an edit (the Committed listener).
// Tests: TabForge.Avalonia.Tests.PianoRollPresenterTests.
public sealed class PianoRollPresenter
{
    private readonly Func<IEditableDocument?> _document;
    private readonly Func<string, Task<bool>> _confirmTab;
    private List<PianoRollNote> _notes = new();
    private readonly HashSet<int> _selected = new();
    private PianoRollTimeMap _map = new(Array.Empty<PianoRollBar>());
    private IPianoRollSource? _source;
    private bool _tabConfirmed;
    private bool _committing;

    /// <param name="document">The shown song (null while none is shown).</param>
    /// <param name="confirmTab">Asks "Edit the tab of (track)?" with the track's name; true goes ahead.</param>
    public PianoRollPresenter(Func<IEditableDocument?> document, Func<string, Task<bool>> confirmTab)
    {
        _document = document;
        _confirmTab = confirmTab;
    }

    /// <summary>The track shown (an index into the song's tracks), or -1.</summary>
    public int TrackIndex { get; private set; } = -1;

    public IReadOnlyList<PianoRollNote> Notes => _notes;

    public IReadOnlyCollection<int> Selected => _selected;

    public PianoRollTimeMap Map => _map;

    /// <summary>The tool in use; a new install starts on the Select tool (the arrow), and a tool picked since stays.</summary>
    public PianoRollTool Tool { get; set; } = PianoRollTool.Pointer;

    /// <summary>The scale highlight's pitch classes (null: no scale picked); the view sets it from the settings after each reload.</summary>
    public IReadOnlySet<int>? Scale { get; set; }

    /// <summary>True while new and moved notes snap to <see cref="Scale"/> (the toolbar's Lock toggle; needs a scale).</summary>
    public bool ScaleLock { get; set; }

    /// <summary>True while Up / Down transpose by diatonic steps of <see cref="Scale"/> (the toolbar's transpose mode; semitones with no scale).</summary>
    public bool ScaleSteps { get; set; }

    /// <summary>The copied notes (starts relative to the first); empty until a copy or cut.</summary>
    public List<PianoRollNote> Clipboard { get; private set; } = new();

    /// <summary>True when the shown track's notes are written tab.</summary>
    public bool IsTab => _source?.IsTab == true;

    /// <summary>True once the tab prompt was answered "Edit the tab" in this visit.</summary>
    public bool TabConfirmed => _tabConfirmed;

    /// <summary>True while a commit runs: the song's change notices it raises are the piano roll's own.</summary>
    public bool IsCommitting => _committing;

    /// <summary>The window's answer to "clips now overlap on a lane" (null: overlaps are left as they are). Asked after a note edit grew a MIDI take over another clip.</summary>
    public Func<ClipOverlapPolicy?>? Overlaps { get; set; }

    /// <summary>Raised after a commit changed the song (true for a tab track), so the host can refresh the score, the timeline and the engine.</summary>
    public event Action<bool>? Committed;

    /// <summary>The shown track, or null.</summary>
    public TrackModel? Track => _document() is { } document && TrackIndex >= 0 && TrackIndex < document.Project.Tracks.Count ? document.Project.Tracks[TrackIndex] : null;

    /// <summary>The tab-track prompt: "Edit the tab of (track)?" with Edit the tab / Cancel; asked once per visit before a pitch or timing edit.</summary>
    public static PromptSpec TabPrompt(string trackName) => new(
        $"Edit the tab of “{trackName}”?",
        "This is a tab track. Changing pitch, timing or length here rewrites its tab: strings and frets are picked automatically.\n\n"
        + "Asked once per visit to the piano roll. Velocity and mute changes never ask.",
        YesText: "Edit the tab", NoText: "Cancel",
        YesToolTip: "Apply this edit and the next ones in this visit to the tab",
        NoToolTip: "Discard this edit: nothing is applied, so there is nothing to undo",
        ShowCancel: false, DefaultIsNo: false);

    /// <summary>A new visit to the piano roll (the pane switched to it): the tab prompt will ask again.</summary>
    public void BeginVisit() => _tabConfirmed = false;

    /// <summary>Shows track <paramref name="index"/>; another track is a new visit (no selection, the prompt asks again).</summary>
    public void Show(int index)
    {
        if (index != TrackIndex)
        {
            TrackIndex = index;
            _selected.Clear();
            _tabConfirmed = false;
            Choice = default;   // another track: the track decides again (a clicked take sets the lane after)
        }
        Reload();
    }

    /// <summary>What the roll edits on the shown track: Auto, the written tab, or one MIDI clip lane (every take on it). Another track resets it to Auto.</summary>
    public PianoRollSourceChoice Choice { get; private set; }

    /// <summary>Edits <paramref name="choice"/>'s notes from now on (the selection goes; the tab prompt applies to the tab only).</summary>
    public void UseSource(PianoRollSourceChoice choice)
    {
        if (Choice == choice) return;
        Choice = choice;
        _selected.Clear();
        Reload();
    }

    private int? ClipLane => !IsTab && Choice.Kind == PianoRollSourceKind.Lane ? Choice.Lane : null;

    private bool HasTakeOn(int lane) => Track is { } track && track.AudioClips.Any(clip => clip.IsMidi && clip.Lane == lane);

    // An empty take lane (clicked with no take on it): it has no controller points yet, and none are written until a note makes the take.
    private bool OnEmptyLane => Choice is { Kind: PianoRollSourceKind.Lane, Fresh: true } && !HasTakeOn(Choice.Lane);

    /// <summary>The sources the shown track offers: its written tab (when it has notes) and each lane holding MIDI takes, with a label such as "MIDI lane 1 (3 takes)".</summary>
    public List<(PianoRollSourceChoice Choice, string Label)> SourceOptions()
    {
        var list = new List<(PianoRollSourceChoice, string)>();
        if (Track is not { } track) return list;
        if (PianoRollViewRules.HasWrittenNotes(track)) list.Add((PianoRollSourceChoice.OnTab, "Tab"));
        foreach (var lane in track.AudioClips.Where(clip => clip.IsMidi).Select(clip => clip.Lane).Distinct().Order())
            list.Add((PianoRollSourceChoice.OnLane(lane), LaneLabel(track, lane)));
        return list;
    }

    private static string LaneLabel(TrackModel track, int lane)
    {
        var takes = track.AudioClips.Count(clip => clip.IsMidi && clip.Lane == lane);
        return $"MIDI lane {lane + 1} ({takes} {(takes == 1 ? "take" : "takes")})";
    }

    /// <summary>"Editing: Tab" or "Editing: MIDI lane 1 (3 takes)" (a MIDI track on its playing lanes: "Editing: MIDI (3 takes)").</summary>
    public string SourceLabel
    {
        get
        {
            if (Track is not { } track || _source is null) return "";
            if (_source.IsTab) return "Editing: Tab";
            if (OnEmptyLane) return $"Editing: MIDI lane {Choice.Lane + 1} (no takes)";
            if (Choice.Kind == PianoRollSourceKind.Lane && track.AudioClips.Any(clip => clip.IsMidi && clip.Lane == Choice.Lane)) return "Editing: " + LaneLabel(track, Choice.Lane);
            var takes = PianoRollClipSource.Shown(track).Count;
            return $"Editing: MIDI ({takes} {(takes == 1 ? "take" : "takes")})";
        }
    }

    /// <summary>Reads the shown track's notes and clock again (after an undo or an edit made elsewhere); the selection keeps the notes still there.</summary>
    public void Reload()
    {
        var kept = _selected.Where(i => i < _notes.Count).Select(i => _notes[i]).ToList();
        if (_document() is not { } document || Track is not { } track)
        {
            _source = null;
            _notes = new List<PianoRollNote>();
            _selected.Clear();
            return;
        }
        if (Choice.Fresh && HasTakeOn(Choice.Lane)) Choice = PianoRollSourceChoice.OnLane(Choice.Lane);   // the first note made the take: an ordinary lane from now on
        _source = PianoRollSources.For(track, Choice);
        _map = _source.Map(document.Project);
        _notes = _source.Read(document.Project, track, _map);
        Reselect(kept);
        Reloaded?.Invoke();
    }

    /// <summary>Raised after the notes were read again (a show, a commit, an undo): the view's rows, lanes and sections follow.</summary>
    public event Action? Reloaded;

    /// <summary>Selects the notes nearest to <paramref name="wanted"/> (same pitch, start within half a sixteenth); the rest are deselected.</summary>
    private void Reselect(IEnumerable<PianoRollNote> wanted)
    {
        _selected.Clear();
        foreach (var want in wanted)
        {
            var best = -1;
            var bestDistance = 0.125;
            for (var i = 0; i < _notes.Count; i++)
            {
                if (_notes[i].Pitch != want.Pitch || _selected.Contains(i)) continue;
                var distance = Math.Abs(_notes[i].Start - want.Start);
                if (distance <= bestDistance) { best = i; bestDistance = distance; }
            }
            if (best >= 0) _selected.Add(best);
        }
    }

    public void Select(IEnumerable<int> indexes, bool add = false)
    {
        if (!add) _selected.Clear();
        foreach (var i in indexes) if (i >= 0 && i < _notes.Count) _selected.Add(i);
    }

    public void Toggle(int index)
    {
        if (!_selected.Remove(index) && index >= 0 && index < _notes.Count) _selected.Add(index);
    }

    public void ClearSelection() => _selected.Clear();

    public void SelectAll() => Select(Enumerable.Range(0, _notes.Count));

    /// <summary>Selects every note not selected now (nothing selected: selects all).</summary>
    public void InvertSelection() => Select(Enumerable.Range(0, _notes.Count).Where(i => !_selected.Contains(i)).ToList());

    /// <summary>Selects every note of <paramref name="pitch"/>; with <paramref name="add"/> the row joins the selection.</summary>
    public void SelectPitchRow(int pitch, bool add = false) =>
        Select(Enumerable.Range(0, _notes.Count).Where(i => _notes[i].Pitch == pitch), add);

    /// <summary>Selects every note of the pitch of note <paramref name="index"/>.</summary>
    public void SelectPitch(int index)
    {
        if (index < 0 || index >= _notes.Count) return;
        var pitch = _notes[index].Pitch;
        Select(Enumerable.Range(0, _notes.Count).Where(i => _notes[i].Pitch == pitch));
    }

    /// <summary>
    /// Stores an edit's notes as one undo step labelled <paramref name="label"/> (<paramref name="lockSnap"/> false: the Lock to scale setting does not touch it, as step input follows its own filter). A <see cref="PianoRollEditKind.Notes"/> edit of a tab track asks
    /// first (once per visit); a "Cancel" answer applies nothing and leaves no step. True when the song changed.
    /// </summary>
    public async Task<bool> CommitAsync(PianoRollEditResult edit, PianoRollEditKind kind, string label, bool lockSnap = true)
    {
        if (_document() is not { } document || Track is not { } track || _source is not { } source) return false;
        if (kind == PianoRollEditKind.Notes && source.IsTab && !_tabConfirmed)
        {
            if (!await _confirmTab(track.Name)) return false;
            _tabConfirmed = true;
            if (Track is null) return false;   // the song changed under the prompt
        }
        if (kind == PianoRollEditKind.Notes && lockSnap) edit = SnapLocked(edit);   // drawn, pasted and step-input notes join the scale too (quantize keeps time only, so a locked pitch is already in key)
        var wanted = edit.Selected.Where(i => i >= 0 && i < edit.Notes.Count).Select(i => edit.Notes[i]).ToList();
        var map = _map;
        bool changed;
        var extents = track.AudioClips.Select(clip => (Clip: clip, clip.StartSec, clip.EndSec)).ToList();   // before: a clip that grew or is new is the edited one
        _committing = true;
        try
        {
            changed = DocumentEdits.Run(document, project => project.Tracks.Count > TrackIndex && source.Write(project, project.Tracks[TrackIndex], map, edit.Notes), label: label).Changed;
        }
        finally { _committing = false; }
        if (changed && Overlaps?.Invoke() is { } policy && Track is { } written)
        {
            var grown = written.AudioClips.Where(clip => extents.FindIndex(e => ReferenceEquals(e.Clip, clip)) is var i && (i < 0 || extents[i].StartSec != clip.StartSec || extents[i].EndSec != clip.EndSec)).ToList();
            if (grown.Count > 0 && await policy.ResolveAsync(document, grown) == ClipOverlapOutcome.Cancelled) { Reload(); return false; }   // the edit was undone
        }
        Reload();
        Reselect(wanted);
        if (changed) Committed?.Invoke(source.IsTab);
        return changed;
    }

    // ---- Commands on the selection (each is one step; keys, menus and the info line call them) ----

    private List<int> Sel => _selected.ToList();

    /// <summary>Snaps the edited notes to the scale when the lock is on (drawn, moved, transposed, pasted and step-input notes stay in key).</summary>
    private PianoRollEditResult SnapLocked(PianoRollEditResult edit) =>
        ScaleLock && Scale is not null ? PianoRollEdits.SnapToScale(edit.Notes, edit.Selected, Scale) : edit;

    public Task<bool> MoveAsync(double beats, int semitones, string label = "Move notes") =>
        _selected.Count == 0 ? Task.FromResult(false) : CommitAsync(SnapLocked(PianoRollEdits.Move(_notes, Sel, beats, semitones)), PianoRollEditKind.Notes, label);

    /// <summary>Transposes the selection: by scale steps in scale-steps mode with a scale (an octave follows as seven steps), else by
    /// semitones; a locked scale then pulls the result into key. The Move notes popup passes its own choices (<paramref name="scaleSteps"/>:
    /// the number is literal scale steps; <paramref name="snap"/>: pull into key for this move only, whatever the Lock setting says).</summary>
    public Task<bool> TransposeAsync(int semitones, bool? snap = null, bool? scaleSteps = null)
    {
        if (_selected.Count == 0) return Task.FromResult(false);
        var steps = (scaleSteps ?? ScaleSteps) && Scale is not null;
        var edit = steps
            ? PianoRollEdits.TransposeSteps(_notes, Sel, Scale!, scaleSteps is null ? semitones switch { >= 12 => 7, <= -12 => -7, _ => semitones } : semitones)
            : PianoRollEdits.Move(_notes, Sel, 0, semitones);
        if (snap is null) return CommitAsync(SnapLocked(edit), PianoRollEditKind.Notes, "Transpose notes");
        if (snap == true && Scale is not null) edit = PianoRollEdits.SnapToScale(edit.Notes, edit.Selected, Scale);
        return CommitAsync(edit, PianoRollEditKind.Notes, "Move notes", lockSnap: false);
    }

    public Task<bool> ResizeAsync(double beats) =>
        _selected.Count == 0 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.Resize(_notes, Sel, beats), PianoRollEditKind.Notes, "Resize notes");

    public Task<bool> DeleteAsync() =>
        _selected.Count == 0 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.Delete(_notes, Sel), PianoRollEditKind.Notes, "Delete notes");

    public Task<bool> DuplicateAsync() =>
        _selected.Count == 0 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.Duplicate(_notes, Sel), PianoRollEditKind.Notes, "Duplicate notes");

    public void Copy()
    {
        if (_selected.Count > 0) Clipboard = PianoRollEdits.Copy(_notes, Sel);
    }

    public Task<bool> CutAsync()
    {
        if (_selected.Count == 0) return Task.FromResult(false);
        Copy();
        return CommitAsync(PianoRollEdits.Delete(_notes, Sel), PianoRollEditKind.Notes, "Cut notes");
    }

    /// <summary>Pastes the clipboard at beat <paramref name="at"/> (the playhead).</summary>
    public Task<bool> PasteAsync(double at) =>
        Clipboard.Count == 0 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.Paste(_notes, Clipboard, at), PianoRollEditKind.Notes, "Paste notes");

    public Task<bool> ToggleMuteAsync() =>
        _selected.Count == 0 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.ToggleMute(_notes, Sel), PianoRollEditKind.Expression, "Mute notes");

    /// <summary>Quantizes the selection, or every note when none is selected.</summary>
    public Task<bool> QuantizeAsync(string grid, double strength, bool starts, bool ends, double swing = 0) =>
        _notes.Count == 0 || (!starts && !ends) ? Task.FromResult(false)
            : CommitAsync(PianoRollEdits.Quantize(_notes, Sel, _map, grid, strength, starts, ends, swing), PianoRollEditKind.Notes, "Quantize");

    public Task<bool> SetVelocityAsync(int velocity) =>
        _selected.Count == 0 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.SetVelocity(_notes, Sel, velocity), PianoRollEditKind.Expression, "Set velocity");

    public Task<bool> ScaleVelocityAsync(double percent) =>
        _selected.Count == 0 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.ScaleVelocity(_notes, Sel, percent), PianoRollEditKind.Expression, "Scale velocity");

    public Task<bool> GlueAsync() =>
        _selected.Count < 2 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.Glue(_notes, Sel), PianoRollEditKind.Notes, "Glue notes");

    public Task<bool> GlueAtAsync(int index) => CommitAsync(PianoRollEdits.GlueAt(_notes, index), PianoRollEditKind.Notes, "Glue notes");

    /// <summary>Legato on the selection (every note when none is selected).</summary>
    public Task<bool> LegatoAsync() =>
        _notes.Count == 0 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.Legato(_notes, Sel), PianoRollEditKind.Notes, "Legato");

    public Task<bool> RandomiseVelocityAsync(int amount) =>
        _selected.Count == 0 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.RandomiseVelocity(_notes, Sel, amount, Environment.TickCount), PianoRollEditKind.Expression, "Randomise velocity");

    public Task<bool> HumaniseAsync(double beats) =>
        _selected.Count == 0 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.Humanise(_notes, Sel, beats, Environment.TickCount), PianoRollEditKind.Notes, "Humanise timing");

    /// <summary>Sets the selected MIDI notes' own channel (-1: the track's).</summary>
    public Task<bool> SetChannelAsync(int channel) =>
        _selected.Count == 0 || IsTab ? Task.FromResult(false)
            : CommitAsync(new PianoRollEditResult(_notes.Select((n, i) => _selected.Contains(i) ? n with { Channel = Math.Clamp(channel, -1, 15) } : n).ToList(), Sel), PianoRollEditKind.Expression, "Set channel");

    // ---- Controller lanes (MIDI tracks) ----

    /// <summary>The points of a controller lane on the shown track (none for a tab track).</summary>
    public List<PianoRollPoint> Points(int controller) =>
        OnEmptyLane || Track is not { } track || controller == PianoRollControllers.Velocity || _document() is not { } document ? new List<PianoRollPoint>()
            : PianoRollControllers.Read(track, ControllerMap(document), controller, ClipLane);

    // A tab track keeps its controller points in a controller-only MIDI take on the track (performed clock, as the MIDI roll's own lanes).
    private PianoRollTimeMap ControllerMap(IEditableDocument document) => IsTab ? PianoRollTimeMap.Performed(document.Project) : _map;

    /// <summary>Stores a controller lane's points as one undo step (no prompt; on a tab track the first point creates a controller-only MIDI take).</summary>
    public Task<bool> CommitPointsAsync(int controller, IReadOnlyList<PianoRollPoint> points, string label)
    {
        if (OnEmptyLane || _document() is not { } document || Track is null || controller == PianoRollControllers.Velocity) return Task.FromResult(false);
        var map = ControllerMap(document);
        var lane = ClipLane;
        bool changed;
        _committing = true;
        try { changed = DocumentEdits.Run(document, project => PianoRollControllers.Write(project.Tracks[TrackIndex], map, controller, points, lane), label: label).Changed; }
        finally { _committing = false; }
        Reload();
        if (changed) Committed?.Invoke(false);
        return Task.FromResult(changed);
    }

    public Task<bool> RampVelocityAsync(int first, int last) =>
        _selected.Count == 0 ? Task.FromResult(false) : CommitAsync(PianoRollEdits.Ramp(_notes, Sel, first, last), PianoRollEditKind.Expression, "Velocity ramp");
}
