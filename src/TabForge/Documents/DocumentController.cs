using System.IO;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Documents;

/// <param name="Notice">Something the user should know about the opened file (e.g. its .tfaudio did not match); null when all is well.</param>
public readonly record struct OpenedScore(SongProject Project, string? SessionPath, bool ImportedFromGuitarPro, string? Notice = null);

/// <summary>File-level document operations kept separate from window prompts and tab presentation.</summary>
public sealed class DocumentController
{
    public OpenedScore Open(string path) => Open(path, null);

    /// <param name="importGuitarPro">Parses a Guitar Pro file (null = <see cref="GuitarProImporter.Import"/> in this process); it may add
    /// notices. The background import passes the out-of-process worker (A5-07); the pair recovery and .tfaudio stay in this process.</param>
    public OpenedScore Open(string path, Func<string, List<string>, SongProject>? importGuitarPro)
    {
        path = FilePathPolicy.ExistingFile(path, "score file",
            FileTypes.AllOpenable);
        if (string.Equals(Path.GetExtension(path), FileTypes.Project, StringComparison.OrdinalIgnoreCase))
            return new OpenedScore(ProjectService.Load(path), path, false);

        var notices = new List<string>();
        // A save of the .gp + .tfaudio pair that was cut short is undone first, so the pair read below is consistent.
        if (path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase) && FilePathPolicy.RecoverInterruptedPair(path, AudioDataFile.PathFor(path)) is { } recovery) notices.Add(recovery);
        var project = importGuitarPro is null ? GuitarProImporter.Import(path) : importGuitarPro(path, notices);
        // A5-04: the song's own title wins (also for a TabForge-embedded project); the file name only fills an empty one.
        if (string.IsNullOrWhiteSpace(project.Title)) project.Title = Path.GetFileNameWithoutExtension(path);
        // A clean .gp saved with its TabForge audio data beside it ("song.tfaudio"): bring the mixer and FX back.
        if (path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase)) AudioDataFile.TryApply(project, path, notices);
        return new OpenedScore(project, null, true, notices.Count == 0 ? null : string.Join("; ", notices));
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
