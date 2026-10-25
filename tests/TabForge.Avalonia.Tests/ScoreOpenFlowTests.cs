using System;
using System.IO;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Avalonia shell's Guitar Pro open (ScoreOpenFlow): a file whose import worker cannot start is refused, never parsed in this process.
/// Does not own: the worker itself (ImportWorker, full suite) or the file dialogs (MainWindowFile).
/// Tests: this class.
/// </summary>
public sealed class ScoreOpenFlowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-scoreopen-" + Guid.NewGuid().ToString("N"));

    public ScoreOpenFlowTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task GuitarProFileIsRefusedWhenItsImportWorkerCannotStart()
    {
        var score = Path.Combine(_folder, "song.gp");
        File.WriteAllText(score, "not a Guitar Pro file");   // any in-process parse would fail with a different error
        var flow = new ScoreOpenFlow(new DocumentController(), new ImportWorkerOptions { ExecutablePath = Path.Combine(_folder, "missing-worker.exe") });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => flow.OpenAsync(score));

        Assert.Contains("could not start", error.Message);
    }
}
