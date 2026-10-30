using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services.Features;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the container checks of the FX chain window: New container, Put selected in a new container, Dissolve, Rename, Delete with its plug-ins (the question),
/// the bypass tick and the collapse chevron, the container pane, the right-click menu, container presets, and that containers survive a saved chain file.
/// Does not own: the container model (ChainTree), the window or the list.
/// Tests: this class.
/// </summary>
[Collection("PluginLibraryRoot")]   // PluginLibrary.RootOverride is process-wide: run alone so no parallel test resets it or reads the scratch folder
public class FxChainContainersTests
{
    private static (FxChainWindow Window, TrackModel Track, FxTestKit.FakeEngine Engine) Open(Func<string, MessageBoxAnswer>? ask = null)
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine), ask: ask);
        window.Show();
        return (window, track, engine);
    }

    private static ChainContainer GroupFirstTwo(FxChainWindow window, TrackModel track)
    {
        var list = FxTestKit.List(window);
        list.Click(0);
        list.Click(1, shift: true);
        Assert.True(window.Run(FxChainIds.GroupSelected));
        return track.Rig.Containers![0];
    }

    [AvaloniaFact]
    public void NewContainerComesBehindTheFocusedRowAndShowsItsPane()
    {
        var (window, track, _) = Open();
        FxTestKit.List(window).Click(0);
        FxTestKit.PressButton(window, "NewContainerButton");
        var container = Assert.Single(track.Rig.Containers!);
        Assert.Equal(1, container.Start);
        Assert.Same(container, FxTestKit.List(window).Focused);
        Assert.True(DialogTestKit.Named<StackPanel>(window, "ContainerPane").IsVisible);
        Assert.False(DialogTestKit.Named<WrapPanel>(window, "PluginBar").IsVisible);
        Assert.Equal("Container", DialogTestKit.Named<TextBox>(window, "ContainerNameBox").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void GroupPutsTheSelectedRowsInOneContainerInOrderAndDissolveKeepsThePlugins()
    {
        var (window, track, _) = Open();
        var container = GroupFirstTwo(window, track);
        Assert.Equal(2, container.Count);
        Assert.Equal(4, FxTestKit.List(window).Count);   // the container row and the three plug-ins
        Assert.True(window.Run(FxChainIds.Dissolve));
        Assert.Null(track.Rig.Containers);
        Assert.Equal(3, track.Rig.Plugins.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void GroupRefusesRowsFromDifferentContainers()
    {
        var (window, track, _) = Open();
        GroupFirstTwo(window, track);
        var list = FxTestKit.List(window);
        list.Click(list.IndexOf(track.Rig.Plugins[0]));
        list.Click(list.IndexOf(track.Rig.Plugins[2]), ctrl: true);
        window.Run(FxChainIds.GroupSelected);
        Assert.Single(track.Rig.Containers!);   // a plug-in is never pulled out of a container others stay in
        window.Close();
    }

    [AvaloniaFact]
    public void RenameThroughTheNameBoxIsOneStepAndEnterApplies()
    {
        var (document, host, track) = FxTestKit.Song();
        var window = new FxChainWindow(host, track);
        window.Show();
        window.Run(FxChainIds.NewContainer);
        var box = DialogTestKit.Named<TextBox>(window, "ContainerNameBox");
        box.Text = "Amp chain";
        box.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = box });
        Assert.Equal("Amp chain", track.Rig.Containers![0].Name);
        Assert.True(document.Undo.CanUndo);
        Assert.Equal("Amp chain", FxTestKit.List(window).GetLogicalDescendantsOf<TextBlock>().Single(t => t.Name == "ContainerName").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void DeleteWithPluginsAsksFirstAndCancelKeepsEverything()
    {
        string? asked = null;
        var (window, track, engine) = Open(text => { asked = text; return MessageBoxAnswer.Cancel; });
        GroupFirstTwo(window, track);
        window.Run(FxChainIds.DeleteContainer);
        DialogTestKit.RunQueuedUiWork();
        Assert.Contains("Delete the container", asked);
        Assert.Contains("2 plug-ins", asked);
        Assert.Equal(3, track.Rig.Plugins.Count);
        window.Close();

        var (second, secondTrack, _) = Open(_ => MessageBoxAnswer.OK);
        GroupFirstTwo(second, secondTrack);
        second.Run(FxChainIds.DeleteContainer);
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(new[] { "Sample delay" }, secondTrack.Rig.Plugins.Select(p => p.Name));
        Assert.Null(secondTrack.Rig.Containers);
        second.Close();
    }

    [AvaloniaFact]
    public void RemoveButtonOnAContainerDeletesItWithItsPlugins()
    {
        var (window, track, _) = Open(_ => MessageBoxAnswer.OK);
        GroupFirstTwo(window, track);
        FxTestKit.PressButton(window, "RemoveButton");
        DialogTestKit.RunQueuedUiWork();
        Assert.Single(track.Rig.Plugins);
        window.Close();
    }

    [AvaloniaFact]
    public void ContainerTickBypassesItsPluginsAndTellsTheEngine()
    {
        var (window, track, engine) = Open();
        var container = GroupFirstTwo(window, track);
        var list = FxTestKit.List(window);
        var tick = list.RowAt(list.IndexOf(container)).GetLogicalDescendantsOf<CheckBox>().First(c => c.Name == "RowTick");
        FxTestKit.Press(window, tick);
        Assert.False(container.Enabled);
        Assert.All(track.Rig.Plugins.Take(2), p => Assert.True(p.GroupBypassed));
        Assert.False(track.Rig.Plugins[2].GroupBypassed);
        Assert.Contains(engine.Calls, c => c.StartsWith("enabled:"));
        window.Close();
    }

    [AvaloniaFact]
    public void ChevronCollapsesWithoutAnUndoStep()
    {
        var (document, host, track) = FxTestKit.Song();
        var window = new FxChainWindow(host, track);
        window.Show();
        var list = FxTestKit.List(window);
        list.Click(0);
        list.Click(1, shift: true);
        window.Run(FxChainIds.GroupSelected);
        var steps = document.Undo.CanUndo;
        var container = track.Rig.Containers![0];
        var chevron = list.RowAt(list.IndexOf(container)).GetLogicalDescendantsOf<Button>().First(b => b.Name == "ContainerChevron");
        FxTestKit.Press(window, chevron);
        Assert.True(container.Collapsed);
        Assert.Equal(2, list.Count);   // the container and the plug-in after it
        Assert.Equal(steps, document.Undo.CanUndo);
        window.Close();
    }

    [AvaloniaFact]
    public void RightClickMenuListsTheContainerCommandsWithGestures()
    {
        var (window, track, _) = Open();
        GroupFirstTwo(window, track);
        Assert.NotNull(DialogTestKit.Named<FxChainList>(window, "ChainList").ContextMenu);   // the menu fills when it opens, from the rows as they are then
        var headers = window.Containers.MenuItems().Select(i => i.Header as string).ToList();
        Assert.Contains("New container", headers);
        Assert.Contains("Put selected in a new container", headers);
        Assert.Contains(headers, h => h!.StartsWith("Dissolve container"));
        Assert.Contains("Rename container", headers);
        Assert.Contains("Delete container with its plug-ins…", headers);
        window.Close();
    }

    [AvaloniaFact]
    public void ContainerPresetsSaveAndLoadWithTheUndoStep()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var (document, host, track) = FxTestKit.Song();
        var window = new FxChainWindow(host, track);
        window.Dialogs.SavePresetName = (_, _) => System.Threading.Tasks.Task.FromResult<string?>("Duo");
        window.Dialogs.Ask = (_, _, _, _, _) => System.Threading.Tasks.Task.FromResult(MessageBoxAnswer.Yes);
        window.Dialogs.AskLoadPreset = (_, _, _, _, _) =>
            System.Threading.Tasks.Task.FromResult<FxPresetLoadAnswer?>(new FxPresetLoadAnswer(FxPresetLoadMode.Replace, Remember: false));
        window.Show();
        var list = FxTestKit.List(window);
        list.Click(0);
        list.Click(1, shift: true);
        window.Run(FxChainIds.GroupSelected);
        FxTestKit.PressButton(window, "ContainerSavePreset");
        DialogTestKit.RunQueuedUiWork();
        Assert.Contains("Duo", PresetFolderStore.Containers().Names());
        var saved = DialogTestKit.Named<ComboBox>(window, "ContainerPresetBox");
        Assert.Equal("Duo", saved.SelectedItem);   // the new preset is shown as chosen at once
        Assert.Single(saved.Items.OfType<string>(), i => i == "Duo");
        Assert.Equal(3, track.Rig.Plugins.Count);   // saving loaded nothing

        window.Run(FxChainIds.NewContainer);
        var empty = (ChainContainer)list.Focused!;
        var box = DialogTestKit.Named<ComboBox>(window, "ContainerPresetBox");
        box.SelectedItem = box.Items.OfType<string>().First(i => i == "Duo");
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(2, empty.Count);
        Assert.Equal(5, track.Rig.Plugins.Count);   // two came with the preset
        Assert.Equal("Duo", empty.Preset);
        window.Close();
    }

    [AvaloniaFact]
    public void ContainersSurviveASavedChainFile()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-containers-{Guid.NewGuid():N}.tfchain");
        try
        {
            var (window, track, _) = Open();
            GroupFirstTwo(window, track);
            window.Close();
            var saver = FxTestKit.Window(track, files: new FxTestKit.PickFile(file));
            saver.Show();
            FxTestKit.Choose(saver, "Save FX chain…");
            DialogTestKit.RunQueuedUiWork();
            saver.Close();

            var target = FxTestKit.Track();
            var loader = FxTestKit.Window(target, files: new FxTestKit.PickFile(file));
            loader.Show();
            FxTestKit.Choose(loader, "Load FX chain…");
            DialogTestKit.RunQueuedUiWork();
            var container = Assert.Single(target.Rig.Containers!);
            Assert.Equal((0, 2), (container.Start, container.Count));
            Assert.Equal(3, target.Rig.Plugins.Count);
            Assert.Null(target.Rig.Plugins[0].ChainContainers);   // unpacked onto the rig
            Assert.Equal(4, FxTestKit.List(loader).Count);

            FxTestKit.Choose(loader, "Add FX chain to the end…");
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(2, target.Rig.Containers!.Count);
            Assert.Equal(6, target.Rig.Plugins.Count);
            loader.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void LoadingReplacesOldContainersToo()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-replace-{Guid.NewGuid():N}.tfchain");
        try
        {
            PluginLibrary.SaveChain(file, FxChainWindow.SampleChain());
            var (window, track, _) = Open();
            GroupFirstTwo(window, track);
            window.Dialogs.Files = new FxTestKit.PickFile(file);
            FxTestKit.Choose(window, "Load FX chain…");
            DialogTestKit.RunQueuedUiWork();
            Assert.Null(track.Rig.Containers);   // no empty container of the old chain stays behind
            Assert.Equal(3, FxTestKit.List(window).Count);
            window.Close();
        }
        finally { File.Delete(file); }
    }
}
