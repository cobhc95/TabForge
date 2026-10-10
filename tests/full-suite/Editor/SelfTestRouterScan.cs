using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace TabForge;

/// <summary>
/// Source scans shared by the command-table and RunHotkey tests (SelfTestCommandRegistry.cs, SelfTestHotkeyHandlers.cs).
/// Owns: the pattern that finds a command id in a route position, and the ScoreEditCommands router files.
/// Does not own: which files a test reads, or what it asserts about the ids found.
/// </summary>
internal static class RouterSourceScan
{
    /// <summary>An id named by a case label, a switch arm or a comparison in router source.</summary>
    internal static readonly Regex RouteId = new(@"(?:\bcase\s+|==\s*|=>\s*)""([A-Za-z]+\.[A-Za-z]+)""|""([A-Za-z]+\.[A-Za-z]+)""\s*=>");

    /// <summary>The ScoreEditCommands*.cs router files under src/TabForge/Views/Score.</summary>
    internal static string[] ScoreCommandFiles(string src) => Directory.GetFiles(Path.Combine(src, "Views", "Score"), "ScoreEditCommands*.cs");

    /// <summary>Adds every id that the files name in a route position to <paramref name="into"/>.</summary>
    internal static void AddRouteIds(IEnumerable<string> files, ISet<string> into)
    {
        foreach (var file in files)
            foreach (Match m in RouteId.Matches(File.ReadAllText(file)))
                into.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
    }
}
