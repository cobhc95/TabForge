using Avalonia.Controls;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Song stats (Tools > Song stats…): the song's title and file, and counts of tracks, bars, notes and sections, read when the window opens.
/// Owns: the window's wiring and the Close button. Does not own: the texts (SongStatsPresenter), the layout and colours (SongStatsWindow.axaml,
/// palette resources), the score or the song file. The preview has no song file: it shows the demo song (DemoSongFactory) as unsaved.
/// Tests: TabForge.Avalonia.Tests.SongStatsWindowTests.
/// </summary>
public partial class SongStatsWindow : Window
{
    /// <summary>For the XAML loader and designer only: the app passes the open song and its file.</summary>
    public SongStatsWindow() : this(DemoSongFactory.Create(), null) { }

    public SongStatsWindow(SongProject project, string? path)
    {
        InitializeComponent();
        Title = "Song stats";
        CloseButton.Content = "Close";
        CloseButton.Click += (_, _) => Close();
        HeadingText.Text = SongStatsPresenter.Heading(project, path);
        CountsText.Text = SongStatsPresenter.CountsLine(project);
    }
}
