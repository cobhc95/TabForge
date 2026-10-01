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
    IReadOnlyList<string> Roots, IReadOnlyList<string> ScaleNames, string? Scale, bool NoteNames, bool PreviewNext, bool LeftHanded, bool Locked);

/// <summary>
/// The fretboard / keyboard / drum-pad right-click menu as data (owner decisions 2026-09-30). Lean: the per-track view, the scale,
/// the practice toggles and ONE "Fretboard settings..." door to Preferences. Appearance, sizes, colours and the "all tracks"
/// view live in Preferences. At most two submenu levels (Scale > key > scale), no submenu repeats its parent's name.
/// </summary>
internal static class InstrumentMenus
{
    public const string ViewId = "view", ScaleId = "scale", FindScaleId = "findscale", ClearScaleId = "clearscale", NoteNamesId = "notenames",
        PreviewId = "preview", LeftHandedId = "lefthanded", LockId = "locksize", SettingsId = "settings";
    public const string ShowThisTrackAs = "Show this track as", Scale = "Scale", NoteNames = "Note names", PreviewNext = "Preview next notes",
        LeftHanded = "Left-handed", LockSize = "Lock fretboard size", Settings = "Fretboard settings…";
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
