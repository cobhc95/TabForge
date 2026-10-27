using TabForge.Avalonia;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the File.ProjectSettings write, the dialog's result applied to the shown song as one undo step.</summary>
public class MainWindowProjectSettingsTests
{
    [Fact]
    public void ApplyWritesTheDialogResultToTheSong()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var edited = MainWindowProjectSettings.ResultOf(document.Project) with { Title = "Edited title", Tempo = 90 };
        Assert.True(MainWindowProjectSettings.Apply(document, edited));
        Assert.Equal("Edited title", document.Project.Title);
        Assert.Equal(90, document.Project.Tempo);
    }
}
