using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TabForge;

// Owns: source-hygiene check (extensions, control chars, skipped folders).

public static partial class SelfTest
{
    private static readonly string[] HygieneExtensions = { ".cs", ".xaml", ".ps1", ".cmd", ".md" };
    private static readonly HashSet<string> HygieneSkippedFolders =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "build", "dist", "third_party", "Tabs", "backup", "work", "TabForge Avalonia" };   // plus every dot-folder (tool data, other checkouts), see EnumerateHygieneFiles

    /// <summary>Every control character (char.IsControl: 0x00-0x1F and 0x7F-0x9F) except tab, LF and CR.</summary>
    private static readonly System.Buffers.SearchValues<char> StrayControlCharacters = System.Buffers.SearchValues.Create(
        Enumerable.Range(0, 0xA0).Select(i => (char)i).Where(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r')).ToArray());

    /// <summary>
    /// A scripted edit must not run escape processing on a Windows path ("native\fetch" would become a form feed).
    /// Fails on any control character other than tab, CR and LF in the repository's text sources.
    /// Skipped (a failure with --require source-hygiene) when the executable does not run from inside a source checkout (installed copy).
    /// </summary>
    private static void TestSourceControlCharacters()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("source files contain no stray control characters", "no source checkout found", "source-hygiene"); return; }

        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in EnumerateHygieneFiles(root))
        {
            scanned++;
            var text = File.ReadAllText(file);
            var at = text.AsSpan().IndexOfAny(StrayControlCharacters);   // vectorised: the sweep covers every text source
            if (at >= 0) offenders.Add($"{Path.GetRelativePath(root, file)}:{text.AsSpan(0, at).Count('\n') + 1} (0x{(int)text[at]:X2})");
        }
        Log.Add($"  info  scanned {scanned} source file(s) under {root} for control characters");
        Check("source files contain no stray control characters (tab/CR/LF only)", offenders.Count == 0,
            string.Join(", ", offenders.Take(10)));
    }

    /// <summary>
    /// installer/TabForge.iss writes the file associations itself ([Registry]); they must match
    /// <see cref="Services.FileAssociations.Extensions"/> (same extensions, ProgIDs, descriptions, icon and open command).
    /// </summary>
    private static void TestInstallerAssociationParity()
    {
        var root = FindRepositoryRoot();
        var iss = root is null ? null : Path.Combine(root, "Source", "installer", "TabForge.iss");
        if (iss is null || !File.Exists(iss)) { Skip("installer file associations match FileAssociations.Extensions", "no source checkout / installer script found", "installer-parity"); return; }

        var lines = new List<string>();
        var inRegistry = false;
        foreach (var raw in File.ReadAllLines(iss))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inRegistry = line.Equals("[Registry]", StringComparison.OrdinalIgnoreCase); continue; }
            if (inRegistry && line.StartsWith("Root:", StringComparison.OrdinalIgnoreCase)) lines.Add(line);
        }
        static string? Field(string line, string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, $@"(?:^|;)\s*{name}:\s*(""(?:[^""]|"""")*""|[^;]*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var v = m.Groups[1].Value.Trim();
            return v.StartsWith('"') && v.EndsWith('"') && v.Length >= 2 ? v[1..^1].Replace("\"\"", "\"") : v;
        }
        var entries = lines.Select(l => (Key: Field(l, "Subkey") ?? "", Name: Field(l, "ValueName") ?? "", Data: Field(l, "ValueData") ?? "")).ToList();
        var issExtensions = entries.Select(e => System.Text.RegularExpressions.Regex.Match(e.Key, @"^Software\\Classes\\(\.[A-Za-z0-9]+)\\OpenWithProgids$"))
            .Where(m => m.Success).Select(m => m.Groups[1].Value.ToLowerInvariant()).ToHashSet();
        var code = Services.FileAssociations.Extensions.Select(e => e.ToLowerInvariant()).ToHashSet();
        var problems = new List<string>();
        foreach (var missing in code.Except(issExtensions)) problems.Add($"{missing} missing in the installer");
        foreach (var extra in issExtensions.Except(code)) problems.Add($"{extra} only in the installer");
        foreach (var ext in code.Intersect(issExtensions))
        {
            var progId = Services.FileAssociations.ProgIdFor(ext);
            bool Has(string key, string name, string data) => entries.Any(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && e.Name == name && e.Data == data);
            if (!Has($@"Software\Classes\{ext}\OpenWithProgids", progId, "")) problems.Add($"{ext}: OpenWithProgids does not list {progId}");
            if (!Has($@"Software\Classes\{progId}", "", Services.FileAssociations.Description(ext))) problems.Add($"{progId}: description differs");
            if (!Has($@"Software\Classes\{progId}\DefaultIcon", "", "\"{app}\\TabForge.exe\",0")) problems.Add($"{progId}: icon differs");
            if (!Has($@"Software\Classes\{progId}\shell\open\command", "", "\"{app}\\TabForge.exe\" \"%1\"")) problems.Add($"{progId}: open command differs");
        }
        Check("B-05: installer [Registry] file associations match FileAssociations.Extensions (extensions, ProgIDs, descriptions, icon, command)",
            problems.Count == 0 && code.Count > 0, $"{lines.Count} registry lines; " + string.Join("; ", problems.Take(8)));
    }

    /// <summary>
    /// LGPL-2.1 compliance: SoundTouch.Net.dll must sit beside TabForge.exe as a loose, replaceable file (not inside the
    /// single-file bundle), and the licence texts must ship in a licenses folder.
    /// </summary>
    private static void TestLooseSoundTouchAndLicenseTexts()
    {
        var dir = AppContext.BaseDirectory;
        Check("LGPL: SoundTouch.Net.dll is a loose file beside the executable",
            File.Exists(Path.Combine(dir, "SoundTouch.Net.dll")), dir);
        var licenses = Path.Combine(dir, "licenses");
        var required = new[] { "LGPL-2.1_SoundTouch.Net.txt", "OFL-1.1_Bravura.txt", "MIT_NAudio.txt", "MIT_MeltySynth_and_notices.txt" };
        var missing = required.Where(f => !File.Exists(Path.Combine(licenses, f))).ToList();
        Check("licence texts ship in the licenses folder beside the executable", missing.Count == 0, string.Join(", ", missing));
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
            var text = File.ReadAllText(file);
            if (bad.Any(b => text.Contains(b, StringComparison.Ordinal))) offenders.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
        }
        Check("tracked .md and .cs files hold no mojibake sequences", offenders.Count == 0, string.Join(", ", offenders.Take(10)));
    }

    private static IEnumerable<string> EnumerateHygieneFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (!HygieneSkippedFolders.Contains(Path.GetFileName(sub)) && !Path.GetFileName(sub).StartsWith('.')) pending.Push(sub);
            foreach (var file in Directory.EnumerateFiles(dir))
                if (HygieneExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) yield return file;
        }
    }

    /// <summary>
    /// The source checkout that contains this executable or the working directory, if any. The private workspace keeps the sources
    /// under "Source\"; the public repository has them at its root. For the public layout a private-layout mirror is built once per run
    /// (files copied under a temp "Source\" folder) so the checks that name "Source/..." paths read the same files in both layouts.
    /// </summary>
    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Source", "TabForge.sln")) &&
                    File.Exists(Path.Combine(dir.FullName, "Source", "Directory.Build.props")))
                    return dir.FullName;
                if (File.Exists(Path.Combine(dir.FullName, "TabForge.sln")) && File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "src")))
                    return PublicLayoutMirror(dir.FullName);
            }
        }
        return null;
    }

    private static string? _publicMirror;

    private static readonly string[] PublicRootEntries = { "src", "tests", "tools", "native", "vendor", "installer", "samples", ".github", "LICENSE", "Directory.Build.props", "global.json", "TabForge.sln", "nuget.config" };

    /// <summary>A root document of the public tree links relative to the repository root; in the mirror it sits in Source/docs, so its link targets are moved back.</summary>
    private static string PrivateRelativeLinks(string markdown) =>
        Regex.Replace(markdown, @"\]\(([^)\s#]+)((?:#[^)\s]*)?)\)", m =>
        {
            var target = m.Groups[1].Value;
            if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal)) return m.Value;
            if (target.StartsWith("docs/", StringComparison.Ordinal)) return "](" + target[5..] + m.Groups[2].Value + ")";
            var first = target.Split('/')[0];
            return PublicRootEntries.Contains(first, StringComparer.Ordinal) ? "](../" + target + m.Groups[2].Value + ")" : m.Value;
        });

    private static string PublicLayoutMirror(string repo)
    {
        if (_publicMirror is not null && Directory.Exists(_publicMirror)) return _publicMirror;
        var mirror = Path.Combine(Path.GetTempPath(), "tabforge-selftest-layout-" + Environment.ProcessId);
        if (Directory.Exists(mirror)) Directory.Delete(mirror, true);
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "build", "dist", "backup", "work", "Tabs", "TestResults", "node_modules" };
        void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (var sub in Directory.EnumerateDirectories(from))
                if (!skipped.Contains(Path.GetFileName(sub))) Copy(sub, Path.Combine(to, Path.GetFileName(sub)));
        }
        var source = Path.Combine(mirror, "Source");
        foreach (var folder in new[] { "src", "tests", "tools", "installer", "native", "vendor", "docs" })
            if (Directory.Exists(Path.Combine(repo, folder))) Copy(Path.Combine(repo, folder), Path.Combine(source, folder));
        foreach (var folder in new[] { "samples", ".github" })
            if (Directory.Exists(Path.Combine(repo, folder))) Copy(Path.Combine(repo, folder), Path.Combine(mirror, folder));
        Directory.CreateDirectory(Path.Combine(source, "docs"));
        foreach (var file in Directory.EnumerateFiles(repo))
        {
            var name = Path.GetFileName(file);
            var isDoc = name.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
            if (name is ".gitattributes" or ".gitignore") File.Copy(file, Path.Combine(mirror, name), true);
            else if (isDoc) File.WriteAllText(Path.Combine(source, "docs", name), PrivateRelativeLinks(File.ReadAllText(file)));
            else File.Copy(file, Path.Combine(source, name), true);
        }
        return _publicMirror = mirror;
    }
}
