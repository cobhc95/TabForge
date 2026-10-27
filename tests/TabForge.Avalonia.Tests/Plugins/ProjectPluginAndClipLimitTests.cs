using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the project checks for MIDI clip notes (their own limit, keyed by the clip) and plug-in paths (full paths only), and the
/// plug-in list the engine is sent (normalised paths only).
/// Does not own: the file format (ProjectService) or the trust decision (PluginTrust).
/// Tests: this class.
/// </summary>
public sealed class ProjectPluginAndClipLimitTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-project-limits-" + Guid.NewGuid().ToString("N"));

    public ProjectPluginAndClipLimitTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static SongProject ProjectWithOneTrack()
    {
        var project = new SongProject { Title = "limits" };
        project.Tracks.Add(new TrackModel { Name = "Synth", Measures = TemplateFactory.Measures(1) });
        return project;
    }

    [Fact]
    public void MidiClipWithThreeHundredNotesSavesAndReopens()
    {
        var project = ProjectWithOneTrack();
        var notes = Enumerable.Range(0, 300).Select(i => new ClipNote(i * 0.1, 0.1, 60 + i % 12, 90)).ToList();
        project.Tracks[0].AudioClips.Add(new AudioClip { Name = "midi", SourceLengthSec = 40, FileLengthSec = 40, Notes = notes });
        var path = Path.Combine(_folder, "midi.tforge");

        ProjectService.Save(path, project);
        var reopened = ProjectService.Load(path);

        Assert.Equal(300, reopened.Tracks[0].AudioClips[0].Notes!.Count);
    }

    [Fact]
    public void PluginPathWithLineBreakIsRefusedByTheProjectCheck()
    {
        var project = ProjectWithOneTrack();
        project.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "Odd", Path = "C:\\plugins\\a.vst3\r\nB", Format = "VST3" });

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(project));
    }

    [Fact]
    public void RelativePluginPathIsRefusedByTheProjectCheck()
    {
        var project = ProjectWithOneTrack();
        project.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "Relative", Path = @"plugins\relative.dll", Format = "VST3" });

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(project));
    }

    [Fact]
    public void EngineIsSentOnlyTheNormalisedPluginPath()
    {
        var slot = new PluginSlot { Name = "Plain", Path = @"C:\Plugins\plain.dll\", Format = "VST3" };

        var specs = new PluginTrust().BuildSpecs(new[] { slot }, new List<string>(), new PluginSettings(), new NoFileTrust());

        Assert.Equal(@"C:\Plugins\plain.dll", specs.Single().Path);
    }

    /// <summary>A file-trust adapter that knows no final paths: every plug-in is untrusted, which keeps the test independent of the disk.</summary>
    private sealed class NoFileTrust : TabForge.Platform.IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => null;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }
}
