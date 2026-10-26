using System.IO;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Documents;

/// <param name="Notice">Something the user should know about the opened file (e.g. its .tfaudio did not match); null when all is well.</param>
public readonly record struct OpenedScore(SongProject Project, string? SessionPath, bool ImportedFromGuitarPro, string? Notice = null, string? SourcePath = null);

// Owns: opening and creating scores into explicit documents (file read, import, session and project assembly).
// Does not own: writing files (DocumentSaveFlow) and the window's tabs.
// Tests: MainWindowDocumentsTests.
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
        if (FileTypes.IsTuxGuitarGtp(Path.GetExtension(path))) throw new InvalidDataException(GuitarProImporter.GtpNotSupportedMessage);
        path = FilePathPolicy.ExistingFile(path, "score file",
            FileTypes.OpenInApp);
        if (string.Equals(Path.GetExtension(path), FileTypes.Project, StringComparison.OrdinalIgnoreCase))
        {
            // A cut-short save of the .gp + .tforge pair is resolved first (the marker belongs to the .gp beside it), exactly as for the .gp.
            var projectRecovery = RecoverInterruptedPairFor(path);
            // Crash-recovery copies are held to the same project-file limit as any project (a copy over it is not written).
            var loaded = ProjectService.Load(path, InputLimits.MaxTforgeFileBytes);
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
    /// The song is captured on the calling (UI) thread and written on a worker, so editing can go on meanwhile.
    /// </summary>
    public async Task SaveCleanGuitarProWithAudioDataAsync(SongDocument document, string path)
    {
        var capture = SaveCapture.Take(document);
        await SaveCleanPairAsync(document, path, capture);
    }

    private static async Task SaveCleanPairAsync(SongDocument document, string path, SaveCapture capture)
    {
        path = FilePathPolicy.OutputFile(path, "score file", ".gp");
        var oldBase = document.Media.BaseDirectory;   // relative clip paths resolve against where the song was opened from; they are written relative to the new folder
        var hash = await RunOnWorker(capture, project => MediaStoredPaths.Write(project, oldBase, path, () =>
        {
            var gp = GuitarProExporter.ToBytes(project, embedProject: false);
            var audio = AudioDataFile.Serialize(project, TabForge.Audio.Contracts.PluginIdentity.Sha256Hex(gp));
            FilePathPolicy.WritePairAtomically(path, stream => stream.Write(gp),
                FilePathPolicy.OutputFile(AudioDataFile.PathFor(path), "TabForge audio data", AudioDataFile.Extension), stream => stream.Write(audio));
            return ProjectService.ContentHash(project);
        }));
        MarkSaved(document, path, hash!, capture);
    }

    /// <summary>
    /// The one capture-then-write step of every save: the song is captured on the calling (UI) thread, <paramref name="write"/> runs on a worker over a
    /// private rebuild of it and returns what it wrote (normally the content hash). The capture is returned so the caller can mark the song clean from it.
    /// </summary>
    private static async Task<(SaveCapture Capture, T Result)> WriteCapturedAsync<T>(SongDocument document, Func<SongProject, T> write)
    {
        var capture = SaveCapture.Take(document);
        return (capture, await RunOnWorker(capture, write));
    }

    private static Task<T> RunOnWorker<T>(SaveCapture capture, Func<SongProject, T> work) => Task.Run(() => work(capture.Rebuild()));

    /// <summary>The write succeeded: the song now lives at <paramref name="path"/> (null: it stays where it was) and the captured content is its clean baseline.</summary>
    private static void MarkSaved(SongDocument document, string? path, byte[] contentHash, SaveCapture capture)
    {
        if (path is not null) { document.Path = path; document.IsNew = false; }
        document.MarkCleanFromCapture(contentHash, capture);
    }

    /// <summary>The lossy-export preflight of the song as it is now: captured here, analysed on a worker (it reads every note and takes alphaTab's model lock, which a running export may hold).</summary>
    public async Task<GpPreflightReport> AnalyzeGuitarProExportAsync(SongDocument document) =>
        (await WriteCapturedAsync(document, GpExportPreflight.Analyze)).Result;

    /// <summary>What an export wrote and what the person should be told; <paramref name="Cancelled"/>: the person cancelled the question, nothing was written.</summary>
    public sealed record GuitarProExportResult(string? CompatiblePath, string? NativeCopyPath, string Notice, bool Cancelled = false);

    /// <summary>
    /// Writes a clean score-format export after the lossy-export preflight. The song is captured once on the calling (UI) thread; the analysis, the question
    /// and the write all use that capture. <paramref name="choose"/> is asked only when the analysis found something a clean .gp cannot hold, and answers
    /// with what the person picks.
    /// Native copy first, then the compatible file; nothing is overwritten that holds native content. The document changes only as the plan says: an Export
    /// never touches it, and a compatible-only Save leaves it unsaved so native content is never reported as preserved.
    /// </summary>
    public async Task<GuitarProExportResult> ExportCleanGuitarProAsync(SongDocument document, string path, GpExportKind kind, Func<GpPreflightReport, Task<GpExportChoice>> choose)
    {
        path = FilePathPolicy.OutputFile(path, "score file", ".gp");
        var capture = SaveCapture.Take(document);
        var report = await RunOnWorker(capture, GpExportPreflight.Analyze);
        // The question may open a dialog: it must continue on the UI context, so nothing on this path uses ConfigureAwait(false).
        var choice = report.ShouldAskFor(kind) ? await choose(report) : GpExportChoice.ExportCompatible;
        if (choice == GpExportChoice.Cancel) return new GuitarProExportResult(null, null, "", Cancelled: true);
        if (kind == GpExportKind.Save && !report.ShouldAskFor(kind))   // nothing is lost: the plain save of the clean pair, no plan needed
        {
            await SaveCleanPairAsync(document, path, capture);
            return new GuitarProExportResult(path, null, "");
        }
        var plan = GpExportPreflight.Plan(report, choice, kind, path, document.Path);
        if (!plan.Proceed) return new GuitarProExportResult(null, null, plan.Note);
        // A Save with the compatible choice: the clean pair of the song (.gp + .tfaudio), and the song is saved.
        if (kind == GpExportKind.Save && choice == GpExportChoice.ExportCompatible && plan.MarkDocumentClean && plan.NativeCopyPath is null)
        {
            await SaveCleanPairAsync(document, plan.CompatiblePath, capture);
            return new GuitarProExportResult(plan.CompatiblePath, null, plan.Note);
        }
        var compatiblePath = FilePathPolicy.OutputFile(plan.CompatiblePath, "score file", ".gp");
        var oldBase = document.Media.BaseDirectory;
        var hash = await RunOnWorker(capture, project =>
        {
            if (plan.NativeCopyPath is not { } native)
            {
                GuitarProExporter.Save(project, compatiblePath, embedProject: false);
                return null;
            }
            // The full copy and the .gp are one pair: both are staged before either is replaced, and an interrupted write is resolved on the next open.
            var nativeTarget = FilePathPolicy.OutputFile(native, "TabForge project", ".tforge");
            var gp = GuitarProExporter.ToBytes(project, embedProject: false);
            _ = FilePathPolicy.RecoverInterruptedPair(compatiblePath);   // an earlier interrupted pair of this .gp (whatever its partner) is resolved first; the write below refuses if it cannot be
            return MediaStoredPaths.Write(project, oldBase, nativeTarget, () =>
            {
                FilePathPolicy.WritePairAtomically(compatiblePath, stream => stream.Write(gp), nativeTarget, stream => ProjectService.WriteTo(stream, project));
                return ProjectService.ContentHash(project);
            });
        });
        if (plan.MarkDocumentClean && plan.NativeCopyPath is { } nativePath && hash is not null)
            MarkSaved(document, plan.ChangeDocumentPath ? nativePath : null, hash, capture);
        return new GuitarProExportResult(plan.CompatiblePath, plan.NativeCopyPath, plan.Note);
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
        // Any thread: the claim is one atomic compare-and-set, so two callers can never both win it.
        var hold = new SaveHold(this);
        if (Interlocked.CompareExchange(ref _hold, hold, null) is not null) return null;
        RaiseSavingChanged();
        return hold;
    }

    /// <summary>Completes when no save or export holds the claim any more (at once when none does): a closing window or tab waits on it before it asks about unsaved songs.</summary>
    public Task WhenIdle()
    {
        lock (_idleGate) return _hold is null ? Task.CompletedTask : (_idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    }

    // A handler that throws must not leave the claim taken or skip the release: the claim is already set (or cleared) when this runs.
    private void RaiseSavingChanged()
    {
        try { SavingChanged?.Invoke(); }
        catch (Exception ex) { Services.Trace.Error(Services.Trace.Ui, "saving state handler: " + ex.Message); }
    }

    private readonly object _idleGate = new();
    private TaskCompletionSource? _idle;

    private void Release(SaveHold hold)
    {
        if (Interlocked.CompareExchange(ref _hold, null, hold) != hold) return;
        TaskCompletionSource? idle;
        lock (_idleGate) { idle = _idle; _idle = null; }
        idle?.TrySetResult();
        RaiseSavingChanged();
    }

    /// <summary>
    /// The one save sequence: collect plug-in states asynchronously (the caller thread is never blocked), and only then run
    /// <paramref name="write"/> so the file is a snapshot of the model after the states arrived (the write captures the song on the calling thread and
    /// writes on a worker). Throws when <paramref name="hold"/> is no longer the running claim.
    /// </summary>
    /// <param name="hold">The claim the caller took with <see cref="TryBeginSave"/> before its first question; the caller releases it.</param>
    public async Task<StateCollection> SaveAsync(Func<Task<StateCollection>> collectStates, Func<StateCollection, Task> write, SaveHold hold)
    {
        if (!ReferenceEquals(hold, _hold)) throw new InvalidOperationException("The save claim is no longer held; nothing was written.");
        var capture = await collectStates();
        await write(capture);
        return capture;
    }

    /// <summary>Saves the song to <paramref name="path"/> (.gp with the project embedded, or .tforge): captured here, written on a worker, then the path and the clean baseline are set.</summary>
    public async Task SaveToFileAsync(SongDocument document, string path)
    {
        var asGuitarPro = path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase);
        path = asGuitarPro ? FilePathPolicy.OutputFile(path, "score file", ".gp") : FilePathPolicy.OutputFile(path, "TabForge project", ".tforge");
        var oldBase = document.Media.BaseDirectory;   // see SaveCleanPairAsync
        var (capture, contentHash) = await WriteCapturedAsync(document, project => MediaStoredPaths.Write(project, oldBase, path, () =>
        {
            // .gp: Guitar Pro 7/8 file with the full TabForge project embedded (see GuitarProExporter).
            if (asGuitarPro) GuitarProExporter.Save(project, path);
            return asGuitarPro ? ProjectService.ContentHash(project) : ProjectService.Save(path, project);
        }));
        MarkSaved(document, path, contentHash!, capture);
    }
}
