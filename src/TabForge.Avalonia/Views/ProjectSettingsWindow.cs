using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Per-song settings: the score information credits, the song-level musical defaults, notes and lyrics, and a read-only summary.
/// OK closes with the edited values; Cancel, Esc and the close box close with null, and ask first when the draft has changes.
/// Owns: the window's controls, their order and texts, and the discard question on close. Does not own: the checks, the edited result,
/// the pending changes and the summary figures (ProjectSettingsPresenter).
/// Tests: TabForge.Avalonia.Tests.ProjectSettingsWindowTests.
/// </summary>
public sealed class ProjectSettingsWindow : Window
{
    /// <summary>The field labels' column: the label on the left, the editor in the column to its right.</summary>
    private const double LabelColumnWidth = ChromeMetrics.SongInfoLabelColumn;
    /// <summary>The space between two field rows.</summary>
    private const double RowGap = 10;
    /// <summary>The space between a page's edge and its fields.</summary>
    private const double PagePadding = 20;
    /// <summary>The OK / Cancel footer's horizontal and vertical padding.</summary>
    private const double FooterPaddingVertical = 10;
    /// <summary>The Summary card's padding and corner radius.</summary>
    private const double SummaryCardPaddingHorizontal = 14, SummaryCardPaddingVertical = 10, SummaryCardCornerRadius = 6;
    /// <summary>The Summary card's label column (wider than the field labels, so "Approximate length" fits).</summary>
    private const double StatLabelColumn = 170;

    private readonly ProjectSettingsPresenter _presenter;
    private readonly TextBox _title = SingleLine(), _subtitle = SingleLine(), _artist = SingleLine(), _album = SingleLine();
    private readonly TextBox _music = SingleLine(), _words = SingleLine(), _tab = SingleLine(), _copyright = SingleLine();
    private readonly TextBox _tempo = SingleLine(), _beatsPerBar = new() { Width = 56 };
    private readonly ComboBox _denominator = new() { Width = 70, Margin = new Thickness(6, 0, 0, 0) };
    private readonly ComboBox _key = new() { Width = 90 };
    private readonly CheckBox _minor = new() { Content = "Minor", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _gray = new() { Content = "Grey out the inactive voice" };
    private readonly TextBox _instructions = MultiLine(), _notice = MultiLine(), _lyrics = MultiLine();
    private readonly TabControl _tabs = new();
    private readonly TextBlock _error = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
    private bool _mayClose;
    private bool _asking;

    /// <summary>Shows the settings of a song: <paramref name="original"/> is what the song holds as opened, the counts feed the summary.</summary>
    public ProjectSettingsWindow(ProjectSettingsResult original, int trackCount, int barCount, int sectionCount)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _presenter = new ProjectSettingsPresenter(original, trackCount, barCount, sectionCount);
        Title = "Project settings";
        Width = ChromeMetrics.SongInfoWindowWidth;
        Height = ChromeMetrics.SongInfoWindowHeight;
        MinWidth = ChromeMetrics.SongInfoWindowMinWidth;
        MinHeight = ChromeMetrics.SongInfoWindowMinHeight;
        ShowInTaskbar = false;
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBrush");

        FillEditors(original);
        _tabs.Items.Add(TabPage("Song", Song()));
        _tabs.Items.Add(TabPage("Credits", Credits()));
        _tabs.Items.Add(TabPage("Music", Music()));
        _tabs.Items.Add(TabPage("Notes & lyrics", NotesAndLyrics()));
        _tabs.Items.Add(TabPage("Summary", Summary()));

        var cancel = new Button { Content = "Cancel", Width = 88, IsCancel = true };
        var ok = new Button { Content = "OK", Width = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close();
        ok.Click += (_, _) => Accept();
        _error[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Danger");
        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(ok, Dock.Right);
        DockPanel.SetDock(_error, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        buttons.Children.Add(_error);
        var footer = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(PagePadding, FooterPaddingVertical), Child = buttons,
        };
        footer[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        footer[!Border.BackgroundProperty] = new DynamicResourceExtension("WindowBrush");
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(_tabs);
        Content = root;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Opened += (_, _) => _title.Focus();
        Closing += OnClosing;
    }

    private void FillEditors(ProjectSettingsResult o)
    {
        _title.Text = o.Title; _subtitle.Text = o.Subtitle; _artist.Text = o.Artist; _album.Text = o.Album;
        _music.Text = o.MusicAuthor; _words.Text = o.LyricsAuthor; _tab.Text = o.TabAuthor; _copyright.Text = o.Copyright;
        _tempo.Text = o.Tempo.ToString();
        _beatsPerBar.Text = o.TimeSigNum.ToString();
        foreach (var d in SignatureChoices.Denominators) _denominator.Items.Add(d.ToString());
        _denominator.SelectedIndex = SignatureChoices.Denominators.ToList().IndexOf(o.TimeSigDenom);
        if (_denominator.SelectedIndex < 0) _denominator.SelectedIndex = 2;
        foreach (var name in SignatureChoices.KeyNames) _key.Items.Add(name);
        _key.SelectedIndex = ProjectSettingsPresenter.KeyIndexOf(o.KeySignature);
        _minor.IsChecked = o.KeyMinor;
        _gray.IsChecked = o.GrayInactiveVoice;
        _instructions.Text = o.Instructions; _notice.Text = o.Notice; _lyrics.Text = o.Lyrics;
    }

    private StackPanel Song()
    {
        var page = Page();
        Field(page, "Title", _title);
        Field(page, "Subtitle", _subtitle);
        Field(page, "Artist", _artist);
        Field(page, "Album", _album);
        return page;
    }

    private StackPanel Credits()
    {
        var page = Page();
        Field(page, "Music by", _music, "Composer of the music.");
        Field(page, "Words by", _words, "Author of the lyrics.");
        Field(page, "Tabbed by", _tab, "Transcriber of this tab.");
        Field(page, "Copyright", _copyright);
        return page;
    }

    private StackPanel Music()
    {
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal };
        timeRow.Children.Add(_beatsPerBar);
        timeRow.Children.Add(new TextBlock { Text = "/", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        timeRow.Children.Add(_denominator);
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal };
        keyRow.Children.Add(_key);
        keyRow.Children.Add(_minor);
        var page = Page();
        Field(page, "Tempo (BPM)", _tempo, "Starting tempo, 20–400. Tempo changes inside the song are kept.");
        Field(page, "Time signature", timeRow, "Applied from bar 1; later time-signature changes are kept.");
        Field(page, "Key signature", keyRow, "Applied from bar 1; later key changes are kept.");
        Field(page, null, _gray);
        return page;
    }

    private StackPanel NotesAndLyrics()
    {
        var page = Page();
        Field(page, "Instructions", _instructions, "Performance notes shown with the score.");
        Field(page, "Notice", _notice);
        Field(page, "Lyrics", _lyrics);
        return page;
    }

    private StackPanel Summary()
    {
        var page = Page();
        var stats = new StackPanel();
        Stat(stats, "Tracks", _presenter.TrackCount.ToString());
        Stat(stats, "Bars", _presenter.BarCount.ToString());
        Stat(stats, "Sections", _presenter.SectionCount.ToString());
        Stat(stats, "Approximate length", _presenter.LengthText);
        var card = new Border
        {
            CornerRadius = new CornerRadius(SummaryCardCornerRadius), BorderThickness = new Thickness(1),
            Padding = new Thickness(SummaryCardPaddingHorizontal, SummaryCardPaddingVertical), Child = stats,
        };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        page.Children.Add(card);
        page.Children.Add(Hint("Length assumes the starting tempo and time signature, without repeats.", new Thickness(0, 8, 0, 0)));
        return page;
    }

    private static StackPanel Page() => new() { Margin = new Thickness(PagePadding) };

    private static TabItem TabPage(string header, Control page) => new()
    {
        Header = header,
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
    };

    /// <summary>A field row: the label (none for a row without one) in the label column, the editor and its hint in the column to the right.</summary>
    private static void Field(Panel page, string? label, Control editor, string? hint = null)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, RowGap), ColumnDefinitions = new ColumnDefinitions($"{LabelColumnWidth},*") };
        if (label is not null)
        {
            row.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 5, 0, 0) });
        }
        var editorColumn = new StackPanel();
        editorColumn.Children.Add(editor);
        if (hint is not null) editorColumn.Children.Add(Hint(hint, new Thickness(0, 2, 0, 0)));
        Grid.SetColumn(editorColumn, 1);
        row.Children.Add(editorColumn);
        page.Children.Add(row);
    }

    private static TextBlock Hint(string text, Thickness margin)
    {
        var note = new TextBlock { Text = text, FontSize = 11.5, Margin = margin, TextWrapping = TextWrapping.Wrap };
        note[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return note;
    }

    private static void Stat(Panel page, string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5), ColumnDefinitions = new ColumnDefinitions($"{StatLabelColumn},*") };
        var name = new TextBlock { Text = label };
        name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        var figure = new TextBlock { Text = value, FontWeight = FontWeight.SemiBold };
        Grid.SetColumn(figure, 1);
        row.Children.Add(name);
        row.Children.Add(figure);
        page.Children.Add(row);
    }

    private static TextBox SingleLine() => new() { MinHeight = 26, VerticalContentAlignment = VerticalAlignment.Center };

    private static TextBox MultiLine()
    {
        var box = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 110, MaxHeight = 320, VerticalContentAlignment = VerticalAlignment.Top,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        return box;
    }

    private ProjectSettingsDraft ReadDraft() => new(
        _title.Text ?? "", _subtitle.Text ?? "", _artist.Text ?? "", _album.Text ?? "",
        _music.Text ?? "", _words.Text ?? "", _tab.Text ?? "", _copyright.Text ?? "",
        _tempo.Text ?? "", _beatsPerBar.Text ?? "", _denominator.SelectedIndex, _key.SelectedIndex, _minor.IsChecked == true, _gray.IsChecked == true,
        _instructions.Text ?? "", _notice.Text ?? "", _lyrics.Text ?? "");

    private void Accept()
    {
        var draft = ReadDraft();
        var problem = _presenter.FindProblem(draft);
        if (problem is not null)
        {
            _error.Text = problem.Message;
            _tabs.SelectedIndex = 2;
            (problem.Field == ProjectSettingsField.Tempo ? _tempo : _beatsPerBar).Focus();
            return;
        }
        _mayClose = true;
        Close(_presenter.Build(draft));
    }

    /// <summary>Closing with changes asks first; the window stays open unless the person discards them.</summary>
    // async void: started from the Closing event, which cannot wait; an exception from the question surfaces on the UI thread.
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_mayClose) return;
        var changes = _presenter.PendingChanges(ReadDraft());
        if (changes.Count == 0) return;
        e.Cancel = true;
        if (_asking) return;   // a second Close while the question is open does not open a second one
        _asking = true;
        PromptButton? answer;
        try
        {
            answer = await DialogLauncher.AskFrom<SavePromptWindow, PromptButton?>(this, () => new SavePromptWindow(
                new SavePrompt("Unsaved project settings", "Discard the changes made to the project settings?", "Close without applying", "Return to the project settings"),
                showCancel: false, details: changes, rememberText: "Don't ask me again"), window => window.Result);
        }
        finally { _asking = false; }
        if (answer != PromptButton.Yes) return;
        _mayClose = true;
        Close(null);
    }
}
