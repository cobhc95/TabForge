using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the no-pointer-shape-change guard. The app never changes the mouse pointer over a control (hand, I-beam, cross, move); only resize cursors
//   (SizeWestEast, SizeNorthSouth, and the window-edge corner and side sizes) are allowed. Hover shades and pressed looks carry the "clickable" feedback.
// Does not own: the resize grips themselves (DockSplitter, BandFretboardGrip, LinuxChromeAdapter).
// Tests: itself. A text scan of Source/src for StandardCursorType, Cursor.Parse, Cursors.*, the Cursor property as a style setter, and XAML Cursor= attributes.
public sealed class NoPointerShapeGuardTests
{
    private static readonly Regex AllowedType = new(@"^StandardCursorType\.(SizeWestEast|SizeNorthSouth|(?:Top|Bottom)(?:Left|Right)Corner|TopSide|BottomSide|LeftSide|RightSide)\b", RegexOptions.Compiled);
    private static readonly Regex TypeUse = new(@"StandardCursorType\.\w+", RegexOptions.Compiled);
    private static readonly Regex Other = new(@"\bCursor\.Parse\b|\bCursors\.\w+|Property\s*=\s*""Cursor""|Cursor\s*=\s*""|\bCursorProperty\b", RegexOptions.Compiled);

    [Fact]
    public void OnlyResizeCursorsAreUsed()
    {
        var repo = ArchitectureScan.RepoRoot();
        var src = Path.Combine(repo, "Source", "src");
        var files = ArchitectureScan.SourceFiles(repo).Concat(Directory.EnumerateFiles(src, "*.axaml", SearchOption.AllDirectories)).ToList();
        Assert.True(files.Count > 50, "NoPointerShape guard scanned too few source files");
        var found = new List<string>();
        foreach (var file in files)
        {
            if (ArchitectureScan.HasPathSegment(file, "obj") || ArchitectureScan.HasPathSegment(file, "bin")) continue;
            var text = file.EndsWith(".cs", StringComparison.Ordinal) ? ArchitectureScan.Strip(ArchitectureScan.ReadText(file), stripStrings: false) : ArchitectureScan.ReadText(file);
            var rel = ArchitectureScan.Relative(repo, file);
            found.AddRange(TypeUse.Matches(text).Where(m => !AllowedType.IsMatch(m.Value)).Select(m => $"{rel}:{ArchitectureScan.LineOf(text, m.Index)} {m.Value}"));
            found.AddRange(Other.Matches(text).Select(m => $"{rel}:{ArchitectureScan.LineOf(text, m.Index)} {m.Value}"));
        }
        Assert.True(found.Count == 0, "The pointer shape never changes except to resize (hover shade shows clickable):\n" + string.Join("\n", found));
    }
}
