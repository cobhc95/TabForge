using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the guard that a menu, key or button handler never runs a file or folder picker through a nested message loop: the synchronous IFileDialogs calls
//   (OpenFile, OpenFiles, SaveFile, PickFolder, PickFolders) stay only where a window still takes a synchronous callback, and the list only shrinks.
// Does not own: the dialogs (AvaloniaFileDialogs) or the awaited forms (OpenFileAsync, OpenFilesAsync, SaveFileAsync, PickFolderAsync, PickFoldersAsync).
// Tests: this class.
public class PickerGuardTests
{
    // File -> the most synchronous picker calls it may hold. Each is a callback a window still takes as a plain Func; move it to the awaited form and lower the count.
    private static readonly Dictionary<string, int> Allowed = new()
    {
    };

    private static readonly Regex Call = new(@"\.(OpenFile|OpenFiles|SaveFile|PickFolder|PickFolders)\(", RegexOptions.Compiled);

    [Fact]
    public void SynchronousPickerCallsOnlyShrink()
    {
        var root = ArchitectureScan.RepoRoot();
        var found = new Dictionary<string, int>();
        foreach (var file in ArchitectureScan.SourceFiles(root))
        {
            var relative = ArchitectureScan.Relative(root, file);
            if (relative.EndsWith("Platform/AvaloniaFileDialogs.cs") || relative.EndsWith("Platform/IFileDialogs.cs")) continue;
            var count = File.ReadLines(file).Count(line => Call.IsMatch(line) && !line.TrimStart().StartsWith("//")
                && !line.Contains("File.OpenFile") && !line.Contains("Launcher") && !line.Contains("PluginHostBlock") && !line.Contains("fileLauncher"));
            if (count > 0) found[relative] = count;
        }
        var grown = found.Where(f => !Allowed.TryGetValue(f.Key, out var max) || f.Value > max)
            .Select(f => $"{f.Key}: {f.Value} synchronous picker call(s), allowed {(Allowed.TryGetValue(f.Key, out var a) ? a : 0)}").ToList();
        Assert.True(grown.Count == 0,
            "A synchronous picker call runs a nested message loop inside the handler (it froze Load FX chain). Use the awaited form (OpenFileAsync, OpenFilesAsync, SaveFileAsync, PickFolderAsync, PickFoldersAsync):\n"
            + string.Join("\n", grown));
    }
}
