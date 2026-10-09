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

    /// <summary>The shortcut text of a command, from the user's bindings (empty when unbound).</summary>
    private string MenuKey(string id) => HotkeyCatalog.DisplayAll(_settings.Hotkeys, id);

    /// <summary>
    /// Right-click on empty page. With a <paramref name="target"/> over a beat the beat is
    /// selected (no seek) and Paste / Paste special appear first when the clipboard holds a clip. Lean (owner decisions
    /// 2026-09-30): notation, zoom, page layout and ONE "Score settings..." door; paper, ledger lines, page turns, colours and
    /// text fonts are Preferences rows.
    /// </summary>
    private void ShowScoreContextMenu(Point position, Views.ContextMenuEventArgs? target = null)
    {
        var overBeat = target is { OverBeat: true };
        if (overBeat)
        {
            Editor.SelectForEdit(target!.Measure, target.Cell, target.StringIndex);
            RefreshToolsPalette();
        }
        var state = new ScoreEmptyState(overBeat, ClipboardService.Shared.CanPaste, Editor.Notation != NotationMode.TabOnly,
            Editor.Appearance.CenterSystems, Editor.HorizontalScroll);
        var menu = SpecMenus.New("Score options", ScoreMenus.Empty(state, MenuKey), spec =>
        {
            switch (spec.Id)
            {
                case ScoreMenus.PasteId: Paste_Click(this, new RoutedEventArgs()); break;
                case ScoreMenus.PasteSpecialId: PasteSpecial_Click(this, new RoutedEventArgs()); break;
                case ScoreMenus.NotationId: SetNotation(spec.Checked ? NotationMode.TabOnly : NotationMode.TabAndStaff); break;
                case ScoreMenus.ZoomInId: ScoreZoom.ZoomBy(1); break;
                case ScoreMenus.ZoomOutId: ScoreZoom.ZoomBy(-1); break;
                case ScoreMenus.FitWidthId: ScoreZoom.ApplyZoomText("Fit width"); break;
                case ScoreMenus.LayoutId:
                    switch (spec.Arg)
                    {
                        case "page": SetContinuousScoreView(false); break;
                        case "continuous": SetContinuousScoreView(true); break;
                        case "vertical": SetHorizontalScoreView(false); break;
                        case "horizontal": SetHorizontalScoreView(true); break;
                    }
                    break;
                case ScoreMenus.SettingsId: OpenSettings(SettingsCatalog.Score, ScoreMenus.SettingsRow); break;
            }
        }, Editor);
        SpecMenus.Open(menu, Editor, target?.Anchor, target?.FromKeyboard == true);
    }

    /// <summary>
    /// Right-click on a note: the tool-palette actions grouped into sub-menus (duration, dynamics,
    /// effects, beat), so notes can be edited without the palette. The note is selected for editing
    /// without seeking playback.
    /// </summary>
    private void ShowNoteContextMenu(Views.ContextMenuEventArgs target)
    {
        // A right-click inside the current selection keeps it (the menu acts on the whole selection);
        // outside it, the clicked beat becomes the selection.
        if (!target.InsideSelection) Editor.SelectForEdit(target.Measure, target.Cell, target.StringIndex);
        RefreshToolsPalette();
        var menu = new ContextMenu
        {
            Style = (Style)FindResource(typeof(ContextMenu)),
            Background = (Brush)FindResource("Panel2Brush"),
            Foreground = (Brush)FindResource("TextBrush")
        };
        // Tools with a state (on / off) are checkable even when off, so the tick shows the next time; choices of one value
        // (durations, dynamics) are drawn as a dot (radio), the rest as a tick.
        MenuItem Item(string header, Action action, bool? state = null, bool enabled = true, string? gesture = null, bool radio = false)
        {
            var mi = new MenuItem { Header = header, Style = (Style)FindResource(typeof(MenuItem)), IsEnabled = enabled };
            if (state is not null) { mi.IsCheckable = true; mi.IsChecked = state == true; MenuMarks.SetIsRadio(mi, radio); }
            if (!string.IsNullOrEmpty(gesture)) mi.InputGestureText = gesture;
            mi.Click += (_, _) => action();
            return mi;
        }
        void RunTool(string id) => ToolsPaletteButton_Click(new Button { Tag = id }, new RoutedEventArgs());
        MenuItem Group(string header, string category, bool radio = false)
        {
            var group = new MenuItem { Header = header, Style = (Style)FindResource(typeof(MenuItem)) };
            foreach (var tool in ToolPaletteController.PaletteTools.Where(t => t.Group == category && t.Supported))
            {
                var durationKey = tool.Id.StartsWith("duration:", StringComparison.Ordinal) ? tool.Id[9..] : null;
                var enabled = (durationKey is null || Editor.Effects.CanSetDurationForTool(durationKey)) && ToolPalette.PaletteToolEnabled(tool.Id);
                group.Items.Add(Item(tool.Label, () => RunTool(tool.Id), ToolPalette.PaletteToolState(tool.Id), enabled, radio: radio));
            }
            return group;
        }

        var pitchAndString = new MenuItem { Header = ContextMenuLayouts.PitchAndString, Style = (Style)FindResource(typeof(MenuItem)) };
        pitchAndString.Items.Add(Item("Pitch up a semitone", () => Editor.Effects.ShiftPitch(1), gesture: MenuKey("Note.PitchUp")));
        pitchAndString.Items.Add(Item("Pitch down a semitone", () => Editor.Effects.ShiftPitch(-1), gesture: MenuKey("Note.PitchDown")));
        pitchAndString.Items.Add(Item("Move note to higher string", () => Editor.Effects.MoveNotesToAdjacentString(-1), gesture: MenuKey("Note.MoveStringUp")));
        pitchAndString.Items.Add(Item("Move note to lower string", () => Editor.Effects.MoveNotesToAdjacentString(1), gesture: MenuKey("Note.MoveStringDown")));
        var parts = new Dictionary<string, Func<MenuItem>>
        {
            [ContextMenuLayouts.Copy] = () => Item("Copy", () => Copy_Click(this, new RoutedEventArgs()), gesture: MenuKey("Edit.Copy")),
            [ContextMenuLayouts.Cut] = () => Item("Cut", () => Cut_Click(this, new RoutedEventArgs()), gesture: MenuKey("Edit.Cut")),
            [ContextMenuLayouts.Paste] = () => Item("Paste", () => Paste_Click(this, new RoutedEventArgs()), gesture: MenuKey("Edit.Paste")),
            [ContextMenuLayouts.PasteSpecial] = () => Item("Paste special…", () => PasteSpecial_Click(this, new RoutedEventArgs()), gesture: MenuKey("Edit.PasteSpecial")),
            [ContextMenuLayouts.Delete] = () => Item("Delete", () => Editor.Effects.DeleteNote(), gesture: ContextMenuLayouts.FixedKeys.DeleteNote),
            [ContextMenuLayouts.Duration] = () => Group("Duration", "Duration", radio: true),
            [ContextMenuLayouts.Dynamics] = () => Group("Dynamics", "Dynamic", radio: true),
            [ContextMenuLayouts.Effects] = () => Group("Effects", "Effects"),
            [ContextMenuLayouts.Beat] = () => Group("Beat", "Beat"),
            [ContextMenuLayouts.PitchAndString] = () => pitchAndString
        };
        foreach (var id in ContextMenuLayouts.NoteMenu(ClipboardService.Shared.CanPaste))
        {
            if (id == ContextMenuLayouts.Sep) menu.Items.Add(SpecMenus.Separator(this));
            else menu.Items.Add(parts[id]());
        }
        menu.Closed += (_, _) => RefreshToolsPalette();
        menu.PlacementTarget = Editor;
        SpecMenus.Open(menu, Editor, target.Anchor, target.FromKeyboard);
    }

    private void SetSmoothFollow(bool smooth)
    {
        if (_follow.Continuous == smooth) return;
        _settings.Follow.ContinuousScroll = smooth;
        _follow.ApplySettings(_settings.Follow);
        SaveSettings();
        StatusText.Text = smooth ? "Follow: smooth page turn" : "Follow: page turn";
    }

    private void SetPlayingBar(bool on)
    {
        _settings.Follow.PlayingBarEnabled = on;
        Editor.Appearance.PlayingBarEnabled = on;
        PlayingBarMenu.IsChecked = on;
        Editor.InvalidateVisual();
        SaveSettings();
        StatusText.Text = on ? "Playing bar highlight: on" : "Playing bar highlight: off";
    }

    private void PlayingBarMenu_Click(object sender, RoutedEventArgs e) => SetPlayingBar(PlayingBarMenu.IsChecked);

    private void SetHorizontalScoreView(bool horizontal)
    {
        if (Doc.HorizontalScoreView == horizontal && _settings.PreferredHorizontalScoreView == horizontal) return;
        CaptureDocumentState();
        Doc.HorizontalScoreView = horizontal;
        _settings.PreferredHorizontalScoreView = horizontal;
        Editor.HorizontalScroll = horizontal;
        ScoreZoom.ApplyPageWidth();
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
        Editor.Appearance.CenterSystems = continuous;
        ScoreZoom.ApplyPageWidth(new Point(ScoreScroll.ViewportWidth / 2, 0));
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
        DocumentEdits.Run(Doc, p =>
        {
            cell.Mix = result.Mix is { IsEmpty: false } mix ? mix : null;
            if (result.Mix is { IsEmpty: true } && result.Tempo is null) measure.TempoChange = null;
            if (result.Tempo is int tempo)
                foreach (var t in p.Tracks) if (bar < t.Measures.Count) t.Measures[bar].TempoChange = tempo;
            return true;
        });
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Arrangement);
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
        Editor.Appearance.DurationGlowOpacity = Math.Clamp(opacity, 0, 1);
        Editor.InvalidateVisual();
        Playhead.SetDurationStyle(Editor.Appearance.DurationGlowColor, Editor.Appearance.DurationGlowOpacity, _settings.Follow.DurationTintEnabled);
        SaveSettings();
    }
}
