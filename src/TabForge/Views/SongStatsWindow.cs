using System.Windows;
using System.Windows.Controls;
using TabForge.Models;

namespace TabForge.Views;

/// <summary>
/// Song stats (Tools > Song stats…): the song's title and file, and counts of tracks, bars, notes and sections.
/// The counts are read when the window opens. Esc closes the window.
/// Owns: the window's layout and the counts. Does not own: the score or the song file.
/// Tests: TestToolsChordFinderAndSongStats.
/// </summary>
public static class SongStatsWindow
{
    public static void Show(Window? owner, SongProject project, string? path)
    {
        var notes = project.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count)));
        var bars = project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count);
        var title = string.IsNullOrWhiteSpace(path)
            ? $"{project.Title} (unsaved)"
            : $"{project.Title} — {System.IO.Path.GetFileName(path)}";

        var w = new Window
        {
            Title = "Song stats", Width = 400, Height = 190, MinWidth = 320, MinHeight = 170,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner, ShowInTaskbar = false,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");

        var root = new DockPanel { Margin = new Thickness(14) };
        var close = new Button { Content = "Close", Padding = new Thickness(12, 4, 12, 4), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        var stats = new TextBlock
        {
            Text = $"Tracks {project.Tracks.Count}   Bars {bars}   Notes {notes}   Sections {project.Markers.Count}",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
        };
        body.Children.Add(stats);
        root.Children.Add(body);

        w.Content = root;
        DialogHost.ShowModal(w);
    }
}
