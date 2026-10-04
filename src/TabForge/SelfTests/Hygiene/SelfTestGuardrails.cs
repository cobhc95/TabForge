using System.IO;
using System.Linq;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Files that may hold a mojibake-looking sequence (none today); a new entry needs a reason in the commit.</summary>
    private static readonly HashSet<string> MojibakeAllow = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bindable commands the hotkey document may omit (a ratchet: the list only shrinks); a new entry needs a reason in the commit.</summary>
    private static readonly HashSet<string> HotkeyDocAllow = new(StringComparer.Ordinal) { };   // every command is documented; keep it that way

    /// <summary>
    /// Every command id in the fixed <see cref="HotkeyCatalog"/> appears in TOOLS_AND_HOTKEYS.md, so a new bindable command cannot ship
    /// undocumented. Palette tools registered at start-up are covered by their own category lines and are not listed here.
    /// </summary>
    private static void TestHotkeyIdsDocumented()
    {
        var root = FindRepositoryRoot();
        var path = root is null ? null : Path.Combine(root, "TOOLS_AND_HOTKEYS.md");
        if (path is null || !File.Exists(path)) { Skip("every bindable command id is in TOOLS_AND_HOTKEYS.md", "no source checkout found", "source-hygiene"); return; }
        var doc = File.ReadAllText(path);
        var missing = HotkeyCatalog.All.Select(a => a.Id).Where(id => !doc.Contains(id, StringComparison.Ordinal) && !HotkeyDocAllow.Contains(id)).ToList();
        Check("every bindable command id is in TOOLS_AND_HOTKEYS.md", missing.Count == 0, string.Join(", ", missing.Take(10)) + (missing.Count > 10 ? $" (+{missing.Count - 10} more)" : ""));
    }

    /// <summary>
    /// Tracked .md and .cs files hold no UTF-8-read-as-Windows-1252 sequences (a scripted edit once turned text into such garbage).
    /// The sequences are built from code points so this file does not match itself.
    /// </summary>
    private static void TestNoMojibakeInSources()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("sources hold no mojibake sequences", "no source checkout found", "source-hygiene"); return; }
        // UTF-8 text decoded as Windows-1252 starts with these pairs: U+00E2 U+20AC, U+00C3 + a Latin-1 letter, U+00C2 U+00A0, U+00EF U+00BB U+00BF.
        int[][] pairs = { new[] { 0xE2, 0x20AC }, new[] { 0xC3, 0xA9 }, new[] { 0xC3, 0xA8 }, new[] { 0xC3, 0xBC }, new[] { 0xC3, 0xB6 }, new[] { 0xC3, 0xA4 }, new[] { 0xC2, 0xA0 }, new[] { 0xEF, 0xBB, 0xBF } };
        var bad = pairs.Select(p => new string(p.Select(c => (char)c).ToArray())).ToArray();
        var offenders = new List<string>();
        foreach (var file in EnumerateHygieneFiles(root))
        {
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".md", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (MojibakeAllow.Contains(rel)) continue;
            var text = File.ReadAllText(file);
            if (bad.Any(b => text.Contains(b, StringComparison.Ordinal))) offenders.Add(rel);
        }
        Check("tracked .md and .cs files hold no mojibake sequences", offenders.Count == 0, string.Join(", ", offenders.Take(10)));
    }

    /// <summary>`--find` reports a known test and stays within its line cap.</summary>
    private static void TestFindCommand()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("--find lists matches within the line cap", "no source checkout found", "source-hygiene"); return; }
        var report = Diagnostics.FeatureFinder.Search(root, "Architecture");
        var lines = report.TrimEnd('\n').Split('\n');
        Check("--find lists a known test", report.Contains("TestArchitectureGuards", StringComparison.Ordinal));
        Check("--find stays within 40 lines", lines.Length <= Diagnostics.FeatureFinder.MaxLines, $"{lines.Length} lines");
        Check("--find reports no match for nonsense", Diagnostics.FeatureFinder.Search(root, "zzqqxx-no-such-thing").Contains("0 match", StringComparison.Ordinal));
    }
}
