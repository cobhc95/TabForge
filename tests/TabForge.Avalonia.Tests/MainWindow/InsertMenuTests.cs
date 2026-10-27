using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Insert menu: its new commands (Track.AddAudio, bar after, several bars, Bar.Append, Lyrics, audio and MIDI files at the cursor, an empty MIDI clip at the cursor), the
/// views redrawing at once after each insert, and the consolidated menu tree (eleven menus, no Sound or Sections menu, each command once, unique access keys).
/// Does not own: the older bar, track and section commands (their own tests) or the key defaults (MenuHotkeysUnchangedTests).
/// Tests: this class.
/// </summary>
[Collection("MediaPolicyFileSystem")]
public sealed class InsertMenuTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-insert-{Guid.NewGuid():N}");

    public InsertMenuTests() => Directory.CreateDirectory(_folder);

    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    /// <summary>The window parts the Insert commands need, wired as the main window wires them, with a fake file picker and a fake Several bars dialog.</summary>
    private sealed class Rig
    {
        public MainWindowTabs Tabs = new(new SongDocument(() => null) { Project = DemoSongFactory.Create() }, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        public ScorePreviewView Score = new();
        public MainWindowDocumentPanes Panes = null!;
        public ArrangementView Arrangement = null!;
        public MainWindowCommands Commands = null!;
        public List<string> Status = new();
        public string? NextFile;
        public InsertBarsAnswer? NextBars;
        public int LyricsShown;
        public Task LastImport = Task.CompletedTask;

        public SongDocument Song => Tabs.Active;

        public Rig()
        {
            Panes = new MainWindowDocumentPanes(Tabs, Score);
            Arrangement = (ArrangementView)Panes.NewArrangement();
            var owner = new Window();
            Commands = new MainWindowCommands(owner, () => { });
            void Landed(TrackModel? track) => MainWindowInsertRefresh.Landed(Panes, Score, Tabs.Active, track);
            MainWindowEdit.Register(Commands, Score, owner, new ContextMenuHost(), () => false, Status.Add, afterInsert: () => Landed(null));
            MainWindowTrackMenu.Register(Commands, Tabs, owner, new ContextMenuHost(), Status.Add, null, track => Landed(track));
            var drop = MainWindowAudioDrop.Register(Panes, Tabs, Status.Add);
            MainWindowInsertMenu.Register(Commands, Tabs, Panes, Score,
                new MainWindowInsertMenu.Prompts(_ => Task.FromResult(NextBars), _ => NextFile),
                Status.Add, () => LyricsShown++, (files, track, sec) => LastImport = drop.InsertAtAsync(files, track, sec));
        }

        /// <summary>Waits for the file import the last Insert file command started, then lets its continuation run.</summary>
        public async Task Imported()
        {
            await LastImport;
            Dispatcher.UIThread.RunJobs();
        }

        public int Bars => BarRangeEditor.MaxMeasures(Song.Project);

        /// <summary>True when the arrangement shows the song as it is now: its rows and its width are those a fresh timeline reads from the song.</summary>
        public bool ArrangementIsCurrent() =>
            Arrangement.Timeline.RowCount == Song.Project.Tracks.Count && Arrangement.Timeline.Width == new TimelineView(Song.Project).Width;
    }

    /// <summary>Every path is an ordinary fixed-drive file, as the engine tests judge it; the previous judgement comes back on dispose.</summary>
    private sealed class FixedFiles : IMediaFileSystem, IDisposable
    {
        private readonly IMediaFileSystem _saved = MediaPathPolicy.FileSystem;

        public FixedFiles() => MediaPathPolicy.FileSystem = this;

        public System.IO.DriveType DriveTypeOf(string root) => System.IO.DriveType.Fixed;

        public string? FinalPathOf(string fullPath) => fullPath;

        public void Dispose() => MediaPathPolicy.FileSystem = _saved;
    }

    private string WriteWav()
    {
        var path = Path.Combine(_folder, "tone.wav");
        const int rate = 8000;
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            var data = rate * 2;
            writer.Write("RIFF".ToCharArray()); writer.Write(36 + data); writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray()); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data".ToCharArray()); writer.Write(data);
            writer.Write(new byte[data]);
        }
        return path;
    }

    // Format 0, 480 ticks per quarter, one note (the same file as the take-lane import test).
    private string WriteMidi()
    {
        var path = Path.Combine(_folder, "riff.mid");
        File.WriteAllBytes(path, new byte[]
        {
            0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0,
            0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 13, 0x00, 0x90, 0x3C, 0x64, 0x83, 0x60, 0x80, 0x3C, 0x40, 0x00, 0xFF, 0x2F, 0x00,
        });
        return path;
    }

    private static TrackModel AddAudioTrack(Rig rig)
    {
        var track = MainWindowTrackMenu.AddTrack(rig.Song, TrackKind.Audio)!;
        rig.Song.TrackIndex = rig.Song.Project.Tracks.IndexOf(track);
        return track;
    }

    [AvaloniaFact]
    public void AudioTrackRowAddsAnAudioTrackAtOnceAndShowsIt()
    {
        var rig = new Rig();
        var tracks = rig.Song.Project.Tracks.Count;
        var undo = rig.Song.Undo.UndoCount;
        rig.Commands.Run("Track.AddAudio");
        Assert.Equal(tracks + 1, rig.Song.Project.Tracks.Count);
        Assert.True(rig.Song.Project.Tracks[^1].IsAudio);
        Assert.Equal(tracks, rig.Song.TrackIndex);   // the new track is the selected one
        Assert.True(rig.ArrangementIsCurrent());
        Assert.Equal(undo + 1, rig.Song.Undo.UndoCount);
    }

    [AvaloniaTheory]
    [InlineData("Track.AddBass", TrackKind.Bass)]
    [InlineData("Track.AddDrums", TrackKind.Drums)]
    [InlineData("Track.AddKeys", TrackKind.Keys)]
    public void TheInstrumentTrackRowsRedrawTheTrackListAtOnce(string id, TrackKind kind)
    {
        var rig = new Rig();
        var tracks = rig.Song.Project.Tracks.Count;
        rig.Commands.Run(id);
        Assert.Equal(kind, rig.Song.Project.Tracks[^1].Kind);
        Assert.Equal(tracks, rig.Song.TrackIndex);
        Assert.True(rig.ArrangementIsCurrent());
    }

    [AvaloniaFact]
    public void BarAfterInsertsOneEmptyBarAfterTheCursorBarAndRedrawsAtOnce()
    {
        var rig = new Rig();
        var bars = rig.Bars;
        var undo = rig.Song.Undo.UndoCount;
        rig.Score.Probe.Cursor = (1, 0, 0);
        rig.Commands.Run("Bar.InsertAfter");
        Assert.Equal(bars + 1, rig.Bars);
        Assert.Equal(2, rig.Score.Editor.SelectedMeasure);   // the cursor is on the new bar
        Assert.All(rig.Song.Project.Tracks.Where(t => !t.IsAudio), t => Assert.DoesNotContain(t.Measures[2].Cells, c => c.Notes.Count > 0));
        Assert.True(rig.ArrangementIsCurrent());
        Assert.Equal(undo + 1, rig.Song.Undo.UndoCount);
    }

    [AvaloniaFact]
    public void BarBeforeRedrawsAtOnceToo()
    {
        var rig = new Rig();
        var bars = rig.Bars;
        rig.Score.Probe.Cursor = (1, 0, 0);
        rig.Commands.Run("Bar.Insert");
        Assert.Equal(bars + 1, rig.Bars);
        Assert.True(rig.ArrangementIsCurrent());
    }

    [AvaloniaTheory]
    [InlineData(InsertBarsPlace.BeforeSelected, 1)]
    [InlineData(InsertBarsPlace.AfterSelected, 2)]
    [InlineData(InsertBarsPlace.AtEnd, -1)]
    public void SeveralBarsGoWhereTheDialogSaidAsOneUndoStep(InsertBarsPlace place, int firstNewBar)
    {
        var rig = new Rig();
        var bars = rig.Bars;
        var undo = rig.Song.Undo.UndoCount;
        rig.Score.Probe.Cursor = (1, 0, 0);
        rig.NextBars = new InsertBarsAnswer(3, place);
        rig.Commands.Run("Bar.InsertSeveral");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(bars + 3, rig.Bars);
        Assert.Equal(firstNewBar < 0 ? bars : firstNewBar, rig.Score.Editor.SelectedMeasure);
        Assert.True(rig.ArrangementIsCurrent());
        Assert.Equal(undo + 1, rig.Song.Undo.UndoCount);
        rig.Commands.Run("Edit.Undo");
        Assert.Equal(bars, rig.Bars);
    }

    [AvaloniaFact]
    public void CancellingTheSeveralBarsDialogChangesNothing()
    {
        var rig = new Rig();
        var bars = rig.Bars;
        rig.NextBars = null;
        rig.Commands.Run("Bar.InsertSeveral");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(bars, rig.Bars);
    }

    [AvaloniaFact]
    public void BarAtEndWrapsTheAddBarAtTheEndTool()
    {
        var rig = new Rig();
        var bars = rig.Bars;
        rig.Score.Probe.Cursor = (0, 0, 0);
        rig.Commands.Run("Bar.Append");
        Assert.Equal(bars + 1, rig.Bars);
        Assert.True(rig.ArrangementIsCurrent());
    }

    [AvaloniaFact]
    public void LyricsRowRunsTheShowLyricsAction()
    {
        var rig = new Rig();
        rig.Commands.Run("Insert.Lyrics");
        Assert.Equal(1, rig.LyricsShown);
    }

    [AvaloniaFact]
    public async Task AudioFileLandsAtTheCursorTimeOnTheSelectedAudioTrack()
    {
        using var files = new FixedFiles();
        var rig = new Rig();
        var track = AddAudioTrack(rig);
        rig.Score.Probe.Cursor = (1, 0, 0);
        rig.NextFile = WriteWav();
        var undo = rig.Song.Undo.UndoCount;
        rig.Commands.Run("Insert.AudioFile");
        await rig.Imported();
        var clip = Assert.Single(track.AudioClips);
        Assert.Equal(AudioClipTimelineRules.SecondsAtCursor(rig.Song.Project, 1, 0), clip.StartSec, 3);
        Assert.False(clip.IsMidi);
        Assert.Equal(undo + 1, rig.Song.Undo.UndoCount);
        Assert.True(rig.ArrangementIsCurrent());
    }

    [AvaloniaFact]
    public async Task AudioFileOnANotationTrackGoesOnANewAudioTrack()
    {
        using var files = new FixedFiles();
        var rig = new Rig();
        rig.Song.TrackIndex = 0;
        var tracks = rig.Song.Project.Tracks.Count;
        rig.Score.Probe.Cursor = (2, 0, 0);
        rig.NextFile = WriteWav();
        rig.Commands.Run("Insert.AudioFile");
        await rig.Imported();
        Assert.Equal(tracks + 1, rig.Song.Project.Tracks.Count);
        var added = rig.Song.Project.Tracks[^1];
        Assert.True(added.IsAudio);
        Assert.Equal(AudioClipTimelineRules.SecondsAtCursor(rig.Song.Project, 2, 0), Assert.Single(added.AudioClips).StartSec, 3);
        Assert.Equal(tracks, rig.Song.TrackIndex);
        Assert.True(rig.ArrangementIsCurrent());
    }

    [AvaloniaFact]
    public async Task CancellingTheFilePickerAddsNothing()
    {
        var rig = new Rig();
        var tracks = rig.Song.Project.Tracks.Count;
        rig.NextFile = null;
        rig.Commands.Run("Insert.MidiFile");
        await rig.Imported();
        Assert.Equal(tracks, rig.Song.Project.Tracks.Count);
    }

    [AvaloniaFact]
    public async Task MidiFileBecomesAMidiClipAtTheCursorTime()
    {
        var rig = new Rig();
        var track = AddAudioTrack(rig);
        rig.Score.Probe.Cursor = (3, 0, 0);
        rig.NextFile = WriteMidi();
        rig.Commands.Run("Insert.MidiFile");
        await rig.Imported();
        var clip = Assert.Single(track.AudioClips);
        Assert.True(clip.IsMidi);
        Assert.Equal(AudioClipTimelineRules.SecondsAtCursor(rig.Song.Project, 3, 0), clip.StartSec, 3);
        Assert.True(rig.ArrangementIsCurrent());
    }

    [AvaloniaFact]
    public void EmptyMidiClipIsOneBarAtTheCursorOnTheFirstFreeLane()
    {
        var rig = new Rig();
        var track = AddAudioTrack(rig);
        rig.Score.Probe.Cursor = (1, 0, 0);
        var undo = rig.Song.Undo.UndoCount;
        rig.Commands.Run("Insert.EmptyMidiClip");
        var first = Assert.Single(track.AudioClips);
        Assert.True(first.IsMidi);
        Assert.Equal(AudioClipTimelineRules.SecondsAtCursor(rig.Song.Project, 1, 0), first.StartSec, 3);
        Assert.Equal(0, first.Lane);
        Assert.Equal(undo + 1, rig.Song.Undo.UndoCount);
        rig.Commands.Run("Insert.EmptyMidiClip");   // the same place again: the first lane is taken, so the clip takes the next free one
        Assert.Equal(new[] { 0, 1 }, track.AudioClips.Select(c => c.Lane).OrderBy(l => l));
        Assert.True(rig.ArrangementIsCurrent());
    }

    [AvaloniaFact]
    public void EmptyMidiClipOnANotationTrackMakesANewAudioTrackInTheSameStep()
    {
        var rig = new Rig();
        rig.Song.TrackIndex = 0;
        var tracks = rig.Song.Project.Tracks.Count;
        var undo = rig.Song.Undo.UndoCount;
        rig.Commands.Run("Insert.EmptyMidiClip");
        Assert.Equal(tracks + 1, rig.Song.Project.Tracks.Count);
        Assert.True(rig.Song.Project.Tracks[^1].AudioClips.Single().IsMidi);
        Assert.Equal(undo + 1, rig.Song.Undo.UndoCount);
        Assert.True(rig.ArrangementIsCurrent());
    }

    // ---- the menu tree ----

    private static MenuItem Item(MainWindow main, string id) => main.GetLogicalDescendants().OfType<MenuItem>().Single(m => (m.Tag as string) == id);

    private static IReadOnlyList<MainMenuRow> RowsOf(string menu) => MainMenuTable.WithFeatureRows().Where(g => g.Menu == menu).SelectMany(g => g.Rows).ToList();

    [AvaloniaFact]
    public void TheMenuBarIsElevenMenusInOrderWithoutSoundAndSections()
    {
        var main = new MainWindow();
        main.Show();
        var menu = main.GetLogicalDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu");
        var headers = menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        Assert.Equal(new[] { "_File", "_Edit", "_Insert", "_Bar", "_Track", "_Note", "_Effects", "_View", "_Tools", "_Options", "_Help" }, headers);
        Assert.DoesNotContain("_Sound", headers);
        Assert.DoesNotContain("_Sections", headers);
        main.Close();
    }

    [AvaloniaFact]
    public void EveryInsertRowIsHandledByTheWindowAndRunsAsAMenuClick()
    {
        var main = new MainWindow();
        main.Show();
        var ids = MainMenuTable.CommandRows(MainMenuTable.WithFeatureRows().Where(g => g.Menu == "_Insert")).Select(r => r.CommandId!).ToList();
        Assert.All(ids, id => Assert.True(main.CommandTable.Handles(id), id));
        Assert.All(ids, id => Assert.True(Item(main, id).IsEnabled, id));
        main.Close();
    }

    [AvaloniaFact]
    public void AnInsertMenuClickRedrawsTheTrackListAtOnce()
    {
        const string id = "Track.AddAudio";
        var main = new MainWindow();
        main.Show();
        var tracks = main.Songs.Active.Project.Tracks.Count;
        Item(main, id).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var arrangement = main.GetLogicalDescendants().OfType<ArrangementView>().Single();
        Assert.Equal(tracks + 1, arrangement.Timeline.RowCount);   // no layout pass in between: the rows are already re-read
        main.Songs.Active.MarkClean();   // an edited song would ask to save when the window closes
        main.Close();
    }

    [AvaloniaFact]
    public void LyricsRowOpensAndFocusesTheLyricsPane()
    {
        var main = new MainWindow();
        main.Show();
        main.DockWorkspace.SetPanelVisible("lyrics", false);
        Dispatcher.UIThread.RunJobs();
        Item(main, "Insert.Lyrics").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(main.DockWorkspace.IsPanelVisible("lyrics"));
        var pane = main.GetLogicalDescendants().OfType<TabForge.Avalonia.Lyrics.LyricsSongPane>().Single();
        Assert.True(pane.IsEffectivelyVisible);
        main.Close();
    }

    [Fact]
    public void EachCommandIsInTheMenusOnceAndTheMovedRowsLiveWhereTheyWereMoved()
    {
        var all = MainMenuTable.CommandRows(MainMenuTable.WithFeatureRows()).Select(r => r.CommandId!).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        var moved = new (string Id, string Menu)[]
        {
            ("Bar.First", "_Bar"), ("Bar.Last", "_Bar"), ("Bar.GoTo", "_Bar"), ("Section.Previous", "_Bar"), ("Section.Next", "_Bar"), ("Section.Edit", "_Bar"), ("Bar.Check", "_Bar"), ("Bar.EmptyBar", "_Bar"),
            ("Sound.TestOutput", "_Track"), ("Sound.AudioSetup", "_Options"), ("Tools.PreviewNotes", "_Options"), ("App.Preferences", "_Options"), ("App.CommandPalette", "_Help"),
            ("Transport.RecordVideo", "_File"), ("File.Render", "_File"), ("File.CollectMedia", "_File"), ("File.FindMissingMedia", "_File"), ("Edit.RepeatSelection", "_Edit"), ("Edit.SelectAll", "_Edit"),
            ("Section.Add", "_Insert"), ("Note.Chord", "_Insert"), ("Note.Text", "_Insert"), ("Edit.InsertBeat", "_Insert"), ("Track.Add", "_Insert"), ("Bar.Insert", "_Insert"), ("Tool.composition:tempo", "_Insert"),
            ("View.LearnMode", "_View"), ("View.CompactMode", "_View"), ("Tools.PresetManager", "_Tools"),
        };
        foreach (var (id, menu) in moved) Assert.Contains(id, MainMenuTable.CommandRows(MainMenuTable.WithFeatureRows().Where(g => g.Menu == menu)).Select(r => r.CommandId));
        var gone = new[] { "Transport.PlayPause", "Transport.PlayFromStart", "Transport.Metronome", "Transport.CountIn", "Options.Stylesheet" };
        Assert.All(gone, id => Assert.DoesNotContain(id, all));
    }

    [Fact]
    public void EveryMenuCommandIsCataloguedSoItIsInThePaletteAndTheShortcutList()
    {
        var all = MainMenuTable.CommandRows(MainMenuTable.WithFeatureRows()).Select(r => r.CommandId!).ToList();
        Assert.All(all, id => Assert.NotNull(HotkeyCatalog.ById(id)));
    }

    [Fact]
    public void AccessKeysAreUniqueInEveryMenuAndSubmenu()
    {
        var problems = new List<string>();
        void Check(string where, IEnumerable<MainMenuRow> rows)
        {
            var keys = rows.Where(r => r.Header is not null).Select(r => (Row: r, Key: AccessKey(r.Header!))).Where(x => x.Key is not null).ToList();
            foreach (var group in keys.GroupBy(x => x.Key).Where(g => g.Count() > 1)) problems.Add($"{where}: {group.Key} on {string.Join(" / ", group.Select(x => x.Row.Header))}");
            foreach (var row in rows.Where(r => r.IsSubmenu)) Check(where + " > " + row.Header, row.Children!);
        }
        foreach (var menu in MainMenuTable.Groups.Select(g => g.Menu).Distinct()) Check(menu, RowsOf(menu));
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static char? AccessKey(string header)
    {
        var at = header.IndexOf('_');
        return at >= 0 && at + 1 < header.Length ? char.ToUpperInvariant(header[at + 1]) : null;
    }

    [AvaloniaFact]
    public void TheTransportToggleCommandsAreStillHandledAfterTheirMenuRowsLeft()
    {
        var main = new MainWindow();
        main.Show();
        Assert.All(new[] { "Transport.Metronome", "Transport.CountIn", "Transport.PlayPause", "Transport.PlayFromStart", "Options.Stylesheet", "Sound.TestOutput", "File.CollectMedia" }, id => Assert.True(main.CommandTable.Handles(id), id));
        var entries = MainWindowCommandPalette.Entries(main.CommandTable, new HotkeySettings()).Select(e => e.Id).ToList();
        Assert.All(new[] { "Transport.Metronome", "Transport.CountIn", "Transport.PlayPause", "Options.Stylesheet", "File.CollectMedia", "Insert.MidiFile", "Tool.composition:tempo", "Bar.Append" }, id => Assert.Contains(id, entries));
        main.Close();
    }
}
