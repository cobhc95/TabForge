using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Add path of the FX chain window and the Add plug-in session: the chosen plug-in becomes a slot (approved, identified when the scan did not tell),
/// lands on the chain as one undo step with the chain switched on and the GM sound following the instrument, the limit and Cancel; and the session's list,
/// folders, remembered list and single-file choice.
/// Does not own: the window, the browser's drawing (PluginBrowserWindowTests) or trust (PluginTrust).
/// Tests: this class.
/// </summary>
public class FxAddPluginTests
{
    private static string TempPlugin(string extension = ".dll")
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-fx-addplugin", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "Plug" + extension);
        File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4 });
        return file;
    }

    private static (FxChainWindow Window, TrackModel Track, PluginSettings Settings, SongDocument Document) Open(VstPluginInfo? choice, int saved = 0)
    {
        var (document, song, track) = FxTestKit.Song();
        track.Rig.Plugins.Clear();
        document.MarkClean();
        var settings = new PluginSettings();
        var host = new StandaloneFxChainHost(settings, new HotkeySettings(), new TabForge.Plugins.PluginTrust(), new FxTestKit.FakeEngine(), new[] { track }, _ => { });
        var window = FxTestKit.Window(track, host);
        window.Dialogs.ChoosePlugin = (_, _) => Task.FromResult(choice);
        window.Show();
        return (window, track, settings, document);
    }

    [AvaloniaFact]
    public void AddingAnEffectApprovesItPutsItAtTheEndAndSwitchesTheChainOn()
    {
        var file = TempPlugin();
        var (window, track, settings, _) = Open(new VstPluginInfo("Plate", file, "VST2", "Acme", "Effect"));
        track.Rig.Plugins.Add(new PluginSlot { Name = "Existing", Type = PluginSlotType.Effect });
        FxTestKit.PressButton(window, "AddButton");
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(new[] { "Existing", "Plate" }, track.Rig.Plugins.Select(p => p.Name));
        var added = track.Rig.Plugins[1];
        Assert.Equal((PluginSlotType.Effect, "Acme", "VST2"), (added.Type, added.Vendor, added.Format));
        Assert.Equal(SoundSources.Plugins, track.SoundSource);
        Assert.Contains(settings.ApprovedPluginPaths, p => string.Equals(p, file, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, FxTestKit.List(window).IndexOf(added));   // the new plug-in is the focused row
        window.Close();
    }

    [AvaloniaFact]
    public void AnInstrumentGoesFirstAndSwitchesTheGmSoundOffThenOnAgainWhenItLeaves()
    {
        var file = TempPlugin();
        var (window, track, _, _) = Open(new VstPluginInfo("Grand", file, "VST2", "Acme", "Instrument"));
        track.Rig.Plugins.Add(new PluginSlot { Name = "Existing", Type = PluginSlotType.Effect });
        Assert.True(track.MidiSound);
        FxTestKit.PressButton(window, "AddButton");
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal("Grand", track.Rig.Plugins[0].Name);
        Assert.False(track.MidiSound);
        FxTestKit.List(window).Click(0);
        FxTestKit.PressButton(window, "RemoveButton");
        Assert.True(track.MidiSound);   // the last instrument left the chain: the GM sound plays again
        window.Close();
    }

    [AvaloniaFact]
    public void AddIsOneUndoStep()
    {
        var file = TempPlugin();
        var (document, song, track) = FxTestKit.Song();
        track.Rig.Plugins.Clear();
        var window = FxTestKit.Window(track, song);
        window.Dialogs.ChoosePlugin = (_, _) => Task.FromResult<VstPluginInfo?>(new VstPluginInfo("Plate", file, "VST2", "Acme", "Effect"));
        window.Show();
        FxTestKit.PressButton(window, "AddButton");
        DialogTestKit.RunQueuedUiWork();
        Assert.True(document.Undo.CanUndo);
        DocumentEdits.Undo(document);
        Assert.Empty(document.Project.Tracks[0].Rig.Plugins);
        Assert.False(document.Undo.CanUndo);
        window.Close();
    }

    [AvaloniaFact]
    public void ANameFromTheScanWithoutARoleIsIdentifiedByTheProbe()
    {
        var file = TempPlugin();
        PluginCatalog.ProbeOverride = (_, hash, _) => hash.Length == 64 ? ("Instrument", "Probed Vendor", "Probed") : null;
        try
        {
            var (window, track, settings, _) = Open(new VstPluginInfo("Mystery", file, "VST2", "", ""));
            FxTestKit.PressButton(window, "AddButton");
            DialogTestKit.RunQueuedUiWork();
            var added = Assert.Single(track.Rig.Plugins);
            Assert.Equal((PluginSlotType.Instrument, "Probed Vendor"), (added.Type, added.Vendor));
            Assert.Contains(settings.Probed, k => string.Equals(k.Path, file, StringComparison.OrdinalIgnoreCase) && k.Role == "Instrument");
            window.Close();
        }
        finally { PluginCatalog.ProbeOverride = null; }
    }

    [AvaloniaFact]
    public void CancelAddsNothing()
    {
        var (window, track, settings, document) = Open(null);
        FxTestKit.PressButton(window, "AddButton");
        DialogTestKit.RunQueuedUiWork();
        Assert.Empty(track.Rig.Plugins);
        Assert.Empty(settings.ApprovedPluginPaths);
        Assert.False(document.Undo.CanUndo);
        window.Close();
    }

    [AvaloniaFact]
    public void AddStopsAtThePluginLimit()
    {
        var file = TempPlugin();
        var (window, track, _, _) = Open(new VstPluginInfo("Plate", file, "VST2", "Acme", "Effect"));
        for (var i = 0; i < InputLimits.MaxPluginsPerTrack; i++) track.Rig.Plugins.Add(new PluginSlot { Name = $"P{i}" });
        FxTestKit.PressButton(window, "AddButton");
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(InputLimits.MaxPluginsPerTrack, track.Rig.Plugins.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void ABrokenAddShowsTheReasonInsteadOfLosingIt()
    {
        string? shown = null;
        var (window, track, _, _) = Open(null);
        window.Dialogs.ChoosePlugin = (_, _) => throw new InvalidOperationException("scanner broke");
        window.Dialogs.Ask = (_, text, _, _, _) => { shown = text; return Task.FromResult(MessageBoxAnswer.OK); };
        FxTestKit.PressButton(window, "AddButton");
        DialogTestKit.RunQueuedUiWork();
        Assert.Contains("scanner broke", shown);
        window.Close();
    }

    [AvaloniaFact]
    public void SessionListsTheLastScanAndKnowsWhenToScanOnOpen()
    {
        var settings = new PluginSettings { LastScan = new[] { new VstPluginInfo("A", @"C:\a.vst3", "VST3", "V", "Effect") } };
        var session = new PluginBrowserSession(settings, new TabForge.Plugins.PluginTrust(), new TabForge.Platform.Windows.WindowsPluginFileTrust(), () => { });
        Assert.Single(session.All);
        Assert.False(session.FoldersChosen);
        Assert.False(session.ScansOnOpen);
        session.AddFolders(new[] { @"C:\Plugins" });
        Assert.True(session.FoldersChosen);
        Assert.True(session.ScansOnOpen);
        session.SetRemember(true);
        Assert.Single(settings.ScanCache);
        Assert.False(session.ScansOnOpen);   // the remembered list stands in for the scan
    }

    [AvaloniaFact]
    public async Task SessionScansAFolderAndMergesCommonScans()
    {
        var saves = 0;
        var settings = new PluginSettings();
        var folder = Path.Combine(Path.GetTempPath(), "tf-fx-scan", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        settings.Folders.Add(folder);
        var session = new PluginBrowserSession(settings, new TabForge.Plugins.PluginTrust(), new TabForge.Platform.Windows.WindowsPluginFileTrust(), () => saves++);
        var changed = 0;
        session.Changed += () => changed++;
        await session.ScanAsync();
        Assert.False(session.Scanning);
        Assert.Equal(1, changed);
        Assert.Empty(session.All);   // an empty folder: nothing found, nothing invented
        session.SetCommonFolders(new[] { folder });
        Assert.Equal(new[] { folder }, session.CommonFolderLines);
        session.SetCommonFolders(VstScannerService.DefaultCommonFolders);
        Assert.Empty(settings.CommonFolders);   // the defaults are stored as "follow the app"
    }

    [AvaloniaFact]
    public void SessionChoosesASingleVst3FileOrRefusesOtherFiles()
    {
        var vst3 = PluginBrowserSession.FromFile(@"C:\nowhere\Warm Pad.vst3", out var problem);
        Assert.Equal(("Warm Pad", "VST3"), (vst3!.Name, vst3.Format));
        Assert.Null(problem);
        var text = Path.Combine(Path.GetTempPath(), $"tf-fx-{Guid.NewGuid():N}.dll");
        File.WriteAllText(text, "not a plug-in");
        try
        {
            Assert.Null(PluginBrowserSession.FromFile(text, out problem));
            Assert.Equal(PluginBrowserSession.NotAPluginText, problem);
        }
        finally { File.Delete(text); }
    }

    [AvaloniaFact]
    public async Task SessionIdentifiesPluginsWithoutARoleAndRemembersThem()
    {
        var file = TempPlugin();
        PluginCatalog.ProbeOverride = (_, hash, _) => hash.Length == 64 ? ("Effect", "Vend", "Name") : null;
        try
        {
            var trust = new TabForge.Platform.Windows.WindowsPluginFileTrust();
            var settings = new PluginSettings { LastScan = new[] { new VstPluginInfo("Mystery", file, "VST2", "", "") } };
            var pluginTrust = new TabForge.Plugins.PluginTrust();
            pluginTrust.Approve(settings, file, trust);
            var session = new PluginBrowserSession(settings, pluginTrust, trust, () => { });
            await session.IdentifyAsync(() => true);
            Assert.Contains(settings.Probed, k => k.Role == "Effect");
            Assert.Empty(session.NotIdentified);

            var unapproved = new PluginSettings { LastScan = new[] { new VstPluginInfo("Stranger", TempPlugin(), "VST2", "", "") } };
            var second = new PluginBrowserSession(unapproved, pluginTrust, trust, () => { });
            await second.IdentifyAsync(() => true);
            Assert.Single(second.NotIdentified);   // never run to find out
        }
        finally { PluginCatalog.ProbeOverride = null; }
    }
}
