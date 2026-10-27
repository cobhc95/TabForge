using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Mixer at its smallest allowed size: no control of a row overlaps another one (the Master row used to put the Monitor FX button into the
/// zero-width MIDI column, over the pan slider).
/// Tests: this class.
/// </summary>
public class MixerSmallSizeTests
{
    [AvaloniaFact]
    public void NoRowControlsOverlapAtTheSmallestSize()
    {
        var window = new MixerWindow(new SongDocument(() => null) { Project = DemoSongFactory.Create() });
        window.Show();
        window.EnsureStripsBuilt();
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.UpdateLayout();

        var rows = window.GetLogicalDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("Mixer ") == true && b.Child is Grid).ToList();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var parts = ((Grid)row.Child!).Children.Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0).ToList();
            for (var i = 0; i < parts.Count; i++)
                for (var j = i + 1; j < parts.Count; j++)
                {
                    var overlap = parts[i].Bounds.Intersect(parts[j].Bounds);
                    Assert.True(overlap.Width < 1 || overlap.Height < 1,
                        $"{row.Name}: {parts[i].GetType().Name} {parts[i].Bounds} overlaps {parts[j].GetType().Name} {parts[j].Bounds} ({window.IsCompact})");
                }
        }
        window.Close();
    }
}
