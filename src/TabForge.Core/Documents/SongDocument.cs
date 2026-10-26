using System.Linq;
using System.Security.Cryptography;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Documents;

// Owns: one open song without its playback: the score, its file, undo history, the unsaved-changes baseline, media context and the editor's own
//     per-tab state. Saving and the edit pipeline take it explicitly.
// Does not own: playback and engine chains (the app's AvaloniaPlaybackSessions adds them), the tabs (DocumentManager, MainWindowTabs), writing files
//     (DocumentController, DocumentSaveFlow).
// Tests: TestSaveFlowCharacterization, MainWindowDocumentsTests, TabForge.Avalonia.Tests.MainWindowSaveTests.
/// <summary>
/// One open song. Holds everything that is per-document so several songs can be open at once: the score, the file, undo history and the editor's
/// own state. UI thread only.
/// </summary>
public class SongDocument : IEditableDocument
{
    private byte[]? _cleanContentHash;
    private ProjectState? _cleanState;
    private int _cleanVersion;
    // Cached unsaved-hash: hash of Project at _cachedVersion for instance _cachedProject.
    // Reused across redraws/close-prompts/save-outcomes; recomputed only when the version moves.
    private SongProject? _cachedProject;
    private int _cachedVersion = -1;
    private byte[]? _cachedHash;

    /// <param name="audioSettings">The app's audio settings, read by the media context (media folders, approval).</param>
    /// <param name="persistMedia">Stores a media-context change in the app settings; null when nothing is stored.</param>
    public SongDocument(Func<AudioSettings?> audioSettings, Action<MediaContext>? persistMedia = null)
    {
        Media = new MediaContext(audioSettings, persistMedia);
        Undo = new UndoController { HighlightSource = () => Highlight.Snapshot(), LoopSource = () => Selection.LoopSnapshot() };
        _project.DisplayStateChanged += OnProjectDisplayState;
        _project.TimelineMarked += OnProjectTimelineMarked;
    }

    private SongProject _project = TemplateFactory.Blank();
    public SongProject Project
    {
        get => _project;
        set
        {
            if (ReferenceEquals(_project, value)) return;
            _project.DisplayStateChanged -= OnProjectDisplayState;
            _project.TimelineMarked -= OnProjectTimelineMarked;
            value.Mixer.App ??= _project.Mixer.App;   // an undone / replaced song keeps following the app-wide group rules
            var old = _project;
            _project = value;
            Selection.Rebind(value);
            _project.DisplayStateChanged += OnProjectDisplayState;
            _project.TimelineMarked += OnProjectTimelineMarked;
            DisplayStateChanged?.Invoke(this, EventArgs.Empty);
            ProjectChanged?.Invoke(old, value);
        }
    }
    private void OnProjectDisplayState(object? sender, EventArgs e) => DisplayStateChanged?.Invoke(this, EventArgs.Empty);
    private void OnProjectTimelineMarked(SongProject project)
    {
        OnTimelineMarked(project);
        TimelineMarked?.Invoke(project);
    }

    /// <inheritdoc />
    public event Action<SongProject>? TimelineMarked;
    /// <inheritdoc />
    public event Action<SongProject, SongProject>? ProjectChanged;
    /// <inheritdoc />
    public event Action? Restored;
    /// <inheritdoc />
    public void NotifyRestored() => Restored?.Invoke();

    /// <summary>Any song change that affects timing (every edit path, paste, bar grid, undo and redo) arrives here; playback overrides it.</summary>
    protected virtual void OnTimelineMarked(SongProject project) { }

    /// <summary>Raised when what a tab shows (name, tooltip, unsaved mark) may have changed: a save, a rename, an edit, an undo.</summary>
    public event EventHandler? DisplayStateChanged;
    private string? _path;

    /// <summary>The file this song was saved to or opened from (null: unsaved). Setting it (Save As, first save, recovery) moves the song's media scope and starts a new media revision.</summary>
    public string? Path
    {
        get => _path;
        set { _path = value; if (value is not null) ImportSourcePath = null; Media.SetSavedPath(value); DisplayStateChanged?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>This song's media context: identity, media base directory, approval scope and revision. Passed explicitly to everything that reads its media.</summary>
    public MediaContext Media { get; }
    public UndoController Undo { get; }

    /// <summary>This song's one selection: its tab keeps it while another tab is shown, and a tab moved to another window takes it along.</summary>
    public SelectionModel Selection { get; } = new();
    /// <summary>View-layer objects that belong to this song and live and die with it (the floating plug-in sets); the core never reads them.</summary>
    public List<object> ViewAttachments { get; } = new();

    /// <summary>This song's timeline bar highlight (grey cells): a view state kept with the song while another tab shows, restored by undo and redo, never saved in the file.</summary>
    public BarHighlight Highlight { get; } = new();

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ProjectState, SelectionSnapshot> _selectionByState = new();
    /// <inheritdoc />
    public void RememberSelection(ProjectState state, SelectionSnapshot selection) => _selectionByState.AddOrUpdate(state, selection);
    /// <inheritdoc />
    public void RestoreSelection(ProjectState state)
    {
        var barCount = BarRangeEditor.MaxMeasures(Project);
        if (_selectionByState.TryGetValue(state, out var selection)) Selection.Restore(selection, Project, barCount);
        else Selection.Restore(Selection.Snapshot(), Project, barCount);
    }
    public bool IsNew { get; set; } = true;

    // Editor state, restored when the user switches back to this tab.
    public int CursorBar { get; set; }
    public int CursorCell { get; set; }
    public int CursorString { get; set; }
    public int ActiveVoiceIndex { get; set; }
    private int _trackIndex;
    /// <summary>The selected track; setting a different index moves the selection to it (a one-track range is cleared, S6) and raises <see cref="TrackIndexChanged"/>.</summary>
    public int TrackIndex
    {
        get => _trackIndex;
        set { if (_trackIndex == value) return; _trackIndex = value; Selection.SetTrack(value); TrackIndexChanged?.Invoke(); }
    }

    /// <summary>Raised when the selected track changes, so the score and the panels show the new track.</summary>
    public event Action? TrackIndexChanged;
    public NotationMode Notation { get; set; } = NotationMode.TabAndStaff;
    public double ZoomFactor { get; set; } = 1.0;   // 1 = 100% (the default); 0 = fit width
    public bool ContinuousScoreView { get; set; } = true;
    /// <summary>Score as one line scrolling right instead of wrapped lines scrolling down.</summary>
    public bool HorizontalScoreView { get; set; }
    private bool _darkPaper = true;
    /// <summary>True when this song's score page is the dark paper. Session only: the song file does not store it, so a reopened song
    /// starts on the paper the preferences default names (Appearance.ScorePaper).</summary>
    public bool DarkPaper
    {
        get => _darkPaper;
        set
        {
            PaperSet = true;
            PaperIsDefault = false;
            SetPaper(value);
        }
    }

    private void SetPaper(bool dark)
    {
        if (_darkPaper == dark) return;
        _darkPaper = dark;
        ScorePaperChanged?.Invoke();
    }

    /// <summary>True while the paper is the preferences default (the theme's paper), not one this song's View menu chose; such a song follows a theme change.</summary>
    public bool PaperIsDefault { get; private set; }

    /// <summary>Gives the song the preferences default paper; it keeps following that default until a paper is chosen for the song.</summary>
    public void ApplyDefaultPaper(bool dark)
    {
        PaperSet = true;
        PaperIsDefault = true;
        SetPaper(dark);
    }
    /// <summary>True once this song's paper was chosen (the View menu) or given its default (a tab joining a window); session only.</summary>
    public bool PaperSet { get; private set; }
    /// <summary>Raised when <see cref="DarkPaper"/> changed, so every view of this song repaints on the new paper.</summary>
    public event Action? ScorePaperChanged;
    public int DurationDenominator { get; set; } = 4;
    public int DurationDots { get; set; }
    public bool DurationTriplet { get; set; }
    public int TupletNumerator { get; set; }
    public int TupletDenominator { get; set; }

    // Transport/practice state of this song, restored when its tab becomes active again (the loop region and Loop button live in Selection).
    /// <summary>Bar ranges skipped during playback ("Skip area during playback").</summary>
    public List<(int Start, int End)> SkipRanges { get; } = new();
    /// <summary>
    /// Plug-ins (full paths) the user chose to disable after a very slow load: skipped for this song while it stays open.
    /// Unlike quarantine this is not saved and does not affect other songs.
    /// </summary>
    public HashSet<string> SkippedPlugins { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Semitone shift applied per string (high to low) by the global tuning tool, relative to how the song was opened.</summary>
    public int[] TuningShift { get; } = new int[6];

    // The tuning shift belongs to the song state it was set with: undo and redo restore it together with the strings.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ProjectState, int[]> _tuningByState = new();
    public void RememberTuningShift(ProjectState state) => _tuningByState.AddOrUpdate(state, (int[])TuningShift.Clone());
    public void RememberTuningShift(ProjectState state, int[] shift) => _tuningByState.AddOrUpdate(state, (int[])shift.Clone());
    public void RestoreTuningShift(ProjectState state)
    {
        if (_tuningByState.TryGetValue(state, out var shift)) Array.Copy(shift, TuningShift, TuningShift.Length);
    }

    // The skipped areas follow the bars of a structural edit (a section move, insert or delete) and belong to the song state they were set with:
    // undo and redo restore them together with the bars.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ProjectState, (int Start, int End)[]> _skipByState = new();
    public void RememberSkipRanges(ProjectState state, (int Start, int End)[] ranges) => _skipByState.AddOrUpdate(state, ((int Start, int End)[])ranges.Clone());
    /// <summary>Undo or redo is about to leave <paramref name="current"/> for <paramref name="target"/>: when the target carries skipped areas (an edit moved them), the current ones are kept for the way back.</summary>
    public void RememberSkipRangesForReturn(ProjectState current, ProjectState target)
    {
        if (_skipByState.TryGetValue(target, out _)) _skipByState.AddOrUpdate(current, SkipRanges.ToArray());
    }
    public void RestoreSkipRanges(ProjectState state)
    {
        if (!_skipByState.TryGetValue(state, out var ranges)) return;
        SkipRanges.Clear();
        SkipRanges.AddRange(ranges);
    }

    /// <summary>
    /// True only when persisted project content differs from its clean baseline. The legacy dirty
    /// flag remains a fast hint that a write path ran; comparing content filters no-op UI commits and
    /// edits that were subsequently undone back to the saved state.
    /// </summary>
    public bool IsDirty => Project.IsDirty && HasUnsavedChanges;

    /// <summary>Content-accurate close-prompt check, including changes missed by a legacy dirty flag.</summary>
    public bool HasUnsavedChanges
    {
        get
        {
            if (_cleanContentHash is null) return Project.IsDirty;
            // Clean since the last edit on the same instance: no hash needed on the UI thread.
            if (!Project.IsDirty && Project.EditVersion == _cleanVersion &&
                ReferenceEquals(_cachedProject, Project) && _cachedVersion == _cleanVersion &&
                _cachedHash is not null && CryptographicOperations.FixedTimeEquals(_cleanContentHash, _cachedHash))
                return false;
            return !CryptographicOperations.FixedTimeEquals(_cleanContentHash, CurrentHash());
        }
    }

    /// <summary>Marks the current project content as saved/loaded (<paramref name="contentHash"/>, when given, is that content's hash).</summary>
    public void MarkClean(byte[]? contentHash = null)
    {
        Project.IsDirty = false;
        // Reuse the save hash when the caller passes it (no second serialize); otherwise reuse the
        // cached hash when the version has not moved, hashing only on a version change.
        byte[] hash;
        if (contentHash is not null) hash = contentHash.ToArray();
        else if (_cachedHash is not null && ReferenceEquals(_cachedProject, Project) && _cachedVersion == Project.EditVersion) hash = (byte[])_cachedHash.Clone();
        else hash = CurrentHash().ToArray();
        _cleanContentHash = hash;
        _cleanVersion = Project.EditVersion;
        _cachedProject = Project;
        _cachedVersion = Project.EditVersion;
        _cachedHash = (byte[])hash.Clone();
        _cleanState = Undo.Snapshot(Project).State;
    }

    /// <summary>
    /// A save wrote <paramref name="capture"/> (the song as it was when the save started; <paramref name="contentHash"/> is its hash): that content is
    /// the clean baseline. Edits made while the file was written leave the song unsaved, and undoing back to the captured content makes it clean again.
    /// </summary>
    public void MarkCleanFromCapture(byte[] contentHash, SaveCapture capture)
    {
        var unchanged = capture.Matches(this);
        _cleanContentHash = contentHash.ToArray();
        _cleanState = capture.State;
        _cleanVersion = capture.EditVersion;
        Project.IsDirty = !unchanged;
        _cachedProject = unchanged ? Project : null;
        _cachedVersion = capture.EditVersion;
        _cachedHash = unchanged ? contentHash.ToArray() : null;
    }

    /// <summary>
    /// The file was written but some content could not be captured (plug-in states): keep the song marked unsaved so the user is asked
    /// again, even though the model matches what was written.
    /// </summary>
    public void MarkIncomplete()
    {
        Project.IsDirty = true;
        _cleanContentHash = null;
        _cleanState = null;
        _cleanVersion = Project.EditVersion;
    }

    /// <summary>Undo/redo restored <paramref name="snapshot"/>: playback keeps its bar mapping (nothing to keep without playback).</summary>
    public virtual void RememberBarMapping(UndoSnapshot snapshot) { }
    /// <summary>The timeline's song identity follows a replaced song (nothing to carry without a timeline).</summary>
    public virtual void CarrySongId(SongProject from, SongProject to) { }

    /// <summary>
    /// Undo/redo: true when the restored <paramref name="snapshot"/> is exactly the saved content (same answer as
    /// <see cref="HasUnsavedChanges"/> being false, without hashing the whole song).
    /// </summary>
    public bool IsCleanContent(UndoSnapshot snapshot) =>
        _cleanContentHash is not null && (_cleanState is { } clean ? clean.ContentEquals(snapshot.State) : !HasUnsavedChanges);

    // Streams the compact in-memory serialization straight into SHA-256 (no multi-MB JSON string per check).
    private byte[] ContentHash() => ProjectService.ContentHash(Project);

    /// <summary>
    /// The current content hash, reused while <see cref="TabForge.Models.SongProject.EditVersion"/> has not moved
    /// (redraws, tab titles and save outcomes share it; only the first check after an edit serializes).
    /// Never call <see cref="ContentHash"/> directly from UI code; use this.
    /// </summary>
    internal byte[] CurrentHash()
    {
        var project = Project;
        if (_cachedHash is not null && ReferenceEquals(_cachedProject, project) && _cachedVersion == project.EditVersion)
            return _cachedHash;
        var hash = ProjectService.ContentHash(project);
        _cachedProject = project;
        _cachedVersion = project.EditVersion;
        _cachedHash = hash;
        return hash;
    }

    /// <summary>
    /// Cheap UI-thread check for tab redraws: the legacy dirty flag only, without touching the content hash.
    /// Use <see cref="HasUnsavedChanges"/> only for close prompts and save outcomes (it reuses the cached hash).
    /// </summary>
    public bool IsDirtyHint => Project.IsDirty;

    /// <summary>The file an imported song (.gp5 ...) was read from while it has no save file of its own; its tab names that file until a save gives it a path.</summary>
    public string? ImportSourcePath { get; set; }

    /// <summary>The file the tab names: the save file, else the imported file, else null (an unsaved new song).</summary>
    public string? LabelPath => Path ?? ImportSourcePath;

    public string DisplayName => TabLabelPresenter.Name(LabelPath, Project.Title);

    /// <summary>How many empty bars a new file starts with.</summary>
    public const int NewSongBars = 4;

    /// <summary>The song a new file starts with: the blank template cut to <see cref="NewSongBars"/> bars.</summary>
    public static SongProject BlankProject()
    {
        var p = TemplateFactory.Blank();
        foreach (var t in p.Tracks) t.Measures = t.Measures.Take(NewSongBars).ToList();   // a new song starts short; Right at the end adds bars
        return p;
    }
}
