using System.Linq;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Documents;
using TabForge.Presets;
using Avalonia.Headless.XUnit;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the compact mode strip's checks (shown only while on, one pill per track, exactly the selected one lit, a click selects).
// Does not own: the compact mode command (MainWindowMenuTests), the row geometry (TimelineViewTests).
// Tests: itself.
public class CompactTrackStripTests
{
    [AvaloniaFact]
    public void ShowsOnePillPerTrackWithTheSelectedOneLit()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create(), TrackIndex = 1 };
        var strip = new CompactTrackStrip();
        strip.IsLit = index => index == song.TrackIndex;
        strip.PillClicked = index => song.TrackIndex = index;
        strip.SetOn(true, song);
        Assert.True(strip.IsVisible);
        Assert.Equal(song.Project.Tracks.Count, strip.Pills.Count);
        Assert.True(strip.Pills[1].IsChecked);
        Assert.False(strip.Pills[0].IsChecked);
        strip.Pills[0].RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        Assert.Equal(0, song.TrackIndex);
        Assert.True(strip.Pills[0].IsChecked);
        strip.SetOn(false, song);
        Assert.False(strip.IsVisible);
        Assert.Empty(strip.Pills);
    }

    [AvaloniaFact]
    public void PillsFollowARenamedTrackAfterTheEditEnds()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var strip = new CompactTrackStrip();
        strip.SetOn(true, song);
        song.Project.Tracks[0].Name = "Renamed";
        song.Project.MarkTimelineChanged();
        Assert.Equal("Renamed", strip.Pills[0].TrackName);
        song.Project.Tracks.RemoveAt(0);
        song.Project.MarkTimelineChanged();
        Assert.Equal(song.Project.Tracks.Count, strip.Pills.Count);
    }

    [AvaloniaFact]
    public void CaptionOpensTheStandInMenuThatLendsTheMainMenusAndHandsThemBack()
    {
        var file = new global::Avalonia.Controls.MenuItem { Header = "File" };
        file.Items.Add(new global::Avalonia.Controls.MenuItem { Header = "Open" });
        var row = new global::Avalonia.Controls.Menu();
        row.Items.Add(file);
        var window = new global::Avalonia.Controls.Window { Content = row };
        var menu = new CompactModeMenu(window).Build();
        var stand = Assert.IsType<global::Avalonia.Controls.MenuItem>(Assert.Single(menu.Items));
        Assert.Equal("File", stand.Header);
        Assert.Single(stand.Items);
        Assert.Empty(file.Items);
        var strip = new CompactTrackStrip();
        global::Avalonia.Controls.Control? anchor = null;
        strip.MenuRequested = (c, _) => anchor = c;
        strip.MenuButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        Assert.Same(strip.MenuButton, anchor);
    }

    [AvaloniaFact]
    public void PillShowsTheFullCapoBadgeAndRebuildsWhenItChanges()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        song.Project.Tracks[0].Capo = 5;
        var strip = new CompactTrackStrip();
        strip.SetOn(true, song);
        var pill = strip.Pills[0];
        Assert.True(pill.Shows(song.Project.Tracks[0].Name, song.Project.Tracks[0].ColorHex, 5));
        static bool HasBadge(TrackPillButton p) => ((global::Avalonia.Controls.StackPanel)p.Content!).Children
            .OfType<global::Avalonia.Controls.TextBlock>().Any(t => t.Text is not null && t.Text.Contains("Capo 5"));
        Assert.True(HasBadge(pill));
        song.Project.Tracks[0].Capo = 0;
        song.Project.MarkTimelineChanged();
        Assert.False(HasBadge(strip.Pills[0]));
    }

    [Fact]
    public void RegularStripNameCarriesTheCapoBadge()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        Assert.Equal(track.Name, global::TabForge.Avalonia.Platform.TrackHeaderColumn.DisplayName(track));
        track.Capo = 5;
        Assert.Equal($"{track.Name} · Capo 5", global::TabForge.Avalonia.Platform.TrackHeaderColumn.DisplayName(track));
    }

    [AvaloniaFact]
    public void PillHasItsTrackColourDotAndTheTrackPillLook()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var strip = new CompactTrackStrip();
        strip.SetOn(true, song);
        var pill = strip.Pills[0];
        Assert.Contains(TrackPillButton.LookClass, pill.Classes);
        Assert.Equal(global::Avalonia.Media.Color.Parse(song.Project.Tracks[0].ColorHex), Assert.IsType<global::Avalonia.Media.SolidColorBrush>(pill.Dot.Fill).Color);
    }

    [AvaloniaFact]
    public void StandInMenuStartsWithTheIconRowThenTheMainMenus()
    {
        var file = new global::Avalonia.Controls.MenuItem { Header = "File" };
        file.Items.Add(new global::Avalonia.Controls.MenuItem { Header = "Open" });
        var help = new global::Avalonia.Controls.MenuItem { Header = "Help" };
        var row = new global::Avalonia.Controls.Menu();
        row.Items.Add(file);
        row.Items.Add(help);
        var window = new global::Avalonia.Controls.Window();
        var commands = new MainWindowCommands(window, () => { });
        var feature = new TabForge.Avalonia.Features.LeftStrip.LeftStripFeature(commands, null);
        var menuRow = new global::Avalonia.Controls.Border { Child = new global::Avalonia.Controls.DockPanel() };
        var modeBar = new TabForge.Avalonia.Platform.Controls.CompactTrackStrip();
        feature.Attach(menuRow, modeBar);
        window.Content = new global::Avalonia.Controls.DockPanel
        {
            Children = { TabForge.Avalonia.Views.MenuRowToggles.Build(commands), row, feature.Left, modeBar }
        };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var menu = new CompactModeMenu(window).Build();
        var rows = menu.Items.OfType<global::Avalonia.Controls.MenuItem>().Where(item => item.Icon is not null).ToList();
        Assert.Equal(TabForge.Avalonia.Views.MenuRowToggles.Items.Count(toggle => toggle is not null), rows.Count);
        Assert.Equal("Show / hide the side panel", rows[0].Header);
        var tops = menu.Items.OfType<global::Avalonia.Controls.MenuItem>().Where(item => item.Icon is null).Select(item => item.Header).ToList();
        Assert.Equal(new object[] { "File", "Help" }, tops);
        Assert.IsType<global::Avalonia.Controls.Separator>(menu.Items[^3]);
    }

    [AvaloniaFact]
    public void StandInMenuRestoreHandsTheItemsBack()
    {
        var file = new global::Avalonia.Controls.MenuItem { Header = "File" };
        file.Items.Add(new global::Avalonia.Controls.MenuItem { Header = "Open" });
        var row = new global::Avalonia.Controls.Menu();
        row.Items.Add(file);
        var compact = new CompactModeMenu(new global::Avalonia.Controls.Window { Content = row });
        compact.Build();
        compact.Restore();
        Assert.Single(file.Items);
    }

    [AvaloniaFact]
    public void LentEntryRunsItsCommandWhenClicked()
    {
        var ran = false;
        var open = new global::Avalonia.Controls.MenuItem { Header = "Open" };
        open.Click += (_, _) => ran = true;
        var file = new global::Avalonia.Controls.MenuItem { Header = "File" };
        file.Items.Add(open);
        var row = new global::Avalonia.Controls.Menu();
        row.Items.Add(file);
        var menu = new CompactModeMenu(new global::Avalonia.Controls.Window { Content = row }).Build();
        var stand = Assert.IsType<global::Avalonia.Controls.MenuItem>(Assert.Single(menu.Items));
        var entry = Assert.IsType<global::Avalonia.Controls.MenuItem>(Assert.Single(stand.Items));
        entry.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.MenuItem.ClickEvent));
        Assert.True(ran);
    }
}
