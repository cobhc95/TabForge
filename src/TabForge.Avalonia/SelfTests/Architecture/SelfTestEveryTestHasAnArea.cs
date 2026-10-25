using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TabForge;

// Owns: the check that every registered self-test has an area and every area entry names a registered test.
// Does not own: the area table (SelfTest.cs) or the registrations themselves.
// Tests: TestEveryTestHasAnArea.
public static partial class SelfTest
{
    private static readonly Regex RegistrationCall = new(@"\bGuard(?:Group)?\(\s*(?:[^,()]*,\s*)?(Test\w+)\s*[,)]", RegexOptions.CultureInvariant);
    private static readonly Regex TestDeclaration = new(@"\bstatic\s+void\s+(Test\w+)\s*\(", RegexOptions.CultureInvariant);
    private static readonly Regex TrailingLineComment = new(@"(?m)^\s*//.*$|\s//.*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Every test registered with Guard / GuardGroup has an explicit entry in <c>AreaOf</c>, so a focused <c>--areas</c> run never depends on a
    /// default, and every <c>AreaOf</c> entry is a declared, registered test. The registrations are read from the self-test sources of the checkout.
    /// </summary>
    private static void TestEveryTestHasAnArea()
    {
        var root = FindRepositoryRoot();
        var dir = root is null ? null : Path.Combine(root, "Source", "src", "TabForge.Avalonia", "SelfTests");
        if (dir is null || !Directory.Exists(dir)) { Skip("every registered self-test has an AreaOf entry", "no source checkout found", "architecture"); return; }
        var registered = new SortedSet<string>(StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var code = TrailingLineComment.Replace(File.ReadAllText(file), "");
            foreach (Match m in RegistrationCall.Matches(code)) registered.Add(m.Groups[1].Value);
            foreach (Match m in TestDeclaration.Matches(code)) declared.Add(m.Groups[1].Value);
        }
        var missing = registered.Where(n => !AreaOf.ContainsKey(n)).ToList();
        Check($"every registered self-test ({registered.Count}) has an explicit AreaOf entry", registered.Count > 10 && missing.Count == 0,
            registered.Count <= 10 ? "the registration scan found too few tests" : $"{missing.Count} without an area: {string.Join(", ", missing.Take(12))}");
        var stale = AreaOf.Keys.Where(k => !declared.Contains(k) || !registered.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Check("every AreaOf entry names a declared, registered self-test", stale.Count == 0, string.Join(", ", stale.Take(12)));
    }
}
