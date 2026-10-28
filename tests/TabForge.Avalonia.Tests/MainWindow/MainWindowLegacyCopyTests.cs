using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the copies for older programs (.gp5 / .gp4 / .gp3) written by File > Export and by Save As (MainWindowSaving, DocumentSaveFlow, LegacyGpPreflightPresenter).</summary>
public sealed class MainWindowLegacyCopyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-legacy-{Guid.NewGuid():N}");

    public MainWindowLegacyCopyTests() => Directory.CreateDirectory(_folder);

    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    private sealed class SaveTo : IFileDialogs
    {
        public SaveTo(string? path) => Path = path;
        public string? Path { get; }
        public List<FileDialogOptions> Asked { get; } = new();
        public string? SaveFile(FileDialogOptions options, object? owner) { Asked.Add(options); return Path; }
        public string? OpenFile(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no open dialog expected");
        public string[]? OpenFiles(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no open dialog expected");
        public string? PickFolder(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no folder dialog expected");
        public string[]? PickFolders(FileDialogOptions options, object? owner) => throw new InvalidOperationException("no folder dialog expected");
    }

    /// <summary>One guitar track with a note in each voice of bar 1: a second voice is something .gp4 and .gp3 cannot hold.</summary>
    private static SongDocument TwoVoiceSong()
    {
        var project = new SongProject { Title = "Two voices" };
        project.Tracks.Add(new TrackModel { Name = "Lead", Measures = TemplateFactory.Measures(2) });
        var document = new SongDocument(() => null) { Project = project };
        document.MarkClean();
        DocumentEdits.Run(document, p =>
        {
            var bar = p.Tracks[0].Measures[0];
            bar.Cells[0].DurationDenominator = 4; bar.Cells[0].Notes.Add(new TabNote { StringIndex = 1, Fret = 5, MidiValue = p.Tracks[0].StringTunings[1] + 5 });
            var voice2 = bar.CellsForVoice(1, create: true);
            voice2[0].DurationDenominator = 4; voice2[0].Notes.Add(new TabNote { StringIndex = 4, Fret = 3, MidiValue = p.Tracks[0].StringTunings[4] + 3 });
            return true;
        });
        return document;
    }

    private static MainWindowSaving Saving(IFileDialogs dialogs, List<string> reports, Func<GpPreflightReport, string, LegacyGpFormat, Task<bool>>? ask) =>
        new(dialogs, null, reports.Add, SongPluginStates.ForEngine(null, () => null), () => "tforge", _ => throw new InvalidOperationException("no question expected"), () => new DegradedSaveGate(), null, ask);

    [AvaloniaFact]
    public async Task ExportWritesAGp5CopyAndLeavesTheSongAsItWas()
    {
        var path = Path.Combine(_folder, "copy.gp5");
        var document = TwoVoiceSong();
        var dirty = document.IsDirty;
        var asked = new List<string>();
        var dialogs = new SaveTo(path);
        var wrote = await Saving(dialogs, new List<string>(), (report, name, format) => { asked.Add(name + ":" + format); return Task.FromResult(true); }).ExportLegacyCopy(document, LegacyGpFormat.Gp5);
        Assert.True(wrote);
        Assert.Empty(asked);   // .gp5 holds two voices: nothing is lost, so nothing is asked
        Assert.StartsWith("FICHIER GUITAR PRO v5", Encoding.ASCII.GetString(File.ReadAllBytes(path), 1, 24));
        Assert.Equal(dirty, document.IsDirty);
        Assert.Null(document.Path);
        Assert.Equal(".gp5 file (older programs) (*.gp5)|*.gp5", dialogs.Asked.Single().Filter);
        Assert.Single(GuitarProImporter.Import(path, new ImportContext()).Tracks);
    }

    [AvaloniaFact]
    public async Task ASecondVoiceInGp3IsAskedAboutAndCancelWritesNothing()
    {
        var path = Path.Combine(_folder, "lossy.gp3");
        var document = TwoVoiceSong();
        GpPreflightReport? shown = null;
        var wrote = await Saving(new SaveTo(path), new List<string>(), (report, _, _) => { shown = report; return Task.FromResult(false); }).ExportLegacyCopy(document, LegacyGpFormat.Gp3);
        Assert.False(wrote);
        Assert.False(File.Exists(path));
        Assert.Contains(shown!.Losses, l => l.Feature == "Second voice");

        wrote = await Saving(new SaveTo(path), new List<string>(), (_, _, _) => Task.FromResult(true)).ExportLegacyCopy(document, LegacyGpFormat.Gp3);
        Assert.True(wrote);
        Assert.True(File.Exists(path));
    }

    [AvaloniaFact]
    public async Task SaveAsToAnOlderTypeWritesACopyAndDoesNotSaveTheSong()
    {
        var path = Path.Combine(_folder, "as.gp4");
        var document = TwoVoiceSong();
        var dirty = document.IsDirty;
        var dialogs = new SaveTo(path);
        var saved = await Saving(dialogs, new List<string>(), (_, _, _) => Task.FromResult(true)).SaveAs(document);
        Assert.False(saved);   // a copy is not a save of the song
        Assert.True(File.Exists(path));
        Assert.Null(document.Path);
        Assert.Equal(dirty, document.IsDirty);
        var filter = dialogs.Asked.Single().Filter;
        foreach (var format in LegacyGpFormats.All) Assert.Contains(LegacyGpFormats.DialogFilter(format), filter);
    }

    [AvaloniaFact]
    public void TheLegacyPreflightWindowShowsTheLossesAndReportsSaveCopyOrCancel()
    {
        var report = new GpPreflightReport();
        report.Losses.Add(new GpLoss("Second voice", "left out", 1, "Lead: bar 1"));
        var chosen = new List<GpExportChoice>();
        var window = new GpExportPreflightWindow(report, LegacyGpFormat.Gp4, "song.gp4", chosen.Add);
        window.Show();
        Assert.Equal("Save .gp4 copy", window.Title);
        Assert.Contains("Second voice", window.FindControl<TextBox>("LossList")!.Text);
        var buttons = window.FindControl<WrapPanel>("ButtonsPanel")!.Children.OfType<Button>().ToList();
        Assert.Equal(new[] { "Save copy", "Cancel" }, buttons.Select(b => (string)b.Content!));
        buttons[0].RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { GpExportChoice.ExportCompatible }, chosen);
    }

    [Fact]
    public void ThePreflightWordingNeverNamesAnotherProgramAndOffersSaveCopyAndCancel()
    {
        foreach (var format in LegacyGpFormats.All)
        {
            var texts = string.Join(" ", LegacyGpPreflightPresenter.Title(format), LegacyGpPreflightPresenter.Headline("x" + LegacyGpFormats.Extension(format)), LegacyGpPreflightPresenter.Intro(format), LegacyGpPreflightPresenter.Advice, LegacyGpFormats.Label(format));
            Assert.DoesNotContain("Guitar Pro", texts, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("older programs", LegacyGpFormats.Label(format));
        }
        var buttons = LegacyGpPreflightPresenter.Buttons();
        Assert.Equal(new[] { "Save copy", "Cancel" }, buttons.Select(b => b.Label));
        Assert.Equal(GpExportChoice.ExportCompatible, buttons[0].Choice);
        Assert.True(buttons[0].IsDefault); Assert.True(buttons[1].IsCancel);
    }
}
