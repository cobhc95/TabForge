using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the View and tempo bar following the shown tab: the tempo and time signature captions change on a tab switch and after an edit of the shown song.</summary>
public sealed class MainWindowTempoBarTests
{
    private static SongDocument Song(int tempo, int numerator)
    {
        var project = SongDocument.BlankProject();
        project.Tempo = tempo;
        project.TimeSignatureNumerator = numerator;
        var song = new SongDocument(() => null) { Project = project };
        song.MarkClean();
        return song;
    }

    [AvaloniaFact]
    public void TempoBarFollowsTheShownTabAndItsEdits()
    {
        var first = Song(120, 4);
        var tabs = new MainWindowTabs(first, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var bar = new ViewTempoBarView();
        _ = new MainWindowTempoBar(tabs, bar);
        Assert.Equal("120", bar.Caption(2));
        Assert.StartsWith("4/4", bar.Caption(3));

        tabs.Open(Song(90, 3));
        Assert.Equal("90", bar.Caption(2));
        Assert.StartsWith("3/4", bar.Caption(3));

        DocumentEdits.Run(tabs.Active, project => { project.Tempo = 150; return true; });
        Assert.Equal("150", bar.Caption(2));

        tabs.Switch(0);
        Assert.Equal("120", bar.Caption(2));
        Assert.StartsWith("4/4", bar.Caption(3));
    }

    [AvaloniaFact]
    public void DisposedTempoBarNoLongerFollowsTheSong()
    {
        var first = Song(120, 4);
        var tabs = new MainWindowTabs(first, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var bar = new ViewTempoBarView();
        var tempoBar = new MainWindowTempoBar(tabs, bar);
        tempoBar.Dispose();
        DocumentEdits.Run(tabs.Active, project => { project.Tempo = 150; return true; });
        Assert.Equal("120", bar.Caption(2));
    }
}
