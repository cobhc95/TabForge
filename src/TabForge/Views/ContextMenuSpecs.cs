using TabForge.Services;
using TabForge.Views.Band;
using System.Windows;

namespace TabForge.Views;

/// <summary>Marks for menu items. <see cref="IsRadioProperty"/> draws a ticked item as a dot (one of several choices) instead of a tick.</summary>
public static class MenuMarks
{
    public static readonly DependencyProperty IsRadioProperty =
        DependencyProperty.RegisterAttached("IsRadio", typeof(bool), typeof(MenuMarks), new PropertyMetadata(false));

    public static bool GetIsRadio(DependencyObject element) => (bool)element.GetValue(IsRadioProperty);
    public static void SetIsRadio(DependencyObject element, bool value) => element.SetValue(IsRadioProperty, value);
}

internal sealed record InstrumentMenuState(bool Keyboard, bool Drums, string TrackName, string CurrentView, IReadOnlyList<string> ViewNames,
    IReadOnlyList<string> Roots, IReadOnlyList<string> ScaleNames, string? Scale, bool NoteNames, bool PreviewNext, bool LeftHanded, bool Locked, bool AtBottom = false);

/// <summary>
/// The fretboard / keyboard / drum-pad right-click menu as data (owner decisions 2026-09-30). Lean: the per-track view, the scale,
/// the practice toggles and ONE "Fretboard settings..." door to Preferences. Appearance, sizes, colours and the "all tracks"
/// view live in Preferences. At most two submenu levels (Scale > key > scale), no submenu repeats its parent's name.
/// </summary>
internal static class InstrumentMenus
{
    public const string ViewId = "view", ScaleId = "scale", FindScaleId = "findscale", ClearScaleId = "clearscale", NoteNamesId = "notenames",
        PreviewId = "preview", LeftHandedId = "lefthanded", LockId = "locksize", PositionId = "position", SettingsId = "settings";
    public const string ShowThisTrackAs = "Show this track as", Scale = "Scale", NoteNames = "Note names", PreviewNext = "Preview next notes",
        LeftHanded = "Left-handed", LockSize = "Lock fretboard size", Position = "Position", Settings = "Fretboard settings…";
    /// <summary>The row "Fretboard settings..." scrolls to: the first row of the Appearance group.</summary>
    public const string SettingsRow = "editing.frets";

    private static MenuSpec Choice(string id, string header, string arg, bool on, string settingKey) =>
        new() { Id = id, Header = header, Arg = arg, Checkable = true, Checked = on, Radio = true, SettingKey = settingKey };

    private static MenuSpec Toggle(string id, string header, bool on, string settingKey, string shortcut = "", string? toolTip = null) =>
        new() { Id = id, Header = header, Checkable = true, Checked = on, SettingKey = settingKey, Shortcut = shortcut, ToolTip = toolTip };

    private static MenuSpec Action(string id, string header, string shortcut = "", bool enabled = true, string? toolTip = null) =>
        new() { Id = id, Header = header, Shortcut = shortcut, Enabled = enabled, ToolTip = toolTip };

    public static List<MenuSpec> Build(InstrumentMenuState s, Func<string, string> key)
    {
        var scale = new List<MenuSpec>();
        foreach (var root in s.Roots)
            scale.Add(new MenuSpec
            {
                Header = root,
                Children = s.ScaleNames.Select(name => Choice(ScaleId, name, $"{root} {name}", $"{root} {name}" == s.Scale, "editing.scale")).ToList()
            });
        scale.Add(MenuSpec.Separator());
        scale.Add(Action(FindScaleId, "Find scale…", key("Tools.ScaleFinder")));
        scale.Add(Action(ClearScaleId, "Clear scale", key("View.ClearScale"), enabled: s.Scale is not null));

        var groups = new List<List<MenuSpec>>
        {
            new()
            {
                new MenuSpec
                {
                    Header = ShowThisTrackAs, Shortcut = key("View.InstrumentView"),                    Children = s.ViewNames.Select(v => Choice(ViewId, v, v, v == s.CurrentView, "fretboard.instrumentview")).ToList()
                },
                new MenuSpec { Header = Scale, Children = scale }
            }
        };
        if (!s.Drums)
        {
            var practice = new List<MenuSpec>
            {
                Toggle(NoteNamesId, NoteNames, s.NoteNames, "editing.notenames"),
                Toggle(PreviewId, PreviewNext, s.PreviewNext, "editing.horizon.enabled"),
            };
            if (!s.Keyboard) practice.Add(Toggle(LeftHandedId, LeftHanded, s.LeftHanded, "editing.lefthanded"));
            groups.Add(practice);
        }
        groups.Add(new List<MenuSpec>
        {
            Toggle(LockId, LockSize, s.Locked, "fretboard.locksize", key("View.LockInstrumentSize"),
                "Locked: dragging the edge does not resize the fretboard / keyboard. Unlocked: the drawing scales with the pane."),
            new MenuSpec
            {
                Header = Position, Shortcut = key("View.FretboardPosition"),
                Children = new List<MenuSpec>
                {
                    Choice(PositionId, "Top", "top", !s.AtBottom, "fretboard.dockposition"),
                    Choice(PositionId, "Bottom", "bottom", s.AtBottom, "fretboard.dockposition")
                }
            },
            Action(SettingsId, Settings, toolTip: "Open Settings > Fretboard & Keyboard (appearance, sizes, colours, look-ahead, default view)")
        });

        var list = new List<MenuSpec>();
        foreach (var group in groups)
        {
            if (list.Count > 0) list.Add(MenuSpec.Separator());
            list.AddRange(group);
        }
        return list;
    }
}

internal sealed record ScoreEmptyState(bool OverBeat, bool CanPaste, bool ShowNotation, bool Continuous, bool Horizontal);

/// <summary>
/// The score's empty-area right-click menu as data: paste (over a beat, with a clip), standard notation, zoom, page layout and ONE
/// "Score settings..." door. Dark / light page, ledger lines, page turns, playback colours and text fonts are Preferences rows.
/// </summary>
internal static class ScoreMenus
{
    public const string PasteId = "paste", PasteSpecialId = "pastespecial", NotationId = "notation", ZoomInId = "zoomin", ZoomOutId = "zoomout",
        FitWidthId = "fitwidth", LayoutId = "layout", SettingsId = "settings";
    public const string ShowNotation = "Show standard notation", Zoom = "Zoom", PageLayout = "Page layout", Settings = "Score settings…";
    /// <summary>The row "Score settings..." scrolls to: the first row of the Page layout group.</summary>
    public const string SettingsRow = "score.pagelayout";

    private static MenuSpec Layout(string arg, string header, bool on, string settingKey) =>
        new() { Id = LayoutId, Header = header, Arg = arg, Checkable = true, Checked = on, Radio = true, SettingKey = settingKey };

    public static List<MenuSpec> Empty(ScoreEmptyState s, Func<string, string> key)
    {
        var list = new List<MenuSpec>();
        if (s.OverBeat && s.CanPaste)
        {
            list.Add(new MenuSpec { Id = PasteId, Header = "Paste", Shortcut = key("Edit.Paste") });
            list.Add(new MenuSpec { Id = PasteSpecialId, Header = "Paste special…", Shortcut = key("Edit.PasteSpecial") });
            list.Add(MenuSpec.Separator());
        }
        list.Add(new MenuSpec { Id = NotationId, Header = ShowNotation, Checkable = true, Checked = s.ShowNotation, SettingKey = "score.defaultnotation" });
        list.Add(new MenuSpec
        {
            Header = Zoom,
            Children = new List<MenuSpec>
            {
                new() { Id = ZoomInId, Header = "Zoom in", Shortcut = key("View.ZoomIn") },
                new() { Id = ZoomOutId, Header = "Zoom out", Shortcut = key("View.ZoomOut") },
                new() { Id = FitWidthId, Header = "Fit width" },
            }
        });
        list.Add(new MenuSpec
        {
            Header = PageLayout,
            Children = new List<MenuSpec>
            {
                Layout("page", "Page", !s.Continuous, "score.pagelayout"),
                Layout("continuous", "Continuous", s.Continuous, "score.pagelayout"),
                MenuSpec.Separator(),
                Layout("vertical", "Vertical", !s.Horizontal, "score.scrolling"),
                Layout("horizontal", "Horizontal", s.Horizontal, "score.scrolling"),
            }
        });
        list.Add(MenuSpec.Separator());
        list.Add(new MenuSpec { Id = SettingsId, Header = Settings, ToolTip = "Open Settings > Score & Notation (what is shown, spacing, text and fonts, paper, ink and ledger lines)" });
        return list;
    }
}

internal sealed record BandMenuState(string Content, string Size, int Rows, bool FollowsScore, bool WidthPerRow = false, string PlayheadLine = BandChoices.TabOnly, string Layout = BandChoices.Vertical);

/// <summary>The Band view's right-click menu as data: lane content, instrument size, rows per screen, follow, zoom (the score's) and ONE "Band settings..." door.</summary>
internal static class BandMenus
{
    public const string ContentId = "bandcontent", SizeId = "bandsize", RowsId = "bandrows", FollowId = "bandfollow", ResetHeightsId = "bandheights", SettingsId = "bandsettings", WidthModeId = "bandwidthmode", ResetWidthsId = "bandwidths", PlayheadId = "bandplayhead", LayoutId = "bandlayout", ResetViewId = "bandresetview", LaneZoomInId = "bandlanezoomin", LaneZoomOutId = "bandlanezoomout", LaneZoomResetId = "bandlanezoomreset";
    public const string AllRows = "All rows", ThisRow = "This row only";
    public const string Lanes = "Lanes show", Instruments = "Instrument size", Rows = "Rows per screen", Follow = "Follow like the score", Settings = "Band settings…";
    /// <summary>The row "Band settings..." scrolls to: the first row of the Band view group.</summary>
    public const string SettingsRow = "band.instrumentsize";

    private static MenuSpec Radio(string id, string header, bool on, string settingKey) =>
        new() { Id = id, Header = header, Arg = header, Checkable = true, Checked = on, Radio = true, SettingKey = settingKey };

    public static List<MenuSpec> Build(BandMenuState s, Func<string, string> key) => new()
    {
        new() { Header = Lanes, Shortcut = key("Band.CycleLaneContent"), Children = BandChoices.Contents.Select(c => Radio(ContentId, c, c == s.Content, "band.lanecontent")).ToList() },
        new() { Header = "Lane layout", Shortcut = key("Band.CycleLaneLayout"), Children = BandChoices.Layouts.Select(c => Radio(LayoutId, c, c == s.Layout, "band.lanelayout")).ToList() },
        new() { Header = Instruments, Shortcut = key("Band.CycleInstrumentSize"), Children = BandChoices.Sizes.Select(c => Radio(SizeId, c, c == s.Size, "band.instrumentsize")).ToList() },
        new() { Header = Rows, Children = Enumerable.Range(BandLayoutState.MinRowsPerScreen, BandLayoutState.MaxRowsPerScreen - BandLayoutState.MinRowsPerScreen + 1).Select(n => Radio(RowsId, n.ToString(), n == s.Rows, "band.rowsperscreen")).ToList() },
        new() { Id = ResetHeightsId, Header = "Reset row heights", Shortcut = key("Band.ResetRowHeights") },
        new() { Header = "Instrument width", Children = new List<MenuSpec>
            {
                new() { Id = WidthModeId, Header = AllRows, Arg = AllRows, Checkable = true, Checked = !s.WidthPerRow, Radio = true },
                new() { Id = WidthModeId, Header = ThisRow, Arg = ThisRow, Checkable = true, Checked = s.WidthPerRow, Radio = true },
            } },
        new() { Id = ResetWidthsId, Header = "Reset row widths" },
        new() { Header = "Playhead line", Children = BandChoices.PlayheadLines.Select(c => Radio(PlayheadId, c, c == s.PlayheadLine, "band.playheadline")).ToList() },
        new() { Id = FollowId, Header = Follow, Checkable = true, Checked = s.FollowsScore, Shortcut = key("Band.ToggleSmoothFollow"), SettingKey = "band.followscore" },
        new()
        {
            Header = ScoreMenus.Zoom,
            Children = new List<MenuSpec>
            {
                new() { Id = LaneZoomInId, Header = "Zoom in", ToolTip = "Make the tab or notation in every Band lane bigger (the mouse wheel with the zoom modifier over the Band view)", Shortcut = "Ctrl+Wheel", SettingKey = "band.lanezoom" },
                new() { Id = LaneZoomOutId, Header = "Zoom out", SettingKey = "band.lanezoom" },
                new() { Id = LaneZoomResetId, Header = "Reset zoom", SettingKey = "band.lanezoom" },
            }
        },
        new() { Id = ResetViewId, Header = "Reset view", ToolTip = "Row heights, instrument widths, rows per screen, hidden instruments, lane zoom, layout and playhead line back to their defaults; the shown tracks and their order stay" },
        MenuSpec.Separator(),
        new() { Id = SettingsId, Header = Settings, ToolTip = "Open Settings > Timeline > Band view (instrument size, lanes, follow, rows, order)" },
    };
}
