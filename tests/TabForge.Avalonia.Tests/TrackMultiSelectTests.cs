using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the multi-selection of the track list in a real window with real pointer and keys: Shift range, Ctrl toggle, plain click and Esc clear, one Delete question and one undo,
//   mute/solo/arm on all selected, volume and pan moved by one delta (live push, reset), copy/cut/paste of the selected tracks, group reorder, and single-track behaviour unchanged.
// Does not own: the pure selection and edit rules (TrackGroupEditsTests), the routing table (EditRoutingTests).
// Tests: this class.
public sealed partial class TrackMultiSelectTests
{
    private sealed class Rig
    {
        public required SongDocument Song { get; init; }
        public required MainWindow Window { get; init; }
        public required ArrangementView View { get; init; }
        public SelectionModel Model => Song.Selection;
        public ClipboardService Clipboard => View.Timeline.ContextMenus.Clipboard!;
        public List<TrackModel> Tracks => Song.Project.Tracks;
        public void Close() { Song.MarkClean(); Window.Close(); }
    }

    private static Rig Open(int tracks = 6)
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        while (song.Project.Tracks.Count < tracks) MainWindowTrackMenu.AddTrack(song, TrackKind.Audio);
        if (song.Project.Tracks.Count > tracks) song.Project.Tracks.RemoveRange(tracks, song.Project.Tracks.Count - tracks);
        for (var i = 0; i < song.Project.Tracks.Count; i++) { song.Project.Tracks[i].Name = $"T{i}"; song.Project.Tracks[i].Volume = 60 + i * 10; }
        song.MarkClean();
        var window = new MainWindow(first: song);
        window.Show();
        window.UpdateLayout();
        return new Rig { Song = song, Window = window, View = window.GetLogicalDescendants().OfType<ArrangementView>().First() };
    }

    private static Point NamePoint(Rig rig, int row)
    {
        var name = TrackRowCells.Column("name", rig.View.Header.NameColumnWidth);
        return rig.View.Header.TranslatePoint(new Point(name.Left + name.Width / 2, rig.View.Timeline.RowTop(row) + 10), rig.Window)!.Value;
    }

    private static Point CellPoint(Rig rig, int row, TrackRowCells.Cell cell, double fraction = 0.5)
    {
        var box = rig.View.Header.CellRect(row, cell);
        return rig.View.Header.TranslatePoint(new Point(box.X + box.Width * fraction, box.Center.Y), rig.Window)!.Value;
    }

    private static void Click(Rig rig, Point at, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        rig.Window.MouseDown(at, MouseButton.Left, modifiers);
        rig.Window.MouseUp(at, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Select(Rig rig, int row, RawInputModifiers modifiers = RawInputModifiers.None) => Click(rig, NamePoint(rig, row), modifiers);

    private static string[] Picked(Rig rig) => rig.Tracks.Where(t => rig.Model.Rows.Contains(t.Id)).Select(t => t.Name).ToArray();

    private static string[] Names(Rig rig) => rig.Tracks.Select(t => t.Name).ToArray();

    private static int[] Where(Rig rig, Func<TrackModel, bool> test) => Enumerable.Range(0, rig.Tracks.Count).Where(i => test(rig.Tracks[i])).ToArray();

    private static void Press(Rig rig, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        rig.Window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = rig.Window });
        Dispatcher.UIThread.RunJobs();
    }

    private static PromptWindow? Prompt(Rig rig)
    {
        Dispatcher.UIThread.RunJobs();
        return rig.Window.OwnedWindows.OfType<PromptWindow>().SingleOrDefault();
    }

    private static void Answer(PromptWindow prompt, string button)
    {
        prompt.GetLogicalDescendants().OfType<Button>().Single(b => (b.Content as string) == button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitUntil(Func<bool> done)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!done() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Assert.True(done());
    }
}
