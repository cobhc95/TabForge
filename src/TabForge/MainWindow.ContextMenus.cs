using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TabForge.Controllers;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// MainWindow, score right-click menu.
public partial class MainWindow
{
    // ---------- score context menu (right click) ----------

    private void ShowScoreContextMenu(Point position)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource(typeof(ContextMenu)),
            Background = (Brush)FindResource("Panel2Brush"),
            Foreground = (Brush)FindResource("TextBrush")
        };

        MenuItem Item(string header, RoutedEventHandler handler, string? gesture = null)
        {
            var mi = new MenuItem
            {
                Header = header,
                Style = (Style)FindResource(typeof(MenuItem))
            };
            if (gesture is not null) mi.InputGestureText = gesture;
            mi.Click += handler;
            return mi;
        }
        void Add(string header, RoutedEventHandler handler, string? gesture = null) => menu.Items.Add(Item(header, handler, gesture));
        void Sep() => menu.Items.Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });

        var scoreLayout = new MenuItem
        {
            Header = "Score layout",
            Style = (Style)FindResource(typeof(MenuItem))
        };
        var pageLayoutItem = Item("Page (paper)", (_, _) => SetContinuousScoreView(false));
        pageLayoutItem.IsCheckable = true;
        pageLayoutItem.IsChecked = !Editor.CenterSystems;
        var continuousLayoutItem = Item("Continuous (seamless)", (_, _) => SetContinuousScoreView(true));
        continuousLayoutItem.IsCheckable = true;
        continuousLayoutItem.IsChecked = Editor.CenterSystems;
        scoreLayout.Items.Add(pageLayoutItem);
        scoreLayout.Items.Add(continuousLayoutItem);
        menu.Items.Add(scoreLayout);
        // Second, independent choice: wrap into lines going down, or one line going right.
        var scrollDirection = new MenuItem { Header = "Score scrolling", Style = (Style)FindResource(typeof(MenuItem)) };
        var verticalItem = Item("Vertical (lines wrap, scroll down)", (_, _) => SetHorizontalScoreView(false));
        verticalItem.IsCheckable = true;
        verticalItem.IsChecked = !Editor.HorizontalScroll;
        var horizontalItem = Item("Horizontal (one line, scroll right)", (_, _) => SetHorizontalScoreView(true));
        horizontalItem.IsCheckable = true;
        horizontalItem.IsChecked = Editor.HorizontalScroll;
        scrollDirection.Items.Add(verticalItem);
        scrollDirection.Items.Add(horizontalItem);
        menu.Items.Add(scrollDirection);
        var followStyle = new MenuItem { Header = "Follow playback", Style = (Style)FindResource(typeof(MenuItem)) };
        var instantItem = Item("Page turn (jump half a page / next line)", (_, _) => SetSmoothFollow(false));
        instantItem.IsCheckable = true;
        instantItem.IsChecked = !_follow.Continuous;
        var smoothItem = Item("Smooth page turn (same turns, glided)", (_, _) => SetSmoothFollow(true));
        smoothItem.IsCheckable = true;
        smoothItem.IsChecked = _follow.Continuous;
        followStyle.Items.Add(instantItem);
        followStyle.Items.Add(smoothItem);
        menu.Items.Add(followStyle);
        var scorePage = new MenuItem
        {
            Header = "Score page",
            Style = (Style)FindResource(typeof(MenuItem))
        };
        var darkPageItem = Item("Dark", (_, _) => SetPaper(dark: true));
        darkPageItem.IsCheckable = true;
        darkPageItem.IsChecked = Editor.DarkPaper;
        var lightPageItem = Item("Light", (_, _) => SetPaper(dark: false));
        lightPageItem.IsCheckable = true;
        lightPageItem.IsChecked = !Editor.DarkPaper;
        scorePage.Items.Add(darkPageItem);
        scorePage.Items.Add(lightPageItem);
        menu.Items.Add(scorePage);
        Sep();
        Add("Zoom in", (_, _) => ZoomBy(1));
        Add("Zoom out", (_, _) => ZoomBy(-1));
        Add("Fit width", (_, _) => ApplyZoomText("Fit width"));
        Add("Playback and glow settings…", (_, _) => Prefs_Click(this, new RoutedEventArgs()));
        var playbackStyle = new MenuItem
        {
            Header = "Playback appearance",
            Style = (Style)FindResource(typeof(MenuItem))
        };
        playbackStyle.Items.Add(Item("Text & fonts…", (_, _) => ShowScoreTextStyleWindow()));
        playbackStyle.Items.Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });
        playbackStyle.Items.Add(Item("Playback line colour…", (_, _) => ChoosePlaybackColour(
            "Playback line colour", Playhead.CurrentColor, color => Playhead.SetColor(color))));
        playbackStyle.Items.Add(Item("Duration glow colour…", (_, _) => ChoosePlaybackColour(
            "Duration glow colour", Editor.DurationGlowColor, color =>
            {
                Editor.DurationGlowColor = color;
                Editor.InvalidateVisual();
                Playhead.SetDurationStyle(color, Editor.DurationGlowOpacity, _settings.Follow.DurationTintEnabled);
            })));
        var glowIntensity = new MenuItem
        {
            Header = $"Duration glow intensity ({Math.Round(Editor.DurationGlowOpacity * 100):0}%)",
            Style = (Style)FindResource(typeof(MenuItem))
        };
        foreach (var percent in PlaybackGlowIntensity.PresetPercentages)
        {
            var intensityItem = Item($"{percent}%", (_, _) => SetDurationGlowOpacity(percent / 100.0));
            intensityItem.IsCheckable = true;
            intensityItem.IsChecked = Math.Abs(Editor.DurationGlowOpacity * 100 - percent) < 0.5;
            glowIntensity.Items.Add(intensityItem);
        }
        playbackStyle.Items.Add(glowIntensity);
        menu.Items.Add(playbackStyle);
        Sep();
        var notation = Item("Show standard notation", (sender, _) =>
            SetNotation((sender as MenuItem)?.IsChecked == true ? NotationMode.TabAndStaff : NotationMode.TabOnly));
        notation.IsCheckable = true;
        notation.IsChecked = Editor.Notation != NotationMode.TabOnly;
        menu.Items.Add(notation);

        var ledgerMenu = new MenuItem
        {
            Header = "Ledger lines",
            Style = (Style)FindResource(typeof(MenuItem))
        };
        foreach (var mode in Enum.GetValues<LedgerLineMode>())
        {
            var ledgerItem = Item(mode.ToString(), (_, _) => SetLedgerLines(mode));
            ledgerItem.IsCheckable = true;
            ledgerItem.IsChecked = Editor.LedgerLines == mode;
            ledgerMenu.Items.Add(ledgerItem);
        }
        menu.Items.Add(ledgerMenu);

        menu.PlacementTarget = Editor;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Right-click on a note: the tool-palette actions grouped into sub-menus (duration, dynamics,
    /// effects, beat), so notes can be edited without the palette. The note is selected for editing
    /// without seeking playback.
    /// </summary>
    private void ShowNoteContextMenu(Views.ContextMenuEventArgs target)
    {
        Editor.SelectForEdit(target.Measure, target.Cell, target.StringIndex);
        RefreshToolsPalette();
        var menu = new ContextMenu
        {
            Style = (Style)FindResource(typeof(ContextMenu)),
            Background = (Brush)FindResource("Panel2Brush"),
            Foreground = (Brush)FindResource("TextBrush")
        };
        MenuItem Item(string header, Action action, bool isChecked = false, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, Style = (Style)FindResource(typeof(MenuItem)), IsEnabled = enabled };
            if (isChecked) { mi.IsCheckable = true; mi.IsChecked = true; }
            mi.Click += (_, _) => action();
            return mi;
        }
        void RunTool(string id) => ToolsPaletteButton_Click(new Button { Tag = id }, new RoutedEventArgs());
        MenuItem Group(string header, string category)
        {
            var group = new MenuItem { Header = header, Style = (Style)FindResource(typeof(MenuItem)) };
            foreach (var tool in PaletteTools.Where(t => t.Group == category && t.Supported))
            {
                var durationKey = tool.Id.StartsWith("duration:", StringComparison.Ordinal) ? tool.Id[9..] : null;
                var enabled = (durationKey is null || Editor.CanSetDurationForTool(durationKey)) && PaletteToolEnabled(tool.Id);
                group.Items.Add(Item(tool.Label, () => RunTool(tool.Id), PaletteToolState(tool.Id) == true, enabled));
            }
            return group;
        }

        menu.Items.Add(Group("Duration", "Duration"));
        menu.Items.Add(Group("Dynamics", "Dynamic"));
        menu.Items.Add(Group("Effects", "Effects"));
        menu.Items.Add(Group("Beat", "Beat"));
        menu.Items.Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });
        menu.Items.Add(Item("Pitch up a semitone", () => Editor.ShiftPitch(1)));
        menu.Items.Add(Item("Pitch down a semitone", () => Editor.ShiftPitch(-1)));
        menu.Items.Add(Item("Move to string above", () => Editor.MoveString(-1)));
        menu.Items.Add(Item("Move to string below", () => Editor.MoveString(1)));
        menu.Items.Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });
        menu.Items.Add(Item("Delete note", () => Editor.DeleteNote()));
        menu.Items.Add(Item("Score options…", () => ShowScoreContextMenu(target.Position)));
        menu.Closed += (_, _) => RefreshToolsPalette();
        menu.PlacementTarget = Editor;
        menu.IsOpen = true;
    }

    private void SetSmoothFollow(bool smooth)
    {
        if (_follow.Continuous == smooth) return;
        _settings.Follow.ContinuousScroll = smooth;
        _follow.ApplySettings(_settings.Follow);
        SaveSettings();
        StatusText.Text = smooth ? "Follow: smooth page turn" : "Follow: page turn";
    }

    private void SetHorizontalScoreView(bool horizontal)
    {
        if (Doc.HorizontalScoreView == horizontal && _settings.PreferredHorizontalScoreView == horizontal) return;
        CaptureDocumentState();
        Doc.HorizontalScoreView = horizontal;
        _settings.PreferredHorizontalScoreView = horizontal;
        Editor.HorizontalScroll = horizontal;
        ApplyPageWidth();
        ScoreScroll.ScrollToVerticalOffset(0);
        ScrollToCursor();
        SaveSettings();
        StatusText.Text = horizontal ? "Score scrolling: horizontal (one line)" : "Score scrolling: vertical";
    }

    private void SetContinuousScoreView(bool continuous)
    {
        if (Doc.ContinuousScoreView == continuous && _settings.PreferredContinuousScoreView == continuous) return;
        CaptureDocumentState();
        Doc.ContinuousScoreView = continuous;
        _settings.PreferredContinuousScoreView = continuous;
        Editor.CenterSystems = continuous;
        ApplyPageWidth(new Point(ScoreScroll.ViewportWidth / 2, 0));
        SaveSettings();
        StatusText.Text = continuous ? "Score layout: continuous" : "Score layout: page";
    }

    private void ChoosePlaybackColour(string label, Color initial, Action<Color> apply)
    {
        // Full chooser: preset dropdown + swatches + hue/saturation/brightness + hex, previewed live.
        var chosen = Views.ColourChooser.Show(this, label, initial, preview: apply);
        if (chosen is null) return;
        apply(chosen.Value);
        SaveSettings();
    }

    /// <summary>F10: Mix Table for the selected beat of the current track.</summary>
    private void ShowMixTable()
    {
        var track = SelectedTrack;
        if (track is null) return;
        var bar = Editor.SelectedMeasure;
        if (bar < 0 || bar >= track.Measures.Count) return;
        var measure = track.Measures[bar];
        var cellIndex = Math.Clamp(Editor.SelectedCell, 0, Math.Max(0, measure.Cells.Count - 1));
        if (measure.Cells.Count == 0) return;
        var cell = measure.Cells[cellIndex];
        var result = Views.MixTableWindow.Show(this, cell.Mix, track.MidiProgram, track.Volume, track.Pan, measure.TempoChange, _project.Tempo);
        if (result is null) return;
        CaptureUndo();
        cell.Mix = result.Mix is { IsEmpty: false } mix ? mix : null;
        if (result.Mix is { IsEmpty: true } && result.Tempo is null) measure.TempoChange = null;
        if (result.Tempo is int tempo)
            foreach (var t in _project.Tracks) if (bar < t.Measures.Count) t.Measures[bar].TempoChange = tempo;
        CommitEdit(EditRefresh.Score | EditRefresh.Arrangement);
        _midi.Rebuild(_project);
        StatusText.Text = cell.Mix is null ? $"Mix table cleared at bar {bar + 1}" : $"Mix table point set at bar {bar + 1}";
    }

    private void ShowScoreTextStyleWindow()
    {
        var areas = _settings.Appearance.ScoreTextAreas ??= new();
        var before = System.Text.Json.JsonSerializer.Serialize(areas);
        void Preview()
        {
            TabEditorControl.ConfigureTextAreas(areas);
            Editor.InvalidateScoreLayout();
            Editor.InvalidateVisual();
        }
        if (Views.ScoreTextStyleWindow.Show(this, areas, Preview)) SaveSettings();
        else
        {
            var restored = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ScoreTextAreaStyle>>(before) ?? new();
            _settings.Appearance.ScoreTextAreas = restored;
            TabEditorControl.ConfigureTextAreas(restored);
            Editor.InvalidateScoreLayout();
            Editor.InvalidateVisual();
        }
    }

    private void SetSectionGlowResources(double intensity)
    {
        intensity = Math.Clamp(intensity, 0, 1);
        Resources["SectionHoverGlowOpacity"] = intensity * 0.84;
        Resources["SectionActiveGlowOpacity"] = Math.Clamp(intensity * 1.6, 0, 1);
    }

    private void SetDurationGlowOpacity(double opacity)
    {
        Editor.DurationGlowOpacity = Math.Clamp(opacity, 0, 1);
        Editor.InvalidateVisual();
        Playhead.SetDurationStyle(Editor.DurationGlowColor, Editor.DurationGlowOpacity, _settings.Follow.DurationTintEnabled);
        SaveSettings();
    }
}
