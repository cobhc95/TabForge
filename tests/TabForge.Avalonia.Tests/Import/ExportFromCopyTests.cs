using System;
using System.IO;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: File > Export writes from a copy of the song (so the write can run on a worker): the MIDI, ASCII and MusicXML files written from the
///     copy are byte for byte the ones written from the live song.
/// Does not own: the exporters themselves or the dialogs (MainWindowDocumentsTests).
/// Tests: this class.
/// </summary>
public sealed class ExportFromCopyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-export-copy-" + Guid.NewGuid().ToString("N"));

    public ExportFromCopyTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("mid")]
    [InlineData("txt")]
    [InlineData("musicxml")]
    public void TheCopyExportsTheSameBytesAsTheLiveSong(string extension)
    {
        Action<SongProject, string> export = extension switch
        {
            "mid" => MidiExportService.Export,
            "txt" => AsciiExportService.Export,
            _ => MusicXmlExportService.Export,
        };
        var song = FullDemoSongFactory.Create();
        var copy = SongSnapshot.Take(song, new ProjectStateEncoder(ProjectStateEncoder.PlaybackParallelEncodeBars)).Song;
        var live = Path.Combine(_folder, "live." + extension);
        var copied = Path.Combine(_folder, "copy." + extension);
        export(song, live);
        export(copy, copied);
        Assert.Equal(File.ReadAllBytes(live), File.ReadAllBytes(copied));
    }
}
