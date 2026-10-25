using System;
using System.IO;
using TabForge.AudioEngine;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the engine log's file for one run (EngineLogWriter.CreateForRun): a new file under the given name, never an existing one.
/// Does not own: the log's writer thread or its rotation (EngineLogWriter, full suite).
/// Tests: this class.
/// </summary>
public sealed class EngineLogRunFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-engine-log-" + Guid.NewGuid().ToString("N"));
    private readonly EngineLogWriter _log = new();

    public void Dispose()
    {
        _log.UseFileForTests(null);
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void RunLogIsCreatedNewAndAnExistingFileIsNeverReused()
    {
        var path = Path.Combine(_folder, "tabforge-audioengine-run.log");

        Assert.True(_log.CreateForRun(path));
        Assert.Equal(path, _log.LogPath);
        Assert.False(_log.CreateForRun(path));
    }
}
