using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the Score information dialog's window: ten labelled text fields in a scroll view, OK and Cancel, Esc closes.
// Does not own: the texts' meaning and the write to the song (ScoreInfoValues), the edit that applies them (MainWindowBarMenu).
// Tests: TabForge.Avalonia.Tests.ScoreInfoWindowTests.
/// <summary>The Bar menu's Score information dialog. OK closes with the edited texts; Cancel, Esc and the close box close with null.</summary>
public sealed class ScoreInfoWindow : Window
{
    private const double WindowWidth = 480;
    private const double WindowHeight = 560;
    private const double ButtonWidth = 80;
    private const double PageMargin = 10;
    private const double ButtonRowTopMargin = 8;
    private const double LabelTopMargin = 6;
    private const double LabelBottomMargin = 2;
    private const double SingleLineMinHeight = 26;
    private const double LongTextMinHeight = 44;
    private const double LongTextMaxHeight = 140;

    private readonly TextBox _title = SingleLine(), _subtitle = SingleLine(), _artist = SingleLine(), _album = SingleLine();
    private readonly TextBox _musicAuthor = SingleLine(), _lyricsAuthor = SingleLine(), _copyright = SingleLine(), _tabAuthor = SingleLine();
    private readonly TextBox _instructions = LongText(), _notice = LongText();

    /// <summary>Shows the texts of a song for editing.</summary>
    public ScoreInfoWindow(ScoreInfoValues values)
    {
        Title = "Score information";
        Width = WindowWidth;
        Height = WindowHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        _title.Text = values.Title; _subtitle.Text = values.Subtitle; _artist.Text = values.Artist; _album.Text = values.Album;
        _musicAuthor.Text = values.MusicAuthor; _lyricsAuthor.Text = values.LyricsAuthor; _copyright.Text = values.Copyright; _tabAuthor.Text = values.TabAuthor;
        _instructions.Text = values.Instructions; _notice.Text = values.Notice;

        var page = new StackPanel { Margin = new Thickness(PageMargin) };
        Field(page, "Title", _title);
        Field(page, "Subtitle", _subtitle);
        Field(page, "Artist", _artist);
        Field(page, "Album", _album);
        Field(page, "Music author", _musicAuthor);
        Field(page, "Lyrics author", _lyricsAuthor);
        Field(page, "Copyright", _copyright);
        Field(page, "Tab author", _tabAuthor);
        Field(page, "Instructions", _instructions);
        Field(page, "Notice", _notice);

        var ok = new Button { Content = "OK", Width = ButtonWidth, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = ButtonWidth, IsCancel = true };
        ok.Click += (_, _) => Close(ReadValues());
        cancel.Click += (_, _) => Close(null);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, ButtonRowTopMargin, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        page.Children.Add(buttons);

        Content = new ScrollViewer { Content = page };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(null); };
    }

    private ScoreInfoValues ReadValues() => new(
        _title.Text ?? "", _subtitle.Text ?? "", _artist.Text ?? "", _album.Text ?? "",
        _musicAuthor.Text ?? "", _lyricsAuthor.Text ?? "", _copyright.Text ?? "", _tabAuthor.Text ?? "",
        _instructions.Text ?? "", _notice.Text ?? "");

    private static void Field(Panel page, string label, Control editor)
    {
        page.Children.Add(new TextBlock
        {
            Text = label, FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, LabelTopMargin, 0, LabelBottomMargin),
        });
        page.Children.Add(editor);
    }

    private static TextBox SingleLine() => new() { MinHeight = SingleLineMinHeight, VerticalContentAlignment = VerticalAlignment.Center };

    private static TextBox LongText()
    {
        var box = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = LongTextMinHeight, MaxHeight = LongTextMaxHeight, VerticalContentAlignment = VerticalAlignment.Top,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        return box;
    }
}
