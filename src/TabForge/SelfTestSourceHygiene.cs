using System.IO;
using System.Linq;

namespace TabForge;

public static partial class SelfTest
{
    private static readonly string[] HygieneExtensions = { ".cs", ".xaml", ".ps1", ".cmd", ".md" };
    private static readonly HashSet<string> HygieneSkippedFolders =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "build", "dist", "third_party", "Tabs", "backup", "work" };   // plus every dot-folder (tool data, other checkouts), see EnumerateHygieneFiles

    /// <summary>
    /// H-02: a scripted edit once ran escape processing on a Windows path ("native\fetch" became a form feed).
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
            var line = 1;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\n') { line++; continue; }
                if (c == '\t' || c == '\r' || c == '﻿') continue;
                if (char.IsControl(c))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{line} (0x{(int)c:X2})");
                    break;
                }
            }
        }
        Log.Add($"  info  scanned {scanned} source file(s) under {root} for control characters");
        Check("source files contain no stray control characters (tab/CR/LF only)", offenders.Count == 0,
            string.Join(", ", offenders.Take(10)));
    }

    /// <summary>
    /// B-05: installer/TabForge.iss writes the file associations itself ([Registry]); they must match
    /// <see cref="Services.FileAssociations.Extensions"/> (same extensions, ProgIDs, descriptions, icon and open command).
    /// </summary>
    private static void TestInstallerAssociationParity()
    {
        var root = FindRepositoryRoot();
        var iss = root is null ? null : Path.Combine(root, "installer", "TabForge.iss");
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

    /// <summary>The source checkout that contains this executable or the working directory, if any.</summary>
    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TabForge.sln")) &&
                    File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
                    return dir.FullName;
            }
        }
        return null;
    }
}
