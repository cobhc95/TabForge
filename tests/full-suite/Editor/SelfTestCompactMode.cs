using System.Windows;
using TabForge.CompactMode;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Compact mode: no track rows, the master track lane at twice its height (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    /// <summary>On hides the rows and shows the double lane without changing the setting; off restores the rows; the command and its key exist.</summary>
    private static void TestCompactMode()
    {
        var song = new SongProject();
        var tracks = new TrackController();
        for (var i = 0; i < 3; i++) song.Tracks.Add(tracks.CreateTrack(song, TrackKind.Guitar));
        var panel = new ArrangementPanel();
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var window = new Window { Content = panel, Width = 1000, Height = 500 };
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            var mode = new CompactModeController(panel, () => null);
            var row = ArrangementPanel.RowHeightFor(song);
            var tops = Enumerable.Range(0, 3).Select(i => ArrangementPanel.RowTopOf(song, i)).ToArray();
            var height = panel.PreferredHeight();
            int Visible() => panel.TrackRowActualHeights.Count(h => h > 0);

            mode.Toggle();
            window.UpdateLayout(); PumpUi();
            Check("compact mode: on, no track row takes space", mode.IsOn && Visible() == 0 && Enumerable.Range(0, 3).All(i => ArrangementPanel.RowHeight(song, i) == 0) && panel.AddLaneRow is null, $"{Visible()}");
            Check("compact mode: the master lane shows at twice the row height", MasterLane.IsShown(song) && panel.TimelineForTest.MasterLaneLineCount == 3 && Math.Abs(MasterLane.OffsetOf(song) - 2 * row) < 0.01
                && panel.MasterLaneRow is { } lane && Math.Abs(lane.ActualHeight - 2 * row) < 0.5 && Math.Abs(ArrangementPanel.RowsHeight(song) - 2 * row) < 0.01, $"{MasterLane.OffsetOf(song)} {panel.MasterLaneRow?.ActualHeight}");
            Check("compact mode: the pane needs less height and the user's setting stays off", panel.PreferredHeight() < height && !panel.ShowMasterLane, $"{panel.PreferredHeight()} {height}");

            mode.Toggle();
            window.UpdateLayout(); PumpUi();
            Check("compact mode: off, three rows at their tops and no lane", !mode.IsOn && Visible() == 3 && Enumerable.Range(0, 3).All(i => ArrangementPanel.RowTopOf(song, i) == tops[i])
                && !MasterLane.IsShown(song) && panel.MasterLaneRow is null && panel.PreferredHeight() == height && panel.AddLaneRow is not null, $"{Visible()} {panel.PreferredHeight()} {height}");

            panel.ShowMasterLane = true;
            mode.Toggle(); mode.Toggle();
            window.UpdateLayout(); PumpUi();
            Check("compact mode: with the setting on, off leaves the lane at one row", panel.ShowMasterLane && MasterLane.IsShown(song) && Math.Abs(MasterLane.OffsetOf(song) - row) < 0.01
                && Math.Abs(ArrangementPanel.RowTopOf(song, 0) - row) < 0.01 && Visible() == 3);

            // The header strip: one pill per track, the selected track lit; the side panel is hidden while compact mode is on.
            var list = new System.Windows.Controls.ListBox { ItemsSource = song.Tracks, SelectedIndex = 0 };
            var strip = new CompactModeStrip();
            strip.Attach(list);
            var side = true;
            var withSide = new CompactModeController(panel, () => null, () => side, () => side = !side);
            withSide.Changed += () => strip.SetOn(withSide.IsOn);
            strip.CloseRequested += () => withSide.Set(false);
            string Lit() => string.Join(",", strip.Pills.Select((p, i) => p.IsChecked == true ? i : -1).Where(i => i >= 0));
            withSide.Toggle();
            Check("compact mode: the strip shows a pill per track with the selected track lit, and the side panel is hidden", strip.Visibility == Visibility.Visible && strip.Pills.Count == 3 && Lit() == "0" && !side, $"{strip.Pills.Count} [{Lit()}] {side}");
            strip.Pills[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("compact mode: a click on pill 3 selects track 3 and unlights the other", list.SelectedIndex == 2 && Lit() == "2", $"{list.SelectedIndex} [{Lit()}]");
            list.SelectedIndex = 1;
            Check("compact mode: a track selected elsewhere moves the highlight", Lit() == "1", Lit());
            strip.CloseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check("compact mode: the close button turns it off, the strip goes and the side panel is back", !withSide.IsOn && strip.Visibility == Visibility.Collapsed && side, $"{withSide.IsOn} {side}");
            side = false;
            withSide.Toggle(); withSide.Toggle();
            Check("compact mode: a side panel hidden before stays hidden after", !side && !withSide.IsOn);
            window.UpdateLayout(); PumpUi();

            // The menu row goes while the strip is the header; the strip's right-click menu stands in for it.
            var menuRow = new System.Windows.Controls.Border();
            var icons = new System.Windows.Controls.StackPanel();
            for (var i = 0; i < 6; i++)
            {
                if (i is 1 or 5) icons.Children.Add(new System.Windows.Shapes.Rectangle());
                icons.Children.Add(new System.Windows.Controls.Button { ToolTip = $"Icon {i}: what it does", Tag = i == 4 ? "on" : null, Content = new System.Windows.Shapes.Path { Data = System.Windows.Media.Geometry.Parse("M0,0 H10") } });
            }
            var clicked = 0;
            ((System.Windows.Controls.Button)icons.Children[0]).Click += (_, _) => clicked++;
            var mainMenu = new System.Windows.Controls.Menu();
            foreach (var name in "File Edit Bar Track Note Effects Sections Tools Sound View Options Help".Split(' '))
            {
                var top = new System.Windows.Controls.MenuItem { Header = name };
                for (var i = 0; i <= name.Length; i++) top.Items.Add(new System.Windows.Controls.MenuItem { Header = name + i });
                mainMenu.Items.Add(top);
            }
            var counts = mainMenu.Items.OfType<System.Windows.Controls.MenuItem>().Select(m => m.Items.Count).ToArray();
            strip.AttachMenuRow(menuRow, icons, mainMenu);
            withSide.Toggle();
            Check("compact mode: on, the menu row is collapsed and the strip shows", menuRow.Visibility == Visibility.Collapsed && strip.Visibility == Visibility.Visible);
            var rows = strip.OpenMenu().Items.OfType<System.Windows.Controls.MenuItem>().ToList();
            Check("compact mode: the strip's menu has the six icon rows with icons, then the twelve menus with their items", rows.Count == 18 && rows.Take(6).All(r => r.Icon is System.Windows.Shapes.Path)
                && (string)rows[0].Header == "Icon 0" && rows[4].IsChecked && !rows[0].IsChecked && rows.Skip(6).Select(r => r.Items.Count).SequenceEqual(counts)
                && strip.ContextMenu.Items.OfType<System.Windows.Controls.Separator>().Count() == 3, $"{rows.Count} {strip.ContextMenu.Items.Count}");
            rows[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check("compact mode: an icon row clicks its button", clicked == 1, $"{clicked}");
            withSide.Toggle();
            Check("compact mode: off, the menu row is back and every menu has its items in order", menuRow.Visibility == Visibility.Visible
                && mainMenu.Items.OfType<System.Windows.Controls.MenuItem>().Select(m => m.Items.Count).SequenceEqual(counts)
                && mainMenu.Items.OfType<System.Windows.Controls.MenuItem>().All(m => m.Items.OfType<System.Windows.Controls.MenuItem>().Select(i => (string)i.Header).SequenceEqual(Enumerable.Range(0, m.Items.Count).Select(i => (string)m.Header + i))));

            // The Band view's header stands in for the strip: the menu row stays hidden and the same rows are offered there.
            var bandView = new TabForge.Views.Band.BandView(new TabForge.Views.Band.BandLayoutState());
            var bandMenu = new TabForge.Views.Band.BandLayoutMenu(bandView.LayoutButton, () => new BandSettings(), _ => { });
            var bandOn = false;
            var both = new CompactModeStrip { SuppressPopups = true };
            both.Attach(list, () => bandOn);
            both.AttachMenuRow(menuRow, icons, mainMenu);
            both.AttachBandHeader(bandView);
            System.Windows.Controls.ContextMenu? shown = null;
            var captureBefore = MainWindow.ContextMenuCapture;
            MainWindow.ContextMenuCapture = m => shown = m;
            try
            {
                both.SetOn(true);
                both.MenuButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check("compact mode: the caption's chevron fills the same menu as a right-click (six icon rows, twelve menus)", both.Visibility == Visibility.Visible
                    && both.ContextMenu!.Items.OfType<System.Windows.Controls.MenuItem>().Count() == 18 && ReferenceEquals(both.OpenMenu(), both.ContextMenu));
                both.CloseMenu();
                bandOn = true; both.Refresh();
                Check("compact mode: with the Band view on, the strip goes and the menu row stays hidden", both.Visibility == Visibility.Collapsed && menuRow.Visibility == Visibility.Collapsed);
                bandView.LayoutButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                var dropRows = shown?.Items.OfType<System.Windows.Controls.MenuItem>().ToList() ?? new();
                Check("compact mode: the Band roll-down has its three layout rows and Reset view, then the icon rows and the twelve menus", dropRows.Count == 4 + 18 && dropRows.Skip(4).Take(6).All(r => r.Icon is not null)
                    && dropRows.Skip(10).Select(r => r.Items.Count).SequenceEqual(counts), $"{dropRows.Count}");
                both.CloseMenu();
                Check("compact mode: the Band header's right-click menu is the compact menu", ReferenceEquals(bandView.Header.ContextMenu, both.ContextMenu));
                both.SetOn(false);
                shown = null;
                bandView.LayoutButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check("compact mode: off, the menu row is back, the Band roll-down has only its own four rows and every menu has its items", menuRow.Visibility == Visibility.Visible
                    && shown?.Items.OfType<System.Windows.Controls.MenuItem>().Count() == 4 && mainMenu.Items.OfType<System.Windows.Controls.MenuItem>().Select(m => m.Items.Count).SequenceEqual(counts), $"{shown?.Items.Count}");
            }
            finally { MainWindow.ContextMenuCapture = captureBefore; }

            // The left column: the toolbar's own buttons in three rows of five; the third is tuner, volume knob, live input, zoom in, zoom out.
            var bar = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            for (var i = 0; i < 8; i++) { if (i == 5) bar.Children.Add(new System.Windows.Controls.Border { Width = 1 }); bar.Children.Add(new System.Windows.Controls.Button { Width = 34, Height = 34, Tag = i }); }
            panel.SetPlaybar(new System.Windows.Controls.Border { Child = bar });
            window.UpdateLayout(); PumpUi();
            var barBefore = bar.Children.Cast<UIElement>().ToList();
            var widthBefore = panel.ColumnDefinitions[0].Width.Value;
            var knobParent = panel.MasterVolumeKnob.Parent as System.Windows.Controls.Panel;
            var toolbarBefore = knobParent!.Children.Cast<UIElement>().ToList();
            var live = toolbarBefore.OfType<TabForge.LiveInput.LiveInputButton>().Single();
            panel.ShowMasterLane = false;
            mode.Toggle();
            window.UpdateLayout(); PumpUi();
            var column = panel.CompactColumn!;
            var cells = column.Buttons;
            Check("compact mode: the left column has the 15 live buttons (live input between the knob and the zoom buttons), without the header row", cells.Count == 15 && ReferenceEquals(cells[12], live) && ReferenceEquals(cells[10], panel.TuningButton) && ReferenceEquals(cells[11], panel.MasterVolumeKnob)
                && barBefore.OfType<System.Windows.Controls.Button>().SequenceEqual(cells.Take(8)) && cells.Contains(panel.MasterVolumeKnob) && cells.Contains(panel.TuningButton)
                && cells.All(c => c.IsVisible), $"{cells.Count}");
            var cellRows = cells.Select(c => Math.Round(c.TranslatePoint(new Point(0, 0), panel).Y / CompactColumnLayout.RowHeight)).ToList();
            Check("compact mode: the buttons sit in three rows of five and the column is narrower", cellRows.GroupBy(y => y).Select(g => g.Count()).SequenceEqual(new[] { 5, 5, 5 })
                && panel.ColumnDefinitions[0].Width.Value < widthBefore, $"{string.Join(",", cellRows)} {panel.ColumnDefinitions[0].Width.Value}");
            mode.Toggle();
            window.UpdateLayout(); PumpUi();
            Check("compact mode: off, the buttons are back in their order and parents, the column has its width", bar.Children.Cast<UIElement>().SequenceEqual(barBefore)
                && knobParent.Children.Cast<UIElement>().SequenceEqual(toolbarBefore) && panel.ColumnDefinitions[0].Width.Value == widthBefore && live.IsVisible
                , $"{panel.ColumnDefinitions[0].Width.Value} {widthBefore}");

            var action = HotkeyCatalog.All.FirstOrDefault(a => a.Id == CompactModeFeatureModule.CommandId);
            Check("compact mode: the command is in the catalogue with a default key", action is not null && HotkeyCatalog.BuildMap(new AppSettings().Hotkeys).Values.Contains(CompactModeFeatureModule.CommandId));
        }
        finally { window.Close(); }
    }
}
