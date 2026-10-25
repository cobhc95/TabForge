using System.Linq;
using TabForge.Documents;
using TabForge.Menus;
using Avalonia.Headless.XUnit;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the document tab's Duplicate (an unsaved copy placed after its original and shown) and the tab right-click menu's enabled rows.
// Does not own: the strip's press and drag (TabStripPressAndLookTests, TabTearOffTests), the middle-click mapping (TabBehaviourSettingsTests).
// Tests: this class.
public class DocumentTabMenuTests
{
    [AvaloniaFact]
    public void DuplicateInsertsAnUnsavedCopyAfterTheOriginalAndShowsIt()
    {
        var window = new MainWindow();
        window.Show();
        var original = window.Songs.Songs[0];

        var copy = window.Songs.Duplicate(0);

        Assert.Same(copy, window.Songs.Songs[1]);
        Assert.Same(copy, window.Songs.Active);
        Assert.True(copy.HasUnsavedChanges);
        Assert.Equal(original.Project.Title + SongDuplicate.CopySuffix, copy.Project.Title);
        copy.MarkClean();   // the unsaved copy would open the save prompt on close, which nothing answers here
        window.Close();
    }

    [Fact]
    public void TabMenuRowsAreDisabledWhenThereIsNothingToActOn()
    {
        var lone = DocumentTabMenus.Build(0, 1, _ => "").Where(row => row.Id is not null).ToDictionary(row => row.Id!);
        Assert.False(lone[DocumentTabMenus.CloseOthers].Enabled);
        Assert.False(lone[DocumentTabMenus.CloseToTheRight].Enabled);
        Assert.False(lone[DocumentTabMenus.MoveToNewWindow].Enabled);

        var lastOfTwo = DocumentTabMenus.Build(1, 2, _ => "").Where(row => row.Id is not null).ToDictionary(row => row.Id!);
        Assert.True(lastOfTwo[DocumentTabMenus.CloseOthers].Enabled);
        Assert.False(lastOfTwo[DocumentTabMenus.CloseToTheRight].Enabled);
        Assert.Equal("1", lastOfTwo[DocumentTabMenus.Duplicate].Arg);
    }
}
