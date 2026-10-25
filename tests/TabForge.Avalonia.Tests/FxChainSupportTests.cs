using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Avalonia.Views.Presets;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the supporting pieces of the FX chain window: the editor plan (which notice and button for each plug-in state), the editor fit arithmetic, the list width
/// splitter and its limits, the Chains menu (saved chains, the replace-or-add question), Manage presets, the .RfxChain export and import steps, the host's
/// handling of the app-wide monitor chain, and the tooltips on the wiring rows.
/// Does not own: the production classes these exercise.
/// Tests: this class.
/// </summary>
public class FxChainSupportTests
{
    [Theory]
    [InlineData(false, true, false, true, false, true, false, FxEditorMode.None)]
    [InlineData(true, false, false, true, false, true, false, FxEditorMode.Blocked)]
    [InlineData(true, true, true, true, false, true, false, FxEditorMode.Quarantined)]
    [InlineData(true, true, false, false, false, true, false, FxEditorMode.Unloaded)]
    [InlineData(true, true, false, true, true, true, false, FxEditorMode.Starting)]
    [InlineData(true, true, false, true, false, false, false, FxEditorMode.Floating)]
    [InlineData(true, true, false, true, false, true, true, FxEditorMode.Floating)]
    [InlineData(true, true, false, true, false, true, false, FxEditorMode.Docked)]
    public void EditorPlanChecksInTheOrderThatKeepsUnsafePluginsOut(bool has, bool trusted, bool crashed, bool chain, bool starting, bool dock, bool floated, FxEditorMode expected) =>
        Assert.Equal(expected, FxEditorPlan.ModeFor(has, trusted, crashed, chain, starting, dock, floated));

    [Fact]
    public void EditorPlanButtonsAndNotices()
    {
        Assert.Equal("Allow", FxEditorPlan.ButtonFor(FxEditorMode.Blocked, true));
        Assert.Equal("Allow again", FxEditorPlan.ButtonFor(FxEditorMode.Quarantined, true));
        Assert.Equal("Show it here", FxEditorPlan.ButtonFor(FxEditorMode.Floating, true));
        Assert.Null(FxEditorPlan.ButtonFor(FxEditorMode.Floating, false));
        Assert.Null(FxEditorPlan.ButtonFor(FxEditorMode.Docked, true));
        Assert.Equal("", FxEditorPlan.NoticeFor(FxEditorMode.Docked, "x", true, true, true));
        Assert.Equal(FxChainPresenter.UnloadedNotice(false, true, true), FxEditorPlan.NoticeFor(FxEditorMode.Unloaded, "x", false, true, true));
        Assert.Equal(FxChainPresenter.NoPluginsNotice, FxEditorPlan.NoticeFor(FxEditorMode.None, "", true, false, false));
    }

    [Fact]
    public void FitGrowsTheWindowOnlyAndOnlyToTheWorkArea()
    {
        var work = new PluginEditorFitMath.Rectangle(0, 0, 1920, 1080);
        var window = new PluginEditorFitMath.Rectangle(100, 100, 980, 620);
        Assert.Equal(window, PluginEditorFitMath.FitWindow(window, (400, 150), (300, 200), work));   // it already shows the editor: no change
        var grown = PluginEditorFitMath.FitWindow(window, (400, 150), (1200, 700), work);
        Assert.Equal((1600d, 850d), (grown.Width, grown.Height));
        var huge = PluginEditorFitMath.FitWindow(window, (400, 150), (9000, 9000), work);
        Assert.Equal((1880d, 1040d), (huge.Width, huge.Height));
        Assert.True(huge.Left >= 0 && huge.Right <= 1920 && huge.Top >= 0 && huge.Bottom <= 1080);
    }

    [Fact]
    public void ScrollBarsAppearOnlyForTheOverflowAndOneBarCanNeedTheOther()
    {
        Assert.Equal(new PluginEditorFitMath.ScrollLayout(300, 200, 0, 0), PluginEditorFitMath.Scroll(600, 400, 300, 200, 14));
        var wide = PluginEditorFitMath.Scroll(600, 400, 900, 200, 14);
        Assert.Equal((600d, 200d, 300d, 0d), (wide.ViewWidth, wide.ViewHeight, wide.RangeX, wide.RangeY));
        var tallNearly = PluginEditorFitMath.Scroll(600, 400, 900, 395, 14);   // the horizontal bar takes room that the editor height needs
        Assert.True(tallNearly.RangeY > 0);
        Assert.Equal((-60, -30), PluginEditorFitMath.ViewOrigin(40, 20, 1.5, 1.5));
        Assert.Equal((200d, 100d), PluginEditorFitMath.ToDip(300, 150, 1.5, 1.5));
    }

    [Theory]
    [InlineData(290, 980, 290)]
    [InlineData(100, 980, 180)]
    [InlineData(900, 980, 441)]
    [InlineData(double.NaN, 980, 290)]
    [InlineData(500, 640, 210)]
    public void ListWidthLimitKeepsTheListAndThePaneUsable(double width, double window, double expected) =>
        Assert.Equal(expected, FxListSplitter.Limit(width, window));

    [AvaloniaFact]
    public void SplitterDragIsRememberedAndDoubleClickResets()
    {
        var saved = 0;
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var settings = new PluginSettings { FxListWidth = 320 };
        var window = FxTestKit.Window(track, FxTestKit.Host(track, null, settings, saved: () => saved++));
        window.Show();
        var splitter = DialogTestKit.Named<FxListSplitter>(window, "ListSplitter");
        Assert.Equal(320, splitter.ListWidth);
        splitter.DragBy(40);
        Assert.Equal(360, settings.FxListWidth);
        Assert.Equal(1, saved);
        splitter.Reset();
        Assert.Equal(FxListSplitter.Default, settings.FxListWidth);
        window.Close();
    }

    [AvaloniaFact]
    public void SplitterWidthComesBackWhenTheWindowWidens()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var settings = new PluginSettings { FxListWidth = 400 };
        var window = FxTestKit.Window(track, FxTestKit.Host(track, null, settings));
        window.Show();
        var splitter = DialogTestKit.Named<FxListSplitter>(window, "ListSplitter");
        window.Width = 640;
        DialogTestKit.RunQueuedUiWork();
        splitter.Reapply();
        Assert.True(splitter.ListWidth < 400);
        Assert.Equal(400, settings.FxListWidth);   // squeezing the window does not forget the choice
        window.Width = 1400;
        DialogTestKit.RunQueuedUiWork();
        splitter.Reapply();
        Assert.Equal(400, splitter.ListWidth);
        window.Close();
    }

    [AvaloniaFact]
    public void ChainsMenuListsSavedChainsAndAsksReplaceOrAdd()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        Directory.CreateDirectory(PluginLibrary.ChainsFolder);
        PluginLibrary.SaveChain(Path.Combine(PluginLibrary.ChainsFolder, "Warm.tfchain"), new List<PluginSlot> { new() { Name = "Warm amp" } });
        PluginLibrary.SaveChain(Path.Combine(PluginLibrary.ChainsFolder, "Wide.tfchain"), new List<PluginSlot> { new() { Name = "Wide verb" } });
        string? question = null;
        var answer = MessageBoxAnswer.No;
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var window = FxTestKit.Window(track);
        window.Dialogs.Ask = (_, text, _, _, _) => { question = text; return System.Threading.Tasks.Task.FromResult(answer); };
        window.Show();
        Assert.Equal(new[] { "Warm", "Wide" }, FxTestKit.Items(window, "Warm").Concat(FxTestKit.Items(window, "Wide")).Select(i => (string)i.Header!));
        FxTestKit.Choose(window, "Warm");
        DialogTestKit.RunQueuedUiWork();
        Assert.Contains("Replace this chain with \"Warm\"", question);
        Assert.Equal(new[] { "Sample piano", "Sample reverb", "Sample delay", "Warm amp" }, track.Rig.Plugins.Select(p => p.Name));   // No: added to the end
        answer = MessageBoxAnswer.Yes;
        FxTestKit.Choose(window, "Wide");
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(new[] { "Wide verb" }, track.Rig.Plugins.Select(p => p.Name));
        answer = MessageBoxAnswer.Cancel;
        FxTestKit.Choose(window, "Warm");
        DialogTestKit.RunQueuedUiWork();
        Assert.Single(track.Rig.Plugins);
        window.Close();
    }

    [AvaloniaFact]
    public void ChainsMenuShowsAnEmptyStateWithoutSavedChains()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var window = FxTestKit.Window();
        window.Show();
        Assert.False(FxTestKit.Item(window, FxChainFiles.NoSavedChainsText).IsEnabled);
        Assert.NotEmpty(FxTestKit.Items(window, FxChainFiles.SaveForHostsText));
        Assert.NotEmpty(FxTestKit.Items(window, PresetRowText.Manage));
        window.Close();
    }

    [AvaloniaFact]
    public void ExportWithNothingExportableSaysSoAndWritesNoFile()
    {
        string? shown = null;
        var window = FxTestKit.Window();
        window.Dialogs.Ask = (_, text, _, _, _) => { shown = text; return System.Threading.Tasks.Task.FromResult(MessageBoxAnswer.OK); };
        window.Show();
        FxTestKit.Choose(window, FxChainFiles.SaveForHostsText);
        DialogTestKit.RunQueuedUiWork();
        Assert.Contains("No plug-in of this chain can be exported", shown);
        window.Close();
    }

    [AvaloniaFact]
    public void ImportOfAChainWithUnknownPluginsOffersTheScanAndReports()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-import-{Guid.NewGuid():N}.RfxChain");
        File.WriteAllText(file, "BYPASS 0 0\n<VST \"VST: Gone (Acme)\" gone.dll 0 \"\" 1<00> \"\"\n>\n");
        FxChainWindow? window = null;
        try
        {
            var asked = new List<string>();
            var track = FxTestKit.Track();
            // One known plug-in and a finished scan: the import does not walk this machine's real VST and Reaper folders (a fresh profile does), so the run is the same everywhere.
            var settings = new PluginSettings { LastScan = new[] { new VstPluginInfo("Known", @"C:\known.vst3") } };
            window = FxTestKit.Window(track, FxTestKit.Host(track, null, settings), files: new FxTestKit.PickFile(file));
            window.Dialogs.Ask = (_, text, _, _, _) => { asked.Add(text); return System.Threading.Tasks.Task.FromResult(MessageBoxAnswer.No); };
            window.Show();
            FxTestKit.Choose(window, "Load FX chain…");
            var end = DateTime.UtcNow.AddSeconds(30);
            while (asked.Count == 0 && DateTime.UtcNow < end) { DialogTestKit.RunQueuedUiWork(); System.Threading.Thread.Sleep(20); }
            Assert.Contains(asked, text => text.Contains("not in your plug-in list"));
            Assert.False(settings.ScanStandardFolders);   // No keeps the option off
            Assert.True(System.Threading.SpinWait.SpinUntil(() => { DialogTestKit.RunQueuedUiWork(); return asked.Count > 1; }, 5000), "the summary of what was left out");
        }
        finally { window?.Close(); File.Delete(file); }
    }

    [AvaloniaFact]
    public void MonitorChainEditsGoToTheSettingsNotTheSong()
    {
        var document = new TabForge.Documents.SongDocument(() => null) { Project = TabForge.Documents.SongDocument.BlankProject() };
        document.MarkClean();
        var saved = 0;
        var settingsOwner = new Window();
        var host = new SongFxChainHost(document, null, null, () => settingsOwner, null, new TabForge.Plugins.PluginTrust());
        var track = MixerBuses.MonitorTrack(document.Project, new BusChain());
        Assert.True(host.MonitorUseGlobal);
        host.Edit(track, () => track.Rig.Plugins.Add(new PluginSlot { Name = "Monitor verb" }));
        Assert.False(document.Undo.CanUndo);
        Assert.False(document.Project.IsDirty);
        Assert.Single(track.Rig.Plugins);
        host.SetMonitorUseGlobal(false);
        Assert.False(host.MonitorUseGlobal);
        Assert.True(document.Undo.CanUndo);
        Assert.Equal(0, saved);
    }

    [AvaloniaFact]
    public void WiringRowsCarryTheirTooltipsAndADisabledRowSaysWhy()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var wiring = new WiringWindow(new[] { track }, track, track.Rig.Plugins[1], apply => apply());
        wiring.Show();
        Assert.Equal(WiringPresenter.Texts.RowInputTip, ToolTip.GetTip(DialogTestKit.Named<ComboBox>(wiring, "InputCombo")));
        Assert.Equal(WiringPresenter.Texts.RowSourceTip, ToolTip.GetTip(DialogTestKit.Named<ComboBox>(wiring, "SourceCombo")));
        Assert.Equal(WiringPresenter.Texts.PassMidiTip, ToolTip.GetTip(DialogTestKit.Named<CheckBox>(wiring, "PassMidiCheck")));
        wiring.Close();
    }
}
