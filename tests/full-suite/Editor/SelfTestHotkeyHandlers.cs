using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TabForge.Services;
using TabForge.Views.EffectEditors;

namespace TabForge;

/// <summary>
/// Every catalogued command id is run by the window's RunHotkey chain: a case label, switch arm or comparison in one of the
/// routers it calls, a line of the plain-command table (MainWindow.Commands.cs), the effect-editor router (EffectEditorFlow.KindOf),
/// the Clip.* route to ClipEditController, or the Tool.* prefix. The ids RunHotkey does not run would be listed below with the place that runs them. Source-level: no window is opened.
/// </summary>
public static partial class SelfTest
{
    /// <summary>Catalogued commands that RunHotkey does not run, each with the place that runs it. Empty: the clip commands reach ClipEditController from the palette too.</summary>
    private static readonly (string Id, string Reason)[] HotkeyIdsOutsideRunHotkey = Array.Empty<(string, string)>();

    /// <summary>A line of the plain-command table: Click("Id", handler) or Run("Id", action).</summary>
    internal static readonly Regex CommandTableLine = new(@"\b(?:Click|Run)\(""([A-Za-z]+\.[A-Za-z]+)""", RegexOptions.Compiled);

    /// <summary>The src/TabForge folder of the source tree the build sits in or the working folder is in; null when it is not there.</summary>
    private static string? RouterSourceFolder()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "src", "TabForge", "Services", "HotkeyCatalog.cs"))) return Path.Combine(dir.FullName, "src", "TabForge");
        return null;
    }

    /// <summary>Every catalogued command id is handled by the RunHotkey chain or listed as run elsewhere; the list stays exact.</summary>
    private static void TestEveryHotkeyIdHasHandler()
    {
        var src = RouterSourceFolder();
        if (src is null) { Skip("every catalogued command id has a RunHotkey handler", "no source checkout found"); return; }

        // The routers RunHotkey calls (MainWindow.Settings.cs, the score commands, the track-row, range and tool-palette routes).
        var files = new List<string>
        {
            Path.Combine(src, "MainWindow.Settings.cs"),
            Path.Combine(src, "MainWindow.Commands.cs"),
            Path.Combine(src, "Controllers", "TrackClipboardFlow.cs"),
            Path.Combine(src, "Controllers", "BarRangeFlow.cs"),
            Path.Combine(src, "Controllers", "ClipEditController.cs"),
            Path.Combine(src, "Services", "BarRangePromptText.cs"),
        };
        files.AddRange(RouterSourceScan.ScoreCommandFiles(src));
        var missing = files.Where(f => !File.Exists(f)).Select(Path.GetFileName).ToList();
        Check("the router sources the hotkey test reads exist", missing.Count == 0, string.Join(", ", missing));
        if (missing.Count > 0) return;

        // Ids named by a case label, a switch arm or a comparison. The clip router is read below and kept out of the chain set.
        var chainFiles = files.Where(f => !f.EndsWith("ClipEditController.cs", StringComparison.Ordinal)).ToList();
        var handled = new HashSet<string>(StringComparer.Ordinal);
        RouterSourceScan.AddRouteIds(chainFiles, handled);
        foreach (Match m in CommandTableLine.Matches(File.ReadAllText(Path.Combine(src, "MainWindow.Commands.cs")))) handled.Add(m.Groups[1].Value);
        // Feature modules register their own commands (Services/Features); a stub host lists them.
        var moduleCommands = new CommandRegistry();
        TabForge.Services.Features.FeatureRegistry.AddCommands(moduleCommands, new StubFeatureHost());
        foreach (var id in moduleCommands.Ids) handled.Add(id);
        var anchors = new[] { "File.New", "Edit.InsertBeat", "Note.PitchUp", "Range.Delete", "Range.Clear", "TrackRow.Copy", "Track.ConvertToAudio" };
        Check("the scan reads the RunHotkey case labels (anchor ids are found)", anchors.All(handled.Contains), string.Join(", ", anchors.Where(a => !handled.Contains(a))));

        var mainWindow = File.ReadAllText(Path.Combine(src, "MainWindow.Settings.cs"));
        Check("RunHotkey routes the Tool.* palette tools by prefix", mainWindow.Contains(@"StartsWith(""Tool."", StringComparison.Ordinal)", StringComparison.Ordinal));

        Check("RunHotkey routes the Clip.* palette commands to the clip controller", mainWindow.Contains("IsClipAction(clip): return _clips.RunHotkey(Doc, clip)", StringComparison.Ordinal));

        bool RunHotkeyRuns(string id) => handled.Contains(id) || HotkeyCatalog.IsClipAction(id) || EffectEditorFlow.KindOf(id) is not null || id.StartsWith("Tool.", StringComparison.Ordinal);

        var catalog = HotkeyCatalog.All.Select(a => a.Id).Distinct(StringComparer.Ordinal).ToList();
        var outside = HotkeyIdsOutsideRunHotkey.ToDictionary(e => e.Id, e => e.Reason, StringComparer.Ordinal);
        var unhandled = catalog.Where(id => !RunHotkeyRuns(id) && !outside.ContainsKey(id)).ToList();
        Check($"every catalogued command ({catalog.Count} ids) is run by the RunHotkey chain or listed with where it runs", unhandled.Count == 0, string.Join(", ", unhandled));

        var stale = outside.Keys.Where(id => !catalog.Contains(id, StringComparer.Ordinal) || RunHotkeyRuns(id)).ToList();
        Check("the list of ids outside RunHotkey names only catalogued ids that RunHotkey does not run", stale.Count == 0, string.Join(", ", stale));

        var clipSource = File.ReadAllText(Path.Combine(src, "Controllers", "ClipEditController.cs"));
        var clipLabels = new HashSet<string>(Regex.Matches(clipSource, @"case\s+""([A-Za-z]+\.[A-Za-z]+)""").Select(m => m.Groups[1].Value), StringComparer.Ordinal);
        var clipNotRun = catalog.Where(id => HotkeyCatalog.IsClipAction(id) && !clipLabels.Contains(id)).ToList();
        Check("each catalogued clip command has a case label in ClipEditController", clipNotRun.Count == 0, string.Join(", ", clipNotRun));

        Log.Add($"  info  {outside.Count} of {catalog.Count} catalogued ids are run outside RunHotkey (each listed with its place in SelfTestHotkeyHandlers.cs)");
    }
}
