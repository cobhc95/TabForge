// Owns: the score editor's empty-area right-click menu as data (paste, standard notation, note length outline, zoom, page layout and the one
//   "Score settings..." door), the main score's "Show in piano roll (this track)" row, and the ids and captions its rows carry.
// Does not own: opening the menu or running its rows (the score host, the Avalonia ScorePreviewView) or the Preferences pages it points to.
// Tests: SelfTestMenuGolden, TabForge.Avalonia.Tests.ContextMenuTests.

namespace TabForge.Menus;

public sealed record ScoreEmptyState(bool OverBeat, bool CanPaste, bool ShowNotation, bool Continuous, bool Horizontal, bool ShowOutline = true);

/// <summary>
/// The score's empty-area right-click menu as data: paste (over a beat, with a clip), standard notation, zoom, page layout and ONE
/// "Score settings..." door. Dark / light page, ledger lines, page turns, playback colours and text fonts are Preferences rows.
/// </summary>
public static class ScoreMenus
{
    public const string PasteId = "paste", PasteSpecialId = "pastespecial", NotationId = "notation", ZoomInId = "zoomin", ZoomOutId = "zoomout",
        FitWidthId = "fitwidth", LayoutId = "layout", SettingsId = "settings", OutlineId = "noteoutline";
    public const string ShowNotation = "Show standard notation", ShowOutline = "Show note length outline", Zoom = "Zoom", PageLayout = "Page layout", Settings = "Score settings…";
    /// <summary>The bottom row of the main score's menus: show this track in the piano roll instead (saved per track in the song).</summary>
    public const string ShowInPianoRollId = "Score.ShowInPianoRoll", ShowInPianoRoll = "Show in piano roll (this track)";
    /// <summary>The row "Score settings..." scrolls to: the first row of the Page layout group.</summary>
    public const string SettingsRow = "score.pagelayout";

    /// <summary>The "Show in piano roll (this track)" row the main score adds under its note and empty-area rows (a separator first).</summary>
    public static IEnumerable<MenuSpec> WithPianoRollRow(IEnumerable<MenuSpec> rows) =>
        rows.Append(MenuSpec.Separator()).Append(new MenuSpec { Id = ShowInPianoRollId, Header = ShowInPianoRoll, ToolTip = "Edit this track's notes in the piano roll; the choice is saved with the song" });

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
        list.Add(new MenuSpec { Id = OutlineId, Header = ShowOutline, Checkable = true, Checked = s.ShowOutline, SettingKey = "score.noteoutline" });
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
