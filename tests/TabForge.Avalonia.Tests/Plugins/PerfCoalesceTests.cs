using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views.Fx;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class PerfCoalesceTests
{
    [AvaloniaFact]
    public void EqDragFiftyStepsOneRebuildPerStep()
    {
        var view = new EqCurveView();
        var sends = 0;
        view.Changed += (_, _) => sends++;
        var before = view.RebuildCount;
        for (var i = 0; i < 50; i++)
            view.DragBand(0, 100 + i * 10, i * 0.1);
        Assert.True(view.RebuildCount - before <= 50 && view.RebuildCount - before >= 45);
        Assert.True(sends <= 150 && sends > 0);
    }

    [AvaloniaFact]
    public void FxListFiftyStepDragComputesOncePerPosition()
    {
        var list = new FxChainList();
        for (var i = 0; i < 5; i++)
            list.Add(new TextBlock { Text = $"P{i}" }, new object(), $"tip{i}", $"P{i}");
        list.Click(0);
        list.SimulateDragStart(0);
        var drops = 0;
        list.Dropped += (_, _, _) => drops++;
        for (var i = 0; i < 50; i++)
            list.SimulateDragMove(list.RowTop(1) + i);
        var computes = list.DragComputeCount;
        Assert.True(computes <= 50 && computes >= 49);
        var same = list.DragComputeCount;
        list.SimulateDragMove(list.RowTop(1) + 49);
        Assert.Equal(same, list.DragComputeCount);
        list.SimulateDragEnd();
        Assert.True(drops <= 1);
    }

    [Fact]
    public void PluginSlotCloneIsDeepWithoutJson()
    {
        var src = new PluginSlot { Name = "P", State = "abc", MidiProcessors = { new PluginMidiProcessor { Type = "t", Params = "{}" } } };
        src.ChainContainers = new() { new ChainContainer { Name = "C", ActiveOn = new ActiveOnRule { DeviceName = "Dev" } } };
        var copy = src.Clone(keepId: false);
        Assert.NotEqual(src.Id, copy.Id);
        Assert.Equal("abc", copy.State);
        Assert.Single(copy.MidiProcessors);
        Assert.Single(copy.ChainContainers!);
        copy.MidiProcessors[0].Type = "changed";
        copy.ChainContainers![0].Name = "changed";
        Assert.Equal("t", src.MidiProcessors[0].Type);
        Assert.Equal("C", src.ChainContainers![0].Name);
    }
}
