using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views;

/// <summary>Edited values from the project settings dialog; the caller applies them with undo.</summary>
public sealed record ProjectSettingsResult(
    string Title, string Subtitle, string Artist, string Album,
    string MusicAuthor, string LyricsAuthor, string TabAuthor, string Copyright,
    int Tempo, int TimeSigNum, int TimeSigDenom, int KeySignature, bool KeyMinor, bool GrayInactiveVoice,
    string Instructions, string Notice, string Lyrics);

/// <summary>
/// Per-song settings (as opposed to the global Preferences window): the reference "score information"
/// credits plus the song-level musical defaults, notes and lyrics, and a read-only summary.
/// </summary>
public static class ProjectSettingsWindow
{
    private static readonly string[] KeyNames = { "Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#" };
    private static readonly int[] Denominators = { 1, 2, 4, 8, 16, 32 };

    public static ProjectSettingsResult? Show(Window owner, SongProject p)
    {
        var w = new Window
        {
            Title = "Project settings",
            Width = 640, Height = 600, MinWidth = 520, MinHeight = 460,
            Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        w.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Control.ForegroundProperty, "TextBrush");

        TextBox Text(string value, bool multiline = false) => new()
        {
            Text = value ?? "",
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
            MinHeight = multiline ? 110 : 26,
            MaxHeight = multiline ? 320 : double.PositiveInfinity,   // a long notice or lyrics (up to 64K) scrolls inside its box
            VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center
        };

        StackPanel Page() => new() { Margin = new Thickness(16, 12, 16, 12) };

        void Field(Panel page, string label, FrameworkElement editor, string? hint = null)
        {
            var caption = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 3) };
            page.Children.Add(caption);
            page.Children.Add(editor);
            if (hint is not null)
            {
                var note = new TextBlock { Text = hint, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
                note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                page.Children.Add(note);
            }
        }

        // ---- Song ----
        var title = Text(p.Title); var subtitle = Text(p.Subtitle);
        var artist = Text(p.Artist); var album = Text(p.Album);
        var song = Page();
        Field(song, "Title", title);
        Field(song, "Subtitle", subtitle);
        Field(song, "Artist", artist);
        Field(song, "Album", album);

        // ---- Credits ----
        var music = Text(p.MusicAuthor); var words = Text(p.LyricsAuthor);
        var tab = Text(p.TabAuthor); var copyright = Text(p.Copyright);
        var credits = Page();
        Field(credits, "Music by", music, "Composer of the music.");
        Field(credits, "Words by", words, "Author of the lyrics.");
        Field(credits, "Tabbed by", tab, "Transcriber of this tab.");
        Field(credits, "Copyright", copyright);

        // ---- Music ----
        var tempo = Text(p.Tempo.ToString());
        var timeNum = Text(p.TimeSignatureNumerator.ToString()); timeNum.Width = 56;
        var timeDen = new ComboBox { Width = 70, Margin = new Thickness(6, 0, 0, 0) };
        foreach (var d in Denominators) timeDen.Items.Add(d.ToString());
        timeDen.SelectedItem = p.TimeSignatureDenominator.ToString();
        if (timeDen.SelectedIndex < 0) timeDen.SelectedIndex = 2;
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal };
        timeRow.Children.Add(timeNum);
        timeRow.Children.Add(new TextBlock { Text = "/", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        timeRow.Children.Add(timeDen);
        var key = new ComboBox { Width = 90 };
        foreach (var name in KeyNames) key.Items.Add(name);
        key.SelectedIndex = Math.Clamp(p.KeySignature + 7, 0, KeyNames.Length - 1);
        var minor = new CheckBox { Content = "Minor", IsChecked = p.KeySignatureMinor, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal };
        keyRow.Children.Add(key); keyRow.Children.Add(minor);
        var gray = new CheckBox { Content = "Grey out the inactive voice", IsChecked = p.GrayInactiveVoice, Margin = new Thickness(0, 12, 0, 0) };
        var musicPage = Page();
        Field(musicPage, "Tempo (BPM)", tempo, "Starting tempo, 20–400. Tempo changes inside the song are kept.");
        Field(musicPage, "Time signature", timeRow, "Applied from bar 1; later time-signature changes are kept.");
        Field(musicPage, "Key signature", keyRow, "Applied from bar 1; later key changes are kept.");
        musicPage.Children.Add(gray);

        // ---- Notes & lyrics ----
        var instructions = Text(p.Instructions, true); var notice = Text(p.Notice, true); var lyrics = Text(p.Lyrics, true);
        var notes = Page();
        Field(notes, "Instructions", instructions, "Performance notes shown with the score.");
        Field(notes, "Notice", notice);
        Field(notes, "Lyrics", lyrics);

        // ---- Summary (read-only) ----
        var summary = Page();
        var bars = p.Tracks.Count == 0 ? 0 : p.Tracks.Max(t => t.Measures.Count);
        var beatsPerBar = p.TimeSignatureNumerator * 4.0 / Math.Max(1, p.TimeSignatureDenominator);
        var seconds = p.Tempo > 0 ? bars * beatsPerBar * 60.0 / p.Tempo : 0;
        void Stat(string label, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var l = new TextBlock { Text = label }; l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var v = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(v, 1);
            row.Children.Add(l); row.Children.Add(v);
            summary.Children.Add(row);
        }
        Stat("Tracks", p.Tracks.Count.ToString());
        Stat("Bars", bars.ToString());
        Stat("Sections", p.Markers.Count.ToString());
        Stat("Approximate length", seconds > 0 ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss") : "—");
        var lengthNote = new TextBlock { Text = "Length assumes the starting tempo and time signature, without repeats.", FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        lengthNote.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        summary.Children.Add(lengthNote);

        var tabs = new TabControl { Margin = new Thickness(12, 12, 12, 0) };
        tabs.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        foreach (var (header, page) in new (string, FrameworkElement)[]
                 { ("Song", song), ("Credits", credits), ("Music", musicPage), ("Notes & lyrics", notes), ("Summary", summary) })
            tabs.Items.Add(new TabItem { Header = header, Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

        var error = new TextBlock { Foreground = Brushes.IndianRed, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var ok = new Button { Content = "OK", Width = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 88, IsCancel = true };
        var footer = new DockPanel { Margin = new Thickness(12), LastChildFill = false };
        DockPanel.SetDock(cancel, Dock.Right); DockPanel.SetDock(ok, Dock.Right); DockPanel.SetDock(error, Dock.Right);
        footer.Children.Add(cancel); footer.Children.Add(ok); footer.Children.Add(error);

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(tabs);
        w.Content = root;

        ProjectSettingsResult? result = null;
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(tempo.Text.Trim(), out var bpm) || bpm is < 20 or > 400)
            { error.Text = "Tempo must be a whole number from 20 to 400."; tabs.SelectedIndex = 2; tempo.Focus(); return; }
            if (!int.TryParse(timeNum.Text.Trim(), out var num) || num is < 1 or > 32)
            { error.Text = "Beats per bar must be from 1 to 32."; tabs.SelectedIndex = 2; timeNum.Focus(); return; }
            var den = int.Parse((string)timeDen.SelectedItem);
            result = new ProjectSettingsResult(
                title.Text.Trim(), subtitle.Text.Trim(), artist.Text.Trim(), album.Text.Trim(),
                music.Text.Trim(), words.Text.Trim(), tab.Text.Trim(), copyright.Text.Trim(),
                bpm, num, den, key.SelectedIndex - 7, minor.IsChecked == true, gray.IsChecked == true,
                instructions.Text, notice.Text, lyrics.Text);
            w.DialogResult = true;
        };

        List<string> PendingChanges()
        {
            var list = new List<string>();
            void Diff(string label, string before, string after)
            {
                if (!string.Equals(before ?? "", after, StringComparison.Ordinal)) list.Add($"{label}: {(string.IsNullOrEmpty(before) ? "(empty)" : before)} → {(string.IsNullOrEmpty(after) ? "(empty)" : after)}");
            }
            Diff("Title", p.Title, title.Text.Trim()); Diff("Subtitle", p.Subtitle, subtitle.Text.Trim());
            Diff("Artist", p.Artist, artist.Text.Trim()); Diff("Album", p.Album, album.Text.Trim());
            Diff("Music by", p.MusicAuthor, music.Text.Trim()); Diff("Words by", p.LyricsAuthor, words.Text.Trim());
            Diff("Tabbed by", p.TabAuthor, tab.Text.Trim()); Diff("Copyright", p.Copyright, copyright.Text.Trim());
            Diff("Tempo", p.Tempo.ToString(), tempo.Text.Trim());
            Diff("Time signature", $"{p.TimeSignatureNumerator}/{p.TimeSignatureDenominator}", $"{timeNum.Text.Trim()}/{timeDen.SelectedItem}");
            Diff("Key", KeyNames[Math.Clamp(p.KeySignature + 7, 0, KeyNames.Length - 1)] + (p.KeySignatureMinor ? " minor" : " major"),
                (key.SelectedItem as string ?? "") + (minor.IsChecked == true ? " minor" : " major"));
            if (p.GrayInactiveVoice != (gray.IsChecked == true)) list.Add($"Grey inactive voice: {(gray.IsChecked == true ? "On" : "Off")}");
            if ((p.Instructions ?? "") != instructions.Text) list.Add("Instructions edited");
            if ((p.Notice ?? "") != notice.Text) list.Add("Notice edited");
            if ((p.Lyrics ?? "") != lyrics.Text) list.Add("Lyrics edited");
            return list;
        }
        w.Closing += (_, e) =>
        {
            if (result is not null) return;
            var changes = PendingChanges();
            if (changes.Count == 0) return;
            if (!DiscardPrompt.Confirm(w, "Unsaved project settings", "Discard the changes made to the project settings?",
                changes, "Return to the project settings")) e.Cancel = true;

        };

        return DialogHost.ShowModal(w) == true ? result : null;
    }
}
