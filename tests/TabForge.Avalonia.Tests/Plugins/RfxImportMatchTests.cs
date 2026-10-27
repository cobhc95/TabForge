using System.IO;
using System.Linq;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: how an FX chain entry from another host finds its plug-in file (file name, "_x64" suffix, VST3 class id), the folders Reaper itself lists, and the
/// not-found notice.
/// Does not own: scanning or trust.
/// Tests: this class.
/// </summary>
public class RfxImportMatchTests
{
    private const string ClassId = "56534450726F3351756F6E7461000000";

    private static ReaperChainImporter.Entry Entry(string text) => ReaperChainImporter.Parse(text).Entries.Single();

    [Fact]
    public void AFileWithAnX64SuffixIsTheSamePlugIn()
    {
        var entry = Entry("<VST \"VST: Bulldozer (Acme)\" Bulldozer.dll 0 \"\" 1<5653> \"\"\n>\n");
        var catalog = new[] { new VstPluginInfo("Bulldozer", @"C:\vst\Bulldozer_x64.dll", "VST2", "Acme") };
        Assert.Same(catalog[0], ReaperChainImporter.Match(entry, catalog));
        Assert.Same(catalog[0], ReaperChainImporter.Match(entry with { FileName = "Bulldozer (x64).dll" }, catalog));
        Assert.Null(ReaperChainImporter.Match(entry with { FileName = "Other.dll" }, catalog));
    }

    [Fact]
    public void AVst3ClassIdFindsThePlugInWhateverItsFileIsCalled()
    {
        var entry = Entry($"<VST \"VST3: Pro-Q 3 (Acme)\" \"Renamed Original.vst3\" 0 \"\" 0{{{ClassId}}} \"\"\n>\n");
        Assert.Equal(ClassId, entry.ClassId);
        var catalog = new[]
        {
            new VstPluginInfo("Other", @"C:\vst\Other.vst3", "VST3", "Acme", "Effect", "00000000000000000000000000000001"),
            new VstPluginInfo("Pro-Q 3", @"C:\vst\FabQ.vst3", "VST3", "Acme", "Effect", ClassId.ToLowerInvariant()),
        };
        Assert.Same(catalog[1], ReaperChainImporter.Match(entry, catalog));
    }

    [Fact]
    public void ANotFoundPlugInIsNamedWithTheFoldersSearched()
    {
        var parsed = ReaperChainImporter.Parse("<VST \"VST: Bulldozer (Acme)\" Bulldozer.dll 0 \"\" 1<5653> \"\"\n>\n");
        var imported = ReaperChainImporter.Build(parsed, new VstPluginInfo[0], new[] { @"C:\vst", @"D:\more" });
        Assert.Equal(1, imported.Missing);
        Assert.Contains("Bulldozer isn't in TabForge's plug-in folders. Add its folder in Preferences › Plug-ins, or rescan.", imported.Report);
        Assert.Contains(@"Folders searched: C:\vst; D:\more.", imported.Report);
    }

    [Fact]
    public void ReapersOwnVstFoldersAreRead()
    {
        var dir = Directory.CreateTempSubdirectory("rfx-ini");
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(dir.FullName, "Plugs")).FullName;
            var ini = Path.Combine(dir.FullName, "reaper.ini");
            File.WriteAllText(ini, $"[REAPER]\nvstpath=x\nvstpath64={folder};{Path.Combine(dir.FullName, "Gone")}\n");
            Assert.Equal(new[] { folder }, ReaperPluginFolders.Roots(ini));
            Assert.Empty(ReaperPluginFolders.Roots(Path.Combine(dir.FullName, "none.ini")));
        }
        finally { dir.Delete(true); }
    }
}
