using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the window's document commands: File > New / Open / Export MIDI, Edit > Undo / Redo and the note commands on the shown tab, and the panes following the shown tab.</summary>
public sealed class MainWindowDocumentsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-avdocs-{Guid.NewGuid():N}");

    public MainWindowDocumentsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    private sealed class Dialogs : IFileDialogs
    {
        public string[]? Open { get; set; }
        public string? Save { get; set; }
        public string? SaveFile(FileDialogOptions options, object? owner) => Save;
        public string? OpenFile(FileDialogOptions options, object? owner) => throw new InvalidOperationException();
        public string[]? OpenFiles(FileDialogOptions options, object? owner) => Open;
        public string? PickFolder(FileDialogOptions options, object? owner) => throw new InvalidOperationException();
        public string[]? PickFolders(FileDialogOptions options, object? owner) => throw new InvalidOperationException();
    }

    private static MainWindowTabs Tabs() => new(MainWindowTabs.NewSong(), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));

    private static MenuItem Item(MainWindow main, string id) => main.GetLogicalDescendants().OfType<MenuItem>().Single(m => (m.Tag as string) == id);

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [AvaloniaFact]
    public void FileNewMenuOpensANewTabAndTheScoreFollowsIt()
    {
        var main = new MainWindow();
        main.Show();
        var strip = main.GetLogicalDescendants().OfType<TabStripView>().Single();
        var score = main.GetLogicalDescendants().OfType<ScorePreviewView>().Single();
        var before = score.Editor.Project;
        Assert.Single(strip.Tabs);
        Click(Item(main, "File.New"));
        Assert.Equal(2, strip.Tabs.Count);
        Assert.NotSame(before, score.Editor.Project);
        main.Close();
    }

    [AvaloniaFact]
    public async Task OpenReadsATforgeFileWrittenByCoreIntoANewTab()
    {
        var path = Path.Combine(_folder, "riff.tforge");
        var written = MainWindowTabs.NewSong();
        written.Project.Title = "Riff";
        await new DocumentController().SaveToFileAsync(written, path);
        // Open replaces the shown tab by default; this test checks the open-in-a-new-tab path, so that setting is off.
        var settings = new AppSettings { Tabs = new TabSettings { OpenInCurrentTab = false } };
        var tabs = new MainWindowTabs(MainWindowTabs.NewSong(), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false), settings: settings);
        var reports = new List<string>();
        await new MainWindowFile(new Dialogs { Open = new[] { path } }, null, tabs, reports.Add).Open();
        Assert.Equal(2, tabs.Songs.Count);
        Assert.Equal("Riff", tabs.Active.Project.Title);
        Assert.Equal(path, tabs.Active.Path);
        Assert.False(tabs.Active.HasUnsavedChanges);
        Assert.Empty(reports);   // a clean open says nothing: the tab is the confirmation
    }

    [AvaloniaFact]
    public async Task OpenOfAnUnreadableFileReportsAndKeepsTheTabs()
    {
        var path = Path.Combine(_folder, "bad.tforge");
        File.WriteAllText(path, "not a project");
        var tabs = Tabs();
        var reports = new List<string>();
        Assert.False(await new MainWindowFile(new Dialogs(), null, tabs, reports.Add).OpenPath(path));
        Assert.Single(tabs.Songs);
        Assert.StartsWith("Could not open bad.tforge", Assert.Single(reports));
    }

    [AvaloniaFact]
    public async Task GtpFileGetsTheTuxGuitarMessageExactly()
    {
        var path = Path.Combine(_folder, "old.GTP");
        File.WriteAllText(path, "not read");
        var tabs = Tabs();
        var reports = new List<string>();
        Assert.False(await new MainWindowFile(new Dialogs(), null, tabs, reports.Add).OpenPath(path));
        Assert.Single(tabs.Songs);
        Assert.Equal("TuxGuitar .gtp files aren't supported. Open it in TuxGuitar and save it as .gp5, then open that here.", Assert.Single(reports));
        Assert.Equal("TuxGuitar .gtp files aren't supported. Open it in TuxGuitar and save it as .gp5, then open that here.", Assert.Throws<InvalidDataException>(() => new DocumentController().Open(path)).Message);
    }

    [AvaloniaFact]
    public async Task DeviceAndLinkedPathsAreRefusedBeforeOpeningOrSaving()
    {
        if (!OperatingSystem.IsWindows()) return;   // the profile junction is a Windows reparse point
        // The profile's compatibility junction is a real reparse point that needs no privilege to read.
        var junction = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Application Data");
        Assert.True(Directory.Exists(junction) && (File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0,
            "The profile's Application Data junction is needed for the reparse check.");

        Assert.Null(AvaloniaFileDialogs.CheckedFiles(new[] { @"\\?\C:\Windows\notepad.exe" }));
        Assert.Null(AvaloniaFileDialogs.CheckedFiles(new[] { junction }));

        var tabs = Tabs();
        var reports = new List<string>();
        var files = new MainWindowFile(new Dialogs(), null, tabs, reports.Add);

        Assert.False(await files.OpenPath(@"\\?\C:\Windows\notepad.exe"));
        Assert.Single(tabs.Songs);
        Assert.StartsWith("Could not open", Assert.Single(reports));

        reports.Clear();
        Assert.False(await files.OpenPath(junction));
        Assert.Single(tabs.Songs);
        Assert.Contains("linked", Assert.Single(reports));
    }

    [AvaloniaFact]
    public async Task ExportMidiWritesTheShownSong()
    {
        var path = Path.Combine(_folder, "song.mid");
        var reports = new List<string>();
        await new MainWindowFile(new Dialogs { Save = path }, null, Tabs(), reports.Add).ExportMidi();
        Assert.True(new FileInfo(path).Length > 14, string.Join(";", reports));
    }

    [AvaloniaFact]
    public async Task ExportAsciiAndMusicXmlWriteTheShownSong()
    {
        var reports = new List<string>();
        var asciiPath = Path.Combine(_folder, "song.txt");
        await new MainWindowFile(new Dialogs { Save = asciiPath }, null, Tabs(), reports.Add).ExportAscii();
        Assert.True(new FileInfo(asciiPath).Length > 0, string.Join(";", reports));
        var xmlPath = Path.Combine(_folder, "song.musicxml");
        await new MainWindowFile(new Dialogs { Save = xmlPath }, null, Tabs(), reports.Add).ExportMusicXml();
        Assert.True(new FileInfo(xmlPath).Length > 0, string.Join(";", reports));
    }

    [AvaloniaFact]
    public void UndoAndRedoCommandsRestoreTheShownSong()
    {
        var tabs = Tabs();
        var score = new ScorePreviewView();
        _ = new MainWindowDocumentPanes(tabs, score);
        var owner = new Window(); var commands = new MainWindowCommands(owner, () => { });
        MainWindowEdit.Register(commands, score, owner, new ContextMenuHost(), () => false, _ => { });
        score.Probe.Cursor = (0, 0, 0);
        var start = DocumentEdits.Fingerprint(tabs.Active);
        commands.Run("Edit.InsertBeat");
        var edited = DocumentEdits.Fingerprint(tabs.Active);
        Assert.NotEqual(start, edited);
        commands.Run("Edit.Undo");
        Assert.Equal(start, DocumentEdits.Fingerprint(tabs.Active));
        commands.Run("Edit.Redo");
        Assert.Equal(edited, DocumentEdits.Fingerprint(tabs.Active));
    }

    [AvaloniaFact]
    public void SectionJumpCommandsSeekPlaybackToTheSectionBar()
    {
        var tabs = Tabs();
        var score = new ScorePreviewView();
        _ = new MainWindowDocumentPanes(tabs, score);
        var owner = new Window(); var commands = new MainWindowCommands(owner, () => { });
        var seeks = new List<int>();
        MainWindowEdit.Register(commands, score, owner, new ContextMenuHost(), () => false, _ => { }, seekPlayback: seeks.Add);
        score.Probe.Cursor = (2, 0, 0);
        score.Editor.AddOrEditMarker("Verse", "#2E74B5");
        score.Probe.Cursor = (0, 0, 0);
        commands.Run("Section.Next");
        Assert.Equal(new[] { 2 }, seeks);
    }

    [AvaloniaFact]
    public void SwitchingTabsShowsThatSongInTheScoreAndTheArrangement()
    {
        var first = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        first.MarkClean();
        var tabs = new MainWindowTabs(first, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var score = new ScorePreviewView();
        var panes = new MainWindowDocumentPanes(tabs, score);
        var arrangement = panes.NewArrangement();
        Assert.Same(first.Project, score.Editor.Project);
        tabs.New();
        var second = tabs.Active;
        Assert.Same(second.Project, score.Editor.Project);
        Assert.Equal(new TimelineView(second.Project).Width, arrangement.Timeline.Width);
        tabs.Switch(0);
        Assert.Same(first.Project, score.Editor.Project);
        Assert.Equal(new TimelineView(first.Project).Width, arrangement.Timeline.Width);
        Assert.NotEqual(new TimelineView(first.Project).Width, new TimelineView(second.Project).Width);
    }

    [AvaloniaFact]
    public void EveryListedEditCommandIsAMenuRowAndRunsInTheEditor()
    {
        var rows = MainMenuTable.CommandRows(MainMenuTable.Groups).Select(r => r.CommandId!).ToHashSet();
        var score = new ScorePreviewView();
        score.Show(MainWindowTabs.NewSong());
        score.Probe.Cursor = (0, 0, 0);
        foreach (var id in MainWindowEdit.Commands.Where(id => id is not ("Edit.Undo" or "Edit.Redo")))
        {
            Assert.Contains(id, rows);
            Assert.True(score.Editor.RunCommand(id), id);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainWindowWithTwoTabsRendersInEachTheme(bool light)
    {
        var main = new MainWindow { RequestedThemeVariant = light ? global::Avalonia.Styling.ThemeVariant.Light : global::Avalonia.Styling.ThemeVariant.Dark };
        main.Show();
        Click(Item(main, "File.New"));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var frame = main.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window rendered no frame.");
        var directory = DialogTestKit.RenderDirectory(nameof(MainWindowDocumentsTests));
        var path = Path.Combine(directory, $"main-two-tabs-{(light ? "light" : "dark")}.png");
        frame.Save(path);
        Assert.True(new FileInfo(path).Length > 0);
        main.Close();
    }
}
