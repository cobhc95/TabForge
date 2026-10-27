using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: opening a score file for the Avalonia shell with the app's import protection: a Guitar Pro file is parsed in the import worker
// process (ImportWorker, as ScoreImportQueue does in the app), and a file whose worker cannot start is refused rather than parsed here.
// Does not own: the tabs, the dialogs and the messages (MainWindowFile), the parser (GuitarProImporter) or the worker (ImportWorker).
// Tests: TabForge.Avalonia.Tests.ScoreOpenFlowTests.
/// <summary>Opens score files for the Avalonia shell; Guitar Pro files go through the import worker, other files open in place.</summary>
public sealed class ScoreOpenFlow
{
    private readonly DocumentController _documents;
    private readonly ImportWorkerOptions _options;

    /// <param name="documents">The document controller that reads the files.</param>
    /// <param name="options">The worker's settings; null = the app's own (tests point the worker at a missing program).</param>
    public ScoreOpenFlow(DocumentController documents, ImportWorkerOptions? options = null)
    {
        _documents = documents;
        _options = options ?? ImportWorkerOptions.Default;
    }

    /// <summary>Opens <paramref name="path"/> off the calling thread. Throws when the file cannot be opened or its protected import cannot start.</summary>
    public Task<OpenedScore> OpenAsync(string path)
    {
        if (!GuitarProImporter.SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return Task.Run(() => _documents.Open(path));
        return Task.Run(() => OpenInWorker(path));
    }

    private OpenedScore OpenInWorker(string path)
    {
        var context = new ImportContext(new ImportGuard(CancellationToken.None));
        try
        {
            return _documents.Open(path, (file, notices) => ImportWorker.Import(file, _options, notices, context), context);
        }
        catch (ImportWorkerUnavailableException unavailable)
        {
            // The Avalonia shell has no per-file consent for an in-process parse yet, so the file is refused, never parsed here.
            throw new InvalidOperationException($"The protected import process could not start ({unavailable.Message}), so {Path.GetFileName(path)} was not opened.", unavailable);
        }
    }
}
