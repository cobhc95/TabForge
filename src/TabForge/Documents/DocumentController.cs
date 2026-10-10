using System.IO;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Documents;

/// <param name="Notice">Something the user should know about the opened file (e.g. its .tfaudio did not match); null when all is well.</param>
public readonly record struct OpenedScore(SongProject Project, string? SessionPath, bool ImportedFromGuitarPro, string? Notice = null, string? SourcePath = null);

// Owns: opening and creating scores into explicit documents (file read, import, session and project assembly).
// Does not own: writing files (DocumentSaveFlow) and the window's tabs.
// Tests: TestDocuments, TestArchitectureLayering.
/// <summary>File-level document operations kept separate from window prompts and tab presentation.</summary>
public sealed class DocumentController
{
    /// <summary>The folder whose autosave-named files are read with the recovery size bound (tests point it elsewhere).</summary>
    public string RecoveryFolder { get; init; } = AutosaveService.DefaultFolder;

    public OpenedScore Open(string path) => Open(path, null);

    /// <param name="importGuitarPro">Parses a Guitar Pro file (null = <see cref="GuitarProImporter.Import"/> in this process); it may add
    /// notices. The background import passes the out-of-process worker; the pair recovery and .tfaudio stay in this process.</param>
    /// <param name="context">The import's limits and reported notices (the in-process parse uses it; null = a fresh one without limits).</param>
    public OpenedScore Open(string path, Func<string, List<string>, SongProject>? importGuitarPro, ImportContext? context = null)
    {
        path = FilePathPolicy.ExistingFile(path, "score file",
            FileTypes.AllOpenable);
        if (string.Equals(Path.GetExtension(path), FileTypes.Project, StringComparison.OrdinalIgnoreCase))
        {
            // Only the app's own crash-recovery copies (written with the larger bound) may exceed the normal .tforge limit.
            var limit = AutosaveService.IsRecoveryCopy(path, RecoveryFolder) ? InputLimits.MaxRecoveryProjectBytes : InputLimits.MaxTforgeFileBytes;
            // A cut-short save of the .gp + .tforge pair is resolved first (the marker belongs to the .gp beside it), exactly as for the .gp.
            var projectRecovery = RecoverInterruptedPairFor(path);
            var loaded = ProjectService.Load(path, limit);
            return new OpenedScore(loaded, path, false, Join(projectRecovery, CoverClips(loaded)));
        }

        var notices = new List<string>();
        // A save of the .gp + .tfaudio pair that was cut short is undone first, so the pair read below is consistent.
        if (RecoverInterruptedPairFor(path) is { } recovery) notices.Add(recovery);
        SongProject project;
        if (importGuitarPro is null)
        {
            context ??= new ImportContext();
            project = GuitarProImporter.Import(path, context);
            // An embedded TabForge project that is present but unusable is reported (the worker path adds it through its own notices).
            context.AddNoticesTo(notices);
        }
        else project = importGuitarPro(path, notices);
        // The song's own title wins (also for a TabForge-embedded project); the file name only fills an empty one.
        if (string.IsNullOrWhiteSpace(project.Title)) project.Title = Path.GetFileNameWithoutExtension(path);
        // A clean .gp saved with its TabForge audio data beside it ("song.tfaudio"): bring the mixer and FX back.
        // Each mode reads only its own data. TabForge never writes a sidecar with an embedded score file, so a .tfaudio beside one is left over from an
        // earlier clean save of that name: it must not replace the embedded project's (newer) mixer and FX.
        if (path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase) && File.Exists(AudioDataFile.PathFor(path)) && GuitarProExporter.HasEmbeddedEntry(path))
            notices.Add($"{Path.GetFileName(AudioDataFile.PathFor(path))} was not applied: {Path.GetFileName(path)} holds its own TabForge project, and the .tfaudio is from an earlier save");
        else if (path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase)) AudioDataFile.TryApply(project, path, notices);
        if (CoverClips(project) is { } extended) notices.Add(extended);
        return new OpenedScore(project, null, true, notices.Count == 0 ? null : string.Join("; ", notices), path);   // no native save path yet: relative media resolves against the imported file's folder
    }

    private static string? Join(string? a, string? b) => a is null ? b : b is null ? a : a + "; " + b;

    /// <summary>
    /// A clip past the last bar always extends the song, also in a file saved before that rule: the bars are added to the loaded model
    /// before the document exists, so the opened song is not marked changed; the notice tells the user. Null when nothing was added.
    /// </summary>
    private static string? CoverClips(SongProject project)
    {
        var added = SongExtent.EnsureCoversClips(project).BarsAdded;
        return added == 0 ? null : $"{added} bar{(added == 1 ? "" : "s")} added at the end so the song covers its audio clips";
    }

    /// <summary>
    /// The one place every open path (menu, recent files, drag-drop, command line, file association) resolves an interrupted pair save:
    /// a .gp is checked directly; a .tforge is checked through the same-named .gp whose marker may name it as the partner.
    /// Returns the notice for the user, or null when nothing was pending.
    /// </summary>
    internal static string? RecoverInterruptedPairFor(string path)
    {
        try
        {
            if (path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase)) return FilePathPolicy.RecoverInterruptedPair(path);
            if (path.EndsWith(FileTypes.Project, StringComparison.OrdinalIgnoreCase)) return FilePathPolicy.RecoverInterruptedPair(Path.ChangeExtension(path, ".gp"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { Services.Trace.Error(Services.Trace.Import, "document open: recover interrupted save: " + ex.Message); }
        return null;
    }

    /// <summary>
    /// Saves a clean Guitar Pro file (nothing TabForge-specific inside, so Guitar Pro reads it exactly as its own)
    /// plus "song.tfaudio" beside it with the mixer and FX chains, which TabForge re-applies on open.
    /// Both files are staged completely before either is replaced, and the .tfaudio records the SHA-256 of the .gp it belongs to.
    /// </summary>
    public void SaveCleanGuitarProWithAudioData(DocumentSession document, string path, string? lyrics)
    {
        path = FilePathPolicy.OutputFile(path, "score file", ".gp");
        var originalLyrics = document.Project.Lyrics;
        document.Project.Lyrics = lyrics ?? "";
        try
        {
            var gp = GuitarProExporter.ToBytes(document.Project, embedProject: false);
            var audio = AudioDataFile.Serialize(document.Project, AudioDataFile.Sha256Hex(gp));
            FilePathPolicy.WritePairAtomically(path, stream => stream.Write(gp),
                FilePathPolicy.OutputFile(AudioDataFile.PathFor(path), "TabForge audio data", AudioDataFile.Extension), stream => stream.Write(audio));
            document.Path = path;
            document.IsNew = false;
            document.MarkClean(ProjectService.ContentHash(document.Project));
        }
        catch
        {
            document.Project.Lyrics = originalLyrics;
            throw;
        }
    }

    /// <summary>What an export wrote and what the person should be told.</summary>
    public sealed record GuitarProExportResult(string? CompatiblePath, string? NativeCopyPath, string Notice);

    /// <summary>
    /// Writes a clean score-format export after the lossy-export preflight. <paramref name="choice"/> is what the person picked when
    /// <see cref="GpExportPreflight.Analyze"/> found something a clean .gp cannot hold (pass <see cref="GpExportChoice.ExportCompatible"/> when it found nothing).
    /// Native copy first, then the compatible file; nothing is overwritten that holds native content. The document changes only as the plan says: an Export
    /// never touches it, and a compatible-only Save leaves it unsaved so native content is never reported as preserved.
    /// </summary>
    public GuitarProExportResult ExportCleanGuitarPro(DocumentSession document, string path, GpExportKind kind, GpExportChoice choice, string? lyrics)
    {
        path = FilePathPolicy.OutputFile(path, "score file", ".gp");
        var report = GpExportPreflight.Analyze(document.Project);
        var plan = GpExportPreflight.Plan(report, choice, kind, path, document.Path);
        if (!plan.Proceed) return new GuitarProExportResult(null, null, plan.Note);
        // A Save with the compatible choice: the song's own clean pair (.gp + .tfaudio) and the song is saved.
        if (kind == GpExportKind.Save && choice == GpExportChoice.ExportCompatible && plan.MarkDocumentClean && plan.NativeCopyPath is null)
        {
            SaveCleanGuitarProWithAudioData(document, plan.CompatiblePath, lyrics);
            return new GuitarProExportResult(plan.CompatiblePath, null, plan.Note);
        }
        var originalLyrics = document.Project.Lyrics;
        document.Project.Lyrics = lyrics ?? "";
        var followedNativeCopy = false;
        try
        {
            byte[]? hash = null;
            var compatiblePath = FilePathPolicy.OutputFile(plan.CompatiblePath, "score file", ".gp");
            if (plan.NativeCopyPath is { } native)
            {
                // The full copy and the .gp are one pair: both are staged before either is replaced, and an interrupted write is resolved on the next open.
                var nativeTarget = FilePathPolicy.OutputFile(native, "TabForge project", ".tforge");
                var gp = GuitarProExporter.ToBytes(document.Project, embedProject: false);
                _ = FilePathPolicy.RecoverInterruptedPair(compatiblePath);   // an earlier interrupted pair of this .gp (whatever its partner) is resolved first; the write below refuses if it cannot be
                FilePathPolicy.WritePairAtomically(compatiblePath, stream => stream.Write(gp), nativeTarget, stream => ProjectService.WriteTo(stream, document.Project));
                hash = ProjectService.ContentHash(document.Project);
            }
            else GuitarProExporter.Save(document.Project, compatiblePath, embedProject: false);
            if (plan.MarkDocumentClean && plan.NativeCopyPath is { } nativePath && hash is not null)
            {
                if (plan.ChangeDocumentPath) { document.Path = nativePath; document.IsNew = false; }
                document.MarkClean(hash);
                followedNativeCopy = true;
            }
            return new GuitarProExportResult(plan.CompatiblePath, plan.NativeCopyPath, plan.Note);
        }
        finally { if (!followedNativeCopy) document.Project.Lyrics = originalLyrics; }   // the saved lyrics stay when the document now is the native copy (as a normal save)
    }

    /// <summary>
    /// The preflight flow: analyses the song, asks <paramref name="ask"/> only when something would be lost (never for a harmless save), then writes as
    /// <see cref="ExportCleanGuitarPro(DocumentSession, string, GpExportKind, GpExportChoice, string?)"/>. A Cancel from <paramref name="ask"/> writes nothing.
    /// </summary>
    public GuitarProExportResult ExportCleanGuitarPro(DocumentSession document, string path, GpExportKind kind, string? lyrics, Func<GpPreflightReport, GpExportChoice> ask)
    {
        var report = GpExportPreflight.Analyze(document.Project);
        var choice = report.ShouldAskFor(kind) ? ask(report) : GpExportChoice.ExportCompatible;
        return ExportCleanGuitarPro(document, path, kind, choice, lyrics);
    }

    /// <summary>The claim a running save or export holds from its first question (the file dialog included) until it is finished; dispose it to release.</summary>
    public sealed class SaveHold : IDisposable
    {
        private readonly DocumentController _owner;
        internal SaveHold(DocumentController owner) => _owner = owner;
        public void Dispose() => _owner.Release(this);
    }

    private SaveHold? _hold;

    /// <summary>True from the first dialog of a save or export until it is finished (collecting plug-in states and writing included): a second save, an exit-save or an import placing a song must wait or open beside.</summary>
    public bool IsSaving => _hold is not null;
    public event Action? SavingChanged;

    /// <summary>Claims the save for the caller (null when one is already running). Everything that follows runs under the claim, so it covers the dialogs before the write.</summary>
    public SaveHold? TryBeginSave()
    {
        if (_hold is not null) return null;
        _hold = new SaveHold(this);
        SavingChanged?.Invoke();
        return _hold;
    }

    private void Release(SaveHold hold)
    {
        if (!ReferenceEquals(_hold, hold)) return;
        _hold = null;
        SavingChanged?.Invoke();
    }

    /// <summary>
    /// The one save sequence: collect plug-in states asynchronously (the caller's thread is never blocked), and only then run
    /// <paramref name="write"/> so the file is a snapshot of the model after the states arrived. Returns null (nothing done)
    /// when a save is already running and <paramref name="held"/> is not its claim.
    /// </summary>
    /// <param name="held">The claim the caller already took with <see cref="TryBeginSave"/>; null takes (and releases) one for this call.</param>
    public async Task<StateCollection?> SaveAsync(Func<Task<StateCollection>> collectStates, Action<StateCollection> write, SaveHold? held = null)
    {
        var hold = held ?? TryBeginSave();
        if (hold is null || !ReferenceEquals(hold, _hold)) return null;
        try
        {
            var capture = await collectStates();
            write(capture);
            return capture;
        }
        finally { if (held is null) hold.Dispose(); }
    }

    public void Save(DocumentSession document, string path, string? lyrics)
    {
        var asGuitarPro = path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase);
        path = asGuitarPro ? FilePathPolicy.OutputFile(path, "score file", ".gp") : FilePathPolicy.OutputFile(path, "TabForge project", ".tforge");
        var originalLyrics = document.Project.Lyrics;
        document.Project.Lyrics = lyrics ?? "";
        try
        {
            // .gp: Guitar Pro 7/8 file with the full TabForge project embedded (see GuitarProExporter).
            if (asGuitarPro) GuitarProExporter.Save(document.Project, path);
            var contentHash = asGuitarPro ? ProjectService.ContentHash(document.Project) : ProjectService.Save(path, document.Project);
            document.Path = path;
            document.IsNew = false;
            document.MarkClean(contentHash);
        }
        catch
        {
            document.Project.Lyrics = originalLyrics;
            throw;
        }
    }
}
