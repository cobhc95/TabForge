using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: closing a tab forgets that song's waveform requests (MainWindowTabs.Released -> WaveformService.Forget).</summary>
public class TabCloseWaveformTests
{
    [Fact]
    public async Task ClosingATabForgetsItsWaveformSlotsAndKeepsTheOthers()
    {
        var service = new WaveformService();
        var first = new SongDocument(() => null) { Project = new SongProject { Title = "A" } };
        first.MarkClean();
        var second = new SongDocument(() => null) { Project = new SongProject { Title = "B" } };
        second.MarkClean();
        var tabs = new MainWindowTabs(first, _ => Task.FromResult(TabForge.Presenters.PromptButton.Cancel), _ => Task.FromResult(false), waveforms: service);
        Assert.Same(service, tabs.Waveforms);
        tabs.Open(second);
        const string File = @"C:\tf-no-such-folder\a.wav";
        service.StatusOf(File, first.Media);
        service.StatusOf(File, second.Media);
        Assert.Equal(2, service.CachedCount);

        Assert.True(await tabs.CloseActive());   // the shown tab is the second one

        Assert.Equal(1, service.CachedCount);
    }
}
