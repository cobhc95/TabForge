using System;
using System.IO;
using System.Linq;
using TabForge.Avalonia;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the one reading and writing of a stored folder list (FolderListText, shared by the Settings list and the plug-in scanner) and folders dropped onto the list.
// Does not own: the list editor's buttons (PreferencesDeadInstructionTests).
// Tests: this file.
public sealed class PreferencesFolderListTests
{
    [Fact]
    public void ParsingKeepsOrderAndDropsEmptyEntriesAndExactDuplicates()
    {
        Assert.Equal(new[] { @"C:\A", @"%APPDATA%\VST", @"D:\B" }, FolderListText.Parse(@" C:\A; ;%APPDATA%\VST;c:\a;""D:\B"";"));
        Assert.Empty(FolderListText.Parse(null));
        Assert.Empty(FolderListText.Parse(" ; ;"));
    }

    [Fact]
    public void WritingAndParsingAgainLosesNothing()
    {
        var folders = new[] { @"C:\A", @"%PROGRAMFILES%\Steinberg\VstPlugins", @"D:\B" };
        var text = FolderListText.Join(folders);
        Assert.Equal(@"C:\A; %PROGRAMFILES%\Steinberg\VstPlugins; D:\B", text);
        Assert.Equal(folders, FolderListText.Parse(text));
        Assert.Equal(@"C:\A", FolderListText.Join(new[] { "\"C:\\A\"", "" }));
    }

    [Fact]
    public void TheScannerReadsTheListTheSameWay()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var settings = new PluginSettings { Folders = { $"\"{folder}\"", folder.ToUpperInvariant(), "  " } };
            Assert.Equal(new[] { folder }, VstScannerService.RootsFor(settings).ToArray());
        }
        finally { Directory.Delete(folder); }
    }

    [Fact]
    public void DroppedFoldersAreReadAndDroppedFilesAreNot()
    {
        var folder = Directory.CreateTempSubdirectory();
        var file = Path.Combine(folder.FullName, "song.tforge");
        File.WriteAllText(file, "x");
        try
        {
            Assert.Equal(new[] { folder.FullName }, MainWindowFileDrop.FoldersAmong(new[] { folder.FullName, file, null, Path.Combine(folder.FullName, "missing") }));
        }
        finally { File.Delete(file); folder.Delete(); }
    }
}
