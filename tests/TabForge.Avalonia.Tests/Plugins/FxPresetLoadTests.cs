using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Avalonia.Views.Presets;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Services.Features;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Replace / Add question of an FX preset load, the merge it chooses (Core), the result through every load path, the GM rule for
/// both modes, one undo step, Cancel and the remembered setting with its Settings row.
/// Does not own: the production classes these exercise.
/// Tests: this class.
/// </summary>
[Collection("PluginLibraryRoot")]   // PluginLibrary.RootOverride is process-wide: run alone so no parallel test resets it
public class FxPresetLoadTests
{
    private static PluginSlot Effect(string name = "Preset reverb") => new() { Name = name, Type = PluginSlotType.Effect };
    private static PluginSlot Instrument(string name = "Preset piano") => new() { Name = name, Type = PluginSlotType.Instrument };

    private static string ChainFile(params PluginSlot[] plugins)
    {
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-load-{Guid.NewGuid():N}.tfchain");
        PluginLibrary.SaveChain(file, plugins);
        return file;
    }

    private static (FxChainWindow Window, TrackModel Track) Open(IEnumerable<PluginSlot>? chain = null, PluginSettings? settings = null)
    {
        var track = FxTestKit.Track(chain);
        var window = FxTestKit.Window(track, FxTestKit.Host(track, null, settings));
        window.Show();
        return (window, track);
    }

    private static void Load(FxChainWindow window, string file, FxPresetLoadAnswer? answer)
    {
        window.Dialogs.AskLoadPreset = (_, _, _, _, _) => Task.FromResult(answer);
        window.Dialogs.Files = new FxTestKit.PickFile(file);
        FxTestKit.Choose(window, "Load FX chain…");
        DialogTestKit.RunQueuedUiWork();
    }

    [Fact]
    public void ReplaceReturnsOnlyTheLoadedChain()
    {
        var existing = new RigPreset { Plugins = FxChainWindow.SampleChain() };
        var loaded = new[] { Effect("One"), Effect("Two") };
        var result = FxChainMerge.Merge(existing, loaded, FxPresetLoadMode.Replace);
        Assert.Equal(new[] { "One", "Two" }, result.Plugins.Select(p => p.Name));
        Assert.Same(loaded[0], result.Plugins[0]);
        Assert.Same(loaded[1], result.Plugins[1]);
    }

    [Fact]
    public void AddKeepsTheExistingSlotsAndAppendsTheLoadedChainInOrder()
    {
        var first = new PluginSlot { Name = "Existing", Enabled = false, State = "state", OutputDb = -3, ActiveOn = new ActiveOnRule { DriverKind = ActiveOnDriverKind.Asio } };
        var existing = new RigPreset { Plugins = new List<PluginSlot> { first } };
        var loaded = new[] { Effect("One"), Effect("Two") };
        var result = FxChainMerge.Merge(existing, loaded, FxPresetLoadMode.Add);
        Assert.Equal(new[] { "Existing", "One", "Two" }, result.Plugins.Select(p => p.Name));
        Assert.Same(first, result.Plugins[0]);
        Assert.False(first.Enabled);
        Assert.Equal("state", first.State);
        Assert.Equal(-3, first.OutputDb);
        Assert.Equal(ActiveOnDriverKind.Asio, first.ActiveOn!.DriverKind);
        Assert.Same(loaded[0], result.Plugins[1]);
        Assert.Same(loaded[1], result.Plugins[2]);
    }

    [Fact]
    public void AddKeepsTheLoadedContainersAndTheirSettings()
    {
        var loaded = new[] { Effect("One"), Effect("Two") };
        loaded[0].ChainContainers = new List<ChainContainer> { new() { Name = "Box", Start = 0, Count = 2, Enabled = false, Mix = 60 } };
        var result = FxChainMerge.Merge(new RigPreset(), loaded, FxPresetLoadMode.Add);
        Assert.NotNull(result.Containers);
        Assert.Equal("Box", Assert.Single(result.Containers!).Name);
        Assert.False(result.Containers![0].Enabled);
        Assert.Equal(60, result.Containers[0].Mix);
        Assert.Equal(2, result.Containers[0].Count);
    }

    [Fact]
    public void AddGivesClashingIdsAndContainerNamesTheirOwn()
    {
        var samePluginId = Guid.NewGuid().ToString("N");
        var sameContainerId = Guid.NewGuid().ToString("N");
        var existing = new RigPreset
        {
            Plugins = new List<PluginSlot> { new() { Name = "Existing", Id = samePluginId } },
            Containers = new List<ChainContainer> { new() { Name = "Box", Id = sameContainerId, Start = 0, Count = 1 } },
        };
        var loaded = new[] { Effect("One") };
        loaded[0].Id = samePluginId;
        loaded[0].ChainContainers = new List<ChainContainer> { new() { Name = "Box", Id = sameContainerId, Start = 0, Count = 1 } };
        var result = FxChainMerge.Merge(existing, loaded, FxPresetLoadMode.Add);
        var containers = Assert.IsAssignableFrom<List<ChainContainer>>(result.Containers);
        Assert.Equal(2, result.Plugins.Select(p => p.Id).Distinct().Count());
        Assert.Equal(2, containers.Select(c => c.Id).Distinct().Count());
        Assert.Equal(2, containers.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [AvaloniaFact]
    public void ThePromptShowsTheApprovedWordsAndDefaultsToReplace()
    {
        var window = new FxPresetLoadPromptWindow("Warm", 3, twoInstruments: true);
        window.Show();
        Assert.Equal("Load \"Warm\"", window.Title);
        Assert.Equal(FxPresetLoadPresenter.Question(3), DialogTestKit.Named<TextBlock>(window, "PresetLoadQuestion").Text);
        Assert.True(DialogTestKit.Named<RadioCard>(window, "ReplaceCard").IsSelected);
        Assert.False(DialogTestKit.Named<RadioCard>(window, "AddCard").IsSelected);
        Assert.Equal(FxPresetLoadPresenter.ReplaceHint(3), DialogTestKit.Named<TextBlock>(window, "ReplaceHint").Text);
        Assert.Equal(FxPresetLoadPresenter.AddHint(3), DialogTestKit.Named<TextBlock>(window, "AddHint").Text);
        Assert.Equal(FxPresetLoadPresenter.TwoInstrumentsLine, DialogTestKit.Named<TextBlock>(window, "TwoInstrumentsLine").Text);
        Assert.Equal(FxPresetLoadPresenter.RememberText, DialogTestKit.Named<CheckBox>(window, "RememberBox").Content);
        var load = DialogTestKit.Named<Button>(window, "LoadButton");
        Assert.True(load.IsDefault);
        Assert.Contains(AvaloniaDefaultLooks.AccentLook, load.Classes);
        Assert.True(DialogTestKit.Named<Button>(window, "CancelButton").IsCancel);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePromptWithoutTwoInstrumentsHidesThatLine()
    {
        var window = new FxPresetLoadPromptWindow("Warm", 2, twoInstruments: false);
        window.Show();
        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Name == "TwoInstrumentsLine");
        window.Close();
    }

    [AvaloniaFact]
    public void ThePromptAnswersAddAndRemembersTheTick()
    {
        var window = new FxPresetLoadPromptWindow("Warm", 2, twoInstruments: false);
        window.Show();
        DialogTestKit.Named<RadioCard>(window, "AddCard").Select();
        DialogTestKit.Named<CheckBox>(window, "RememberBox").IsChecked = true;
        DialogTestKit.Click(DialogTestKit.Named<Button>(window, "LoadButton"));
        Assert.Equal(new FxPresetLoadAnswer(FxPresetLoadMode.Add, Remember: true), window.Answer);
    }

    [Fact]
    public void TheStoredChoiceReadsAskReplaceAndAdd()
    {
        Assert.Null(FxPresetLoadPresenter.Remembered(FxPresetLoadChoice.Ask));
        Assert.Equal(FxPresetLoadMode.Replace, FxPresetLoadPresenter.Remembered(FxPresetLoadChoice.Replace));
        Assert.Equal(FxPresetLoadMode.Add, FxPresetLoadPresenter.Remembered(FxPresetLoadChoice.Add));
        Assert.Equal(FxPresetLoadChoice.Ask, FxPresetLoadChoice.Normalize("something else"));
        Assert.Equal(FxPresetLoadChoice.Add, FxPresetLoadPresenter.Store(FxPresetLoadMode.Add));
    }

    [AvaloniaFact]
    public void AnEmptyChainLoadsWithoutAQuestion()
    {
        var file = ChainFile(Effect("Loaded"));
        try
        {
            var (window, track) = Open();
            var asked = 0;
            window.Dialogs.AskLoadPreset = (_, _, _, _, _) => { asked++; return Task.FromResult<FxPresetLoadAnswer?>(null); };
            window.Dialogs.Files = new FxTestKit.PickFile(file);
            FxTestKit.Choose(window, "Load FX chain…");
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(0, asked);
            Assert.Equal(new[] { "Loaded" }, track.Rig.Plugins.Select(p => p.Name));
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void CancelChangesNothingAndTheQuestionSeesTheCurrentChain()
    {
        var file = ChainFile(Effect("Loaded"));
        try
        {
            var (window, track) = Open(FxChainWindow.SampleChain());
            var asked = 0;
            window.Dialogs.AskLoadPreset = (_, name, count, two, _) => { asked++; Assert.Equal("Loaded", name); Assert.Equal(3, count); Assert.False(two); return Task.FromResult<FxPresetLoadAnswer?>(null); };
            window.Dialogs.Files = new FxTestKit.PickFile(file);
            FxTestKit.Choose(window, "Load FX chain…");
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(1, asked);
            Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample delay" }, track.Rig.Plugins.Select(p => p.Name));
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void ReplaceLoadsOnlyThePresetAndAddKeepsThenAppends()
    {
        var file = ChainFile(Effect("Loaded"));
        try
        {
            var (window, track) = Open(FxChainWindow.SampleChain());
            Load(window, file, new FxPresetLoadAnswer(FxPresetLoadMode.Replace, Remember: false));
            Assert.Equal(new[] { "Loaded" }, track.Rig.Plugins.Select(p => p.Name));

            track.Rig.Plugins.Add(Effect("Kept"));
            Load(window, file, new FxPresetLoadAnswer(FxPresetLoadMode.Add, Remember: false));
            Assert.Equal(new[] { "Loaded", "Kept", "Loaded" }, track.Rig.Plugins.Select(p => p.Name));
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaTheory]
    [InlineData(FxPresetLoadMode.Replace)]
    [InlineData(FxPresetLoadMode.Add)]
    public void BothModesAreOneUndoStep(FxPresetLoadMode mode)
    {
        var file = ChainFile(Effect("Loaded"));
        try
        {
            var (document, host, track) = FxTestKit.Song();
            document.MarkClean();
            var window = FxTestKit.Window(track, host, files: new FxTestKit.PickFile(file));
            window.Dialogs.AskLoadPreset = (_, _, _, _, _) => Task.FromResult<FxPresetLoadAnswer?>(new FxPresetLoadAnswer(mode, Remember: false));
            window.Show();
            FxTestKit.Choose(window, "Load FX chain…");
            DialogTestKit.RunQueuedUiWork();
            Assert.True(document.Undo.CanUndo);
            DocumentEdits.Undo(document);
            Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample delay" }, document.Project.Tracks[0].Rig.Plugins.Select(p => p.Name));
            Assert.False(document.Undo.CanUndo);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaTheory]
    [InlineData(FxPresetLoadMode.Replace, true, true, false)]
    [InlineData(FxPresetLoadMode.Add, true, true, false)]
    [InlineData(FxPresetLoadMode.Replace, false, true, true)]
    [InlineData(FxPresetLoadMode.Add, false, true, true)]
    public void ALoadedInstrumentSwitchesGmOffAndEffectsLeaveIt(FxPresetLoadMode mode, bool instrument, bool before, bool after)
    {
        var file = ChainFile(instrument ? Instrument() : Effect());
        try
        {
            var (window, track) = Open(new[] { Effect("Existing") });
            track.MidiSound = before;
            Load(window, file, new FxPresetLoadAnswer(mode, Remember: false));
            Assert.Equal(after, track.MidiSound);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void TwoInstrumentsInTheChainAndThePresetShowsTheNote()
    {
        var file = ChainFile(Instrument("Loaded"));
        try
        {
            var (window, _) = Open(new[] { Instrument("Existing") });
            var two = false;
            window.Dialogs.Files = new FxTestKit.PickFile(file);
            window.Dialogs.AskLoadPreset = (_, _, _, showTwo, _) => { two = showTwo; return Task.FromResult<FxPresetLoadAnswer?>(null); };
            FxTestKit.Choose(window, "Load FX chain…");
            DialogTestKit.RunQueuedUiWork();
            Assert.True(two);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaTheory]
    [InlineData(FxPresetLoadChoice.Replace, FxPresetLoadMode.Replace)]
    [InlineData(FxPresetLoadChoice.Add, FxPresetLoadMode.Add)]
    public void ARememberedSettingSkipsTheQuestionForEachValue(string stored, FxPresetLoadMode expected)
    {
        var file = ChainFile(Effect("Loaded"));
        try
        {
            var settings = new PluginSettings { FxPresetLoad = stored };
            var (window, track) = Open(FxChainWindow.SampleChain(), settings);
            var asked = 0;
            window.Dialogs.Files = new FxTestKit.PickFile(file);
            window.Dialogs.AskLoadPreset = (_, _, _, _, _) => { asked++; return Task.FromResult<FxPresetLoadAnswer?>(null); };
            FxTestKit.Choose(window, "Load FX chain…");
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(0, asked);
            Assert.Equal(expected == FxPresetLoadMode.Replace ? 1 : 4, track.Rig.Plugins.Count);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void RememberTickedStoresTheChoiceAndTheSettingsRowChangesItBack()
    {
        var file = ChainFile(Effect("Loaded"));
        try
        {
            var settings = new PluginSettings();
            var (window, track) = Open(FxChainWindow.SampleChain(), settings);
            Load(window, file, new FxPresetLoadAnswer(FxPresetLoadMode.Add, Remember: true));
            Assert.Equal(FxPresetLoadChoice.Add, settings.FxPresetLoad);

            var row = SettingsCatalog.Build(new AppSettings { Plugins = settings }).Single(r => r.Key == "vst.fxpresetload");
            Assert.Equal(SettingsCatalog.AudioVst, row.Category);
            Assert.Equal(FxPresetLoadChoice.Add, row.Get());
            row.Set(FxPresetLoadChoice.Ask);
            Assert.Equal(FxPresetLoadChoice.Ask, settings.FxPresetLoad);

            var asked = 0;
            window.Dialogs.Files = new FxTestKit.PickFile(file);
            window.Dialogs.AskLoadPreset = (_, _, _, _, _) => { asked++; return Task.FromResult<FxPresetLoadAnswer?>(new FxPresetLoadAnswer(FxPresetLoadMode.Replace, Remember: false)); };
            FxTestKit.Choose(window, "Load FX chain…");
            DialogTestKit.RunQueuedUiWork();
            Assert.Equal(1, asked);   // Ask again: the row changed it back
            Assert.Equal(new[] { "Loaded" }, track.Rig.Plugins.Select(p => p.Name));
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void TheChainsMenuUsesTheSameQuestion()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        Directory.CreateDirectory(PluginLibrary.ChainsFolder);
        PluginLibrary.SaveChain(Path.Combine(PluginLibrary.ChainsFolder, "Menu.tfchain"), new List<PluginSlot> { Effect("Menu load") });
        var (window, track) = Open(FxChainWindow.SampleChain());
        var asked = 0;
        window.Dialogs.AskLoadPreset = (_, name, count, _, _) => { asked++; Assert.Equal("Menu", name); Assert.Equal(3, count); return Task.FromResult<FxPresetLoadAnswer?>(new FxPresetLoadAnswer(FxPresetLoadMode.Replace, Remember: false)); };
        FxTestKit.Choose(window, "Menu");
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(1, asked);
        Assert.Equal(new[] { "Menu load" }, track.Rig.Plugins.Select(p => p.Name));
        window.Close();
    }

    [AvaloniaFact]
    public void TheReaperImportUsesTheSameQuestion()
    {
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-load-{Guid.NewGuid():N}.RfxChain");
        File.WriteAllText(file, "BYPASS 0 0\n<VST \"VST: Sample (Sample)\" sample.dll 0 \"\" 1<00> \"\"\n>\n");
        try
        {
            var settings = new PluginSettings
            {
                ScanCache = new List<KnownPlugin> { new() { Name = "Sample", Path = @"C:\vst\sample.dll", Format = "VST2", Vendor = "Sample" } },
            };
            var (window, track) = Open(FxChainWindow.SampleChain(), settings);
            var asked = 0;
            window.Dialogs.AskLoadPreset = (_, name, count, _, _) => { asked++; Assert.Equal("sample", name); Assert.Equal(3, count); return Task.FromResult<FxPresetLoadAnswer?>(new FxPresetLoadAnswer(FxPresetLoadMode.Replace, Remember: false)); };
            window.Dialogs.Files = new FxTestKit.PickFile(file);
            FxTestKit.Choose(window, "Load FX chain…");
            Assert.True(SpinWait.SpinUntil(() => { DialogTestKit.RunQueuedUiWork(); return asked == 1; }, 10000));
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void TheContainerPresetUsesTheSameQuestionAndCancelLeavesItAlone()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var path = PresetFolderStore.Containers().PathOf("Box");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        PluginLibrary.SaveChain(path, new List<PluginSlot> { Effect("Container load") });
        var (window, track) = Open(FxChainWindow.SampleChain());
        window.Run(FxChainIds.NewContainer);
        var asked = 0;
        window.Dialogs.AskLoadPreset = (_, name, count, _, _) => { asked++; Assert.Equal("Box", name); Assert.Equal(3, count); return Task.FromResult<FxPresetLoadAnswer?>(null); };
        var box = DialogTestKit.Named<ComboBox>(window, "ContainerPresetBox");
        box.SelectedItem = box.Items.OfType<string>().First(i => i == "Box");
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(1, asked);
        Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample delay" }, track.Rig.Plugins.Select(p => p.Name));
        window.Close();
    }
}
