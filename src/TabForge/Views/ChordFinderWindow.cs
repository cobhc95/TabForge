using System.Windows;
using System.Windows.Controls;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Chord finder (Tools > Chord finder…): the notes of a chord for a root and a type. "Insert name" attaches the
/// chord name to the beat under the score cursor through the host's callback. Esc closes the window.
/// Owns: the window's layout and its root/type choice. Does not own: the chord tables (MusicTheoryService) or the score edit.
/// Tests: TestToolsChordFinderAndSongStats.
/// </summary>
public static class ChordFinderWindow
{
    /// <summary>Opens the window; <paramref name="insertChord"/> returns false when no beat is under the cursor.</summary>
    public static void Show(Window? owner, Func<string, bool> insertChord)
    {
        var w = new Window
        {
            Title = "Chord finder", Width = 380, Height = 330, MinWidth = 320, MinHeight = 260,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner, ShowInTaskbar = false,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");

        var root = new DockPanel { Margin = new Thickness(14) };
        var rootBox = new ComboBox { Width = 64, Margin = new Thickness(4, 0, 12, 0), ItemsSource = MusicTheoryService.NoteNames, SelectedIndex = 0 };
        var typeBox = new ComboBox { Width = 120, Margin = new Thickness(4, 0, 0, 0), ItemsSource = MusicTheoryService.Chords.Keys.ToList(), SelectedIndex = 0 };
        var pick = new WrapPanel();
        pick.Children.Add(new TextBlock { Text = "Root", VerticalAlignment = VerticalAlignment.Center });
        pick.Children.Add(rootBox);
        pick.Children.Add(new TextBlock { Text = "Type", VerticalAlignment = VerticalAlignment.Center });
        pick.Children.Add(typeBox);
        DockPanel.SetDock(pick, Dock.Top);
        root.Children.Add(pick);

        // ---- buttons and status ----
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var show = new Button { Content = "Show", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var insert = new Button { Content = "Insert name", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
        buttons.Children.Add(show);
        buttons.Children.Add(insert);
        buttons.Children.Add(close);
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(buttons);

        // ---- result ----
        var notes = new ListBox { Margin = new Thickness(0, 10, 0, 0), MinHeight = 90 };
        root.Children.Add(notes);

        string Root() => rootBox.SelectedItem as string ?? "C";
        string Type() => typeBox.SelectedItem as string ?? "Maj";
        show.Click += (_, _) =>
        {
            if (!MusicTheoryService.Chords.TryGetValue(Type(), out var intervals)) intervals = new[] { 0, 4, 7 };
            var rootIdx = Array.FindIndex(MusicTheoryService.NoteNames, n => n == Root());
            notes.ItemsSource = intervals.Select(tone => $"{MusicTheoryService.NoteNames[(rootIdx + tone) % 12]}  (tone {tone})").ToList();
            status.Text = "";
        };
        insert.Click += (_, _) =>
        {
            status.Text = insertChord($"{Root()}{Type()}") ? $"Inserted {Root()}{Type()} on the beat under the cursor."
                : "Put the score cursor on a beat first.";
        };
        w.Content = root;
        w.Loaded += (_, _) => show.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        DialogHost.ShowModal(w);
    }
}
