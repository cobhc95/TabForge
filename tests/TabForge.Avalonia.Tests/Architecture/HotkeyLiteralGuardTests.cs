using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TabForge.Menus;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the master-list guard for user-facing key texts and common captions: a key gesture ("Ctrl+L", "Shift+Up") written as a
//   string literal outside the hotkey master lists (Services/Hotkeys: HotkeyCatalog*.cs, FixedKeys.cs), a literal menu Shortcut, and a
//   common caption ("OK", "Cancel", "Apply", "Close", "Browse…", "_Yes", "_No") written as a button/prompt literal outside CommonText.cs all fail.
// Does not own: the catalogue's own consistency (HotkeySweepTests) or one-off texts (they stay with their view or presenter).
// Tests: this class. How to add a key or a caption: Claude/strings-hotkeys-audit-2026-11.md.
public sealed class HotkeyLiteralGuardTests
{
    private static readonly Regex StringLiteral = new(@"@?\$?""(?:[^""\\\r\n]|\\.)*""", RegexOptions.Compiled);

    // A modifier followed by a key name (capitalised); mouse hints such as "Ctrl+click" or "Ctrl+wheel" are lower case and go through FixedKeys too, but by hand.
    private static readonly Regex GestureInLiteral = new(@"\b(?:Ctrl|Alt|Shift|Win)\+(?:[A-Z0-9]|F\d|Num|Del|Esc|Ins|Home|End|Page|Space|Tab|Enter|Back|Oem|Up|Down|Left|Right)", RegexOptions.Compiled);
    private static readonly Regex LiteralShortcut = new(@"\bShortcut\s*=\s*""[^""]+""", RegexOptions.Compiled);

    private static readonly Regex CommonCaption = new(
        @"(?:Content\s*=\s*|\b(?:YesText|NoText):\s*|\b\w*(?:Text|Button|Label|Caption)\s*=\s*|PromptChrome\.Action\(""\w+"",\s*|\b(?:AnswerButton|Push|AddButton|Small|ToolButton|BankButton)\(|\(\s*(?=""\w+"",\s*MessageBoxAnswer))""(?:OK|Cancel|Apply|Close|Browse…|_Yes|_No|Delete)""",
        RegexOptions.Compiled);

    private static bool IsHotkeyMaster(string path)
    {
        var name = Path.GetFileName(path);
        return ArchitectureScan.HasPathSegment(path, "Hotkeys") && (name.StartsWith("HotkeyCatalog", StringComparison.Ordinal) || name == "FixedKeys.cs" || name == "HotkeyPresets.cs")
            || name.StartsWith("HotkeyPresets", StringComparison.Ordinal)
            || ArchitectureScan.HasPathSegment(path, "SelfTests");   // self tests bind throwaway keys on purpose
    }

    private static List<string> Scan(Func<string, bool> skip, Func<string, IEnumerable<int>> hits)
    {
        var root = ArchitectureScan.RepoRoot();
        var found = new List<string>();
        foreach (var file in ArchitectureScan.SourceFiles(root, skip))
        {
            var text = ArchitectureScan.Strip(ArchitectureScan.ReadText(file), stripStrings: false);
            foreach (var index in hits(text))
                found.Add($"{ArchitectureScan.Relative(root, file)}:{ArchitectureScan.LineOf(text, index)}");
        }
        return found;
    }

    [Fact]
    public void NoKeyGestureLiteralOutsideTheHotkeyMasterLists()
    {
        var found = Scan(IsHotkeyMaster, text =>
            StringLiteral.Matches(text).Where(m => GestureInLiteral.IsMatch(m.Value)).Select(m => m.Index)
                .Concat(LiteralShortcut.Matches(text).Select(m => m.Index)));
        Assert.True(found.Count == 0,
            "Key texts must come from HotkeyCatalog (bindable: HotkeyCatalog.DisplayAll / TooltipWithKey / HotkeyTip.Set) or FixedKeys (fixed keys):\n"
            + string.Join("\n", found));
    }

    [Fact]
    public void NoCommonCaptionLiteralOutsideCommonText()
    {
        var found = Scan(p => Path.GetFileName(p) == "CommonText.cs", text => CommonCaption.Matches(text).Select(m => m.Index));
        Assert.True(found.Count == 0, "Use CommonText (TabForge.Core/Text/CommonText.cs) for OK, Cancel, Apply, Close, Delete, Browse…:\n" + string.Join("\n", found));
    }

    [Fact]
    public void PianoRollMenuShowsTheLiveBinding()
    {
        var rows = PianoRollMenus.Rows(new PianoRollMenuState(OnNotes: true, CanPaste: true, InsideLoop: false, Key: id => id == PianoRollMenus.Legato ? "Ctrl+Alt+L" : ""));
        Assert.Equal("Ctrl+Alt+L", rows.Single(r => r.Id == PianoRollMenus.Legato).Shortcut);
        var defaults = PianoRollMenus.Rows(new PianoRollMenuState(OnNotes: true, CanPaste: true, InsideLoop: false));
        Assert.Equal(HotkeyCatalog.DisplayAll(new HotkeySettings(), PianoRollMenus.Duplicate), defaults.Single(r => r.Id == PianoRollMenus.Duplicate).Shortcut);
    }
}
