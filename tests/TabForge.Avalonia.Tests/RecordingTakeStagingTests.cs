using System;
using System.Collections.Generic;
using System.IO;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Audio;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the audio take staging proof: a take is written to a hidden ".tftake" file and only appears at its final
/// ".wav" name when the take is closed, so a failure never leaves a half WAV at the take's name.
/// </summary>
public sealed class RecordingTakeStagingTests
{
    [Fact]
    public void FinishedTakeLandsAtItsFinalNameWithNoStagingLeftBehind()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-take-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var tracks = new List<(int Slot, string TrackName, int Mode)> { (0, "Guitar", 0) };
            using var recorder = new Recorder(folder, tracks, 0, 44100, new EngineLogWriter(), startWriter: false, queueSeconds: 4);
            // The final name is only reserved while recording: every file on disk is still staging.
            Assert.All(Directory.GetFiles(folder), f => Assert.Contains(".tmp.wav", f, StringComparison.Ordinal));
            var block = new float[4410 * 2];
            recorder.Write(block, 4410);
            recorder.PumpForTests();
            var (take, length) = Assert.Single(recorder.Finish());

            Assert.EndsWith(".wav", take.Path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".tmp", take.Path, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(take.Path), "the take was committed to its final name");
            Assert.Equal(0.1, length, 3);
            Assert.Empty(Directory.GetFiles(folder, "*.tmp.wav"));
            Assert.True(new FileInfo(take.Path).Length > 44, "the committed file holds a WAV header and samples");
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }
}
