using System.IO;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Documents;

/// <param name="Notice">Something the user should know about the opened file (e.g. its .tfaudio did not match); null when all is well.</param>
public readonly record struct OpenedScore(SongProject Project, string? SessionPath, bool ImportedFromGuitarPro, string? Notice = null, string? SourcePath = null);

/// <summary>File-level document operations kept separate from window prompts and tab presentation.</summary>
public sealed class DocumentController
{
    /// <summary>The folder whose autosave-named files are read with the recovery size bound (tests point it elsewhere).</summary>
    public string RecoveryFolder { get; init; } = AutosaveService.DefaultFolder;

    public OpenedScore Open(string path) => Open(path, null);

    /// <param name="importGuitarPro">Parses a Guitar Pro file (null = <see cref="GuitarProImporter.Import"/> in this process); it may add
    /// notices. The background import passes the out-of-process worker (A5-07); the pair recovery and .tfaudio stay in this process.</param>
    public OpenedScore Open(string path, Func<string, List<string>, SongProject>? importGuitarPro)
    {
        path = FilePathPolicy.ExistingFile(path, "score file",
            FileTypes.AllOpenable);
        if (string.Equals(Path.GetExtension(path), FileTypes.Project, StringComparison.OrdinalIgnoreCase))
        {
            // Only the app's own crash-recovery copies (written with the larger bound) may exceed the normal .tforge limit.
            var limit = AutosaveService.IsRecoveryCopy(path, RecoveryFolder) ? InputLimits.MaxRecoveryProjectBytes : InputLimits.MaxTforgeFileBytes;
            return new OpenedScore(ProjectService.Load(path, limit), path, false);
        }

        var notices = new List<string>();
        // A save of the .gp + .tfaudio pair that was cut short is undone first, so the pair read below is consistent.
        if (path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase) && FilePathPolicy.RecoverInterruptedPair(path, AudioDataFile.PathFor(path)) is { } recovery) notices.Add(recovery);
        SongProject project;
        if (importGuitarPro is null)
        {
            project = GuitarProImporter.Import(path);
            // A6-02: an embedded TabForge project that was present but unusable is reported (the worker path adds it through its own notices).
            if (GuitarProImporter.LastEmbeddedRejection is { } rejected) notices.Add(rejected);
            if (GuitarProImporter.LastDamageNotice is { } damaged) notices.Add(damaged);
        }
        else project = importGuitarPro(path, notices);
        // A5-04: the song's own title wins (also for a TabForge-embedded project); the file name only fills an empty one.
        if (string.IsNullOrWhiteSpace(project.Title)) project.Title = Path.GetFileNameWithoutExtension(path);
        // A clean .gp saved with its TabForge audio data beside it ("song.tfaudio"): bring the mixer and FX back.
        // R5: each mode reads only its own data. TabForge never writes a sidecar with an embedded .gp, so a .tfaudio beside one is left over from an
        // earlier clean save of that name: it must not replace the embedded project's (newer) mixer and FX.
        if (path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase) && File.Exists(AudioDataFile.PathFor(path)) && GuitarProExporter.HasEmbeddedEntry(path))
            notices.Add($"{Path.GetFileName(AudioDataFile.PathFor(path))} was not applied: {Path.GetFileName(path)} holds its own TabForge project, and the .tfaudio is from an earlier save");
        else if (path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase)) AudioDataFile.TryApply(project, path, notices);
        return new OpenedScore(project, null, true, notices.Count == 0 ? null : string.Join("; ", notices), path);   // no native save path yet: relative media resolves against the imported file's folder
    }

    /// <summary>
    /// Saves a clean Guitar Pro file (nothing TabForge-specific inside, so Guitar Pro reads it exactly as its own)
    /// plus "song.tfaudio" beside it with the mixer and FX chains, which TabForge re-applies on open.
    /// Both files are staged completely before either is replaced, and the .tfaudio records the SHA-256 of the .gp it belongs to.
    /// </summary>
    public void SaveCleanGuitarProWithAudioData(DocumentSession document, string path, string? lyrics)
    {
        path = FilePathPolicy.OutputFile(path, "Guitar Pro file", ".gp");
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
    /// Writes a clean Guitar Pro file after the lossy-export preflight (R5). <paramref name="choice"/> is what the person picked when
    /// <see cref="GpExportPreflight.Analyze"/> found something a clean .gp cannot hold (pass <see cref="GpExportChoice.ExportCompatible"/> when it found nothing).
    /// Native copy first, then the compatible file; nothing is overwritten that holds native content. The document changes only as the plan says: an Export
    /// never touches it, and a compatible-only Save leaves it unsaved so native content is never reported as preserved.
    /// </summary>
    public GuitarProExportResult ExportCleanGuitarPro(DocumentSession document, string path, GpExportKind kind, GpExportChoice choice, string? lyrics)
    {
        path = FilePathPolicy.OutputFile(path, "Guitar Pro file", ".gp");
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
            var wasDirty = document.Project.IsDirty;
            if (plan.NativeCopyPath is { } native) hash = ProjectService.Save(FilePathPolicy.OutputFile(native, "TabForge project", ".tforge"), document.Project);
            // ProjectService.Save clears the flag; until every file is written the document's own unsaved state stays (a failed .gp write must not leave it "clean").
            document.Project.IsDirty = wasDirty;
            GuitarProExporter.Save(document.Project, FilePathPolicy.OutputFile(plan.CompatiblePath, "Guitar Pro file", ".gp"), embedProject: false);
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
        var choice = report.ShouldAsk ? ask(report) : GpExportChoice.ExportCompatible;
        return ExportCleanGuitarPro(document, path, kind, choice, lyrics);
    }

    /// <summary>True while a save is collecting plug-in states or writing; a second save or exit-save must wait.</summary>
    public bool IsSaving { get; private set; }
    public event Action? SavingChanged;

    /// <summary>
    /// The one save sequence: collect plug-in states asynchronously (the caller's thread is never blocked), and only then run
    /// <paramref name="write"/> so the file is a snapshot of the model after the states arrived. Returns null (nothing done)
    /// when a save is already running.
    /// </summary>
    public async Task<StateCollection?> SaveAsync(Func<Task<StateCollection>> collectStates, Action<StateCollection> write)
    {
        if (IsSaving) return null;
        IsSaving = true;
        SavingChanged?.Invoke();
        try
        {
            var capture = await collectStates();
            write(capture);
            return capture;
        }
        finally { IsSaving = false; SavingChanged?.Invoke(); }
    }

    public void Save(DocumentSession document, string path, string? lyrics)
    {
        var asGuitarPro = path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase);
        path = asGuitarPro ? FilePathPolicy.OutputFile(path, "Guitar Pro file", ".gp") : FilePathPolicy.OutputFile(path, "TabForge project", ".tforge");
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
