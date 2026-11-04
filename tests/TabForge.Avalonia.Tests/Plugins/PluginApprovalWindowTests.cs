using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the confirmation window and the Approve all / Approve selected wiring of the Add plug-in window, with the answer injected (no real modal dialog).
/// Does not own: the batch rules (PluginApprovalPresenterTests).
/// Tests: this class. Shares PluginCatalog.ProbeOverride, so it runs in the "PluginProbeSeam" collection.
/// </summary>
[Collection("PluginProbeSeam")]
public class PluginApprovalWindowTests
{
    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().First(c => c.Name == name);

    private static (PluginBrowserWindow Window, PluginBrowserSession Session, PluginSettings Settings, PluginTrust Trust, TabForge.Platform.IPluginFileTrust Files, List<VstPluginInfo> Plugins) Open(int count, bool answer, List<int>? asked = null)
    {
        var (settings, trust, files, plugins) = PluginApprovalPresenterTests.Setup(count);
        settings.RememberScan = false;
        var session = new PluginBrowserSession(settings, trust, files, () => { });
        foreach (var p in plugins) session.NotIdentified.Add(p.Path);
        var window = new PluginBrowserWindow(session, new AvaloniaFileDialogs(), _ => { })
        {
            ConfirmApproval = list => { asked?.Add(list.Count); return Task.FromResult(answer); },
        };
        window.Show();
        return (window, session, settings, trust, files, plugins);
    }

    private static async Task Press(Window window, string name, Func<bool> done)
    {
        var button = Named<Button>(window, name);
        await Wait(() => button.IsEnabled);   // the identify pass of the opening window holds the button, as it does for the user
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(done);
    }

    private static async Task Wait(Func<bool> done)
    {
        for (var i = 0; i < 400 && !done(); i++) await Task.Delay(25);
    }

    [AvaloniaFact]
    public void TheDialogShowsTheCountTheNamesAndFoldersAndNoFutureFilesOption()
    {
        var (_, _, _, plugins) = (0, 0, 0, PluginApprovalPresenterTests.Setup(2).Plugins);
        var window = new PluginApprovalWindow(plugins);
        window.Show();
        Assert.Equal("Approve plug-ins?", window.Title);
        Assert.Equal(PluginApprovalPresenter.Body(2), Named<TextBlock>(window, "MessageText").Text);
        Assert.Equal("Approve 2", Named<Button>(window, "ApproveButton").Content);
        Assert.Equal("Cancel", Named<Button>(window, "CancelButton").Content);
        Assert.True(Named<Expander>(window, "PluginListExpander").IsExpanded);
        var lines = Named<StackPanel>(window, "PluginNames").Children.OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Equal(plugins.Select(PluginApprovalPresenter.ItemText), lines);
        Assert.Contains(System.IO.Path.GetDirectoryName(plugins[0].Path)!, lines[0]);
        Assert.Empty(window.GetLogicalDescendants().OfType<CheckBox>());
        window.Close();
    }

    [AvaloniaFact]
    public void CancelIsNotConfirmedAndApproveIs()
    {
        var plugins = PluginApprovalPresenterTests.Setup(1).Plugins;
        var cancel = new PluginApprovalWindow(plugins);
        cancel.Show();
        Named<Button>(cancel, "CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(cancel.Confirmed);
        var approve = new PluginApprovalWindow(plugins);
        approve.Show();
        Named<Button>(approve, "ApproveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(approve.Confirmed);
    }

    [AvaloniaFact]
    public async Task CancellingTheQuestionApprovesNothingAndProbesNothing()
    {
        var asked = new List<int>();
        var probes = 0;
        PluginCatalog.ProbeOverride = (_, _, _) => { probes++; return ("Effect", "V", "N"); };
        try
        {
            var (window, _, settings, trust, files, plugins) = Open(2, answer: false, asked);
            await Press(window, "ApproveAllButton", () => asked.Count > 0);
            await Task.Delay(50);
            Assert.Equal(new[] { 2 }, asked);
            Assert.Equal(0, probes);
            Assert.DoesNotContain(plugins, p => trust.IsTrusted(p.Path, settings, files));
            Assert.Empty(settings.ApprovedPluginPaths ?? new());
            window.Close();
        }
        finally { PluginCatalog.ProbeOverride = null; }
    }

    [AvaloniaFact]
    public async Task ApproveAllCoversTheListedRowsOnlyAndUpdatesTheRowsAndStatus()
    {
        var asked = new List<int>();
        PluginCatalog.ProbeOverride = (_, hash, _) => hash.Length == 64 ? ("Effect", "Vendor", "Name") : null;
        try
        {
            var (window, session, settings, trust, files, plugins) = Open(3, answer: true, asked);
            Named<TextBox>(window, "SearchBox").Text = "P1";   // the filter narrows "listed"
            await Press(window, "ApproveAllButton", () => asked.Count > 0 && !session.Approving);
            await Wait(() => !session.Approving);
            Assert.Equal(new[] { 1 }, asked);
            Assert.True(trust.IsTrusted(plugins[1].Path, settings, files));
            Assert.False(trust.IsTrusted(plugins[0].Path, settings, files));
            var row = ((IEnumerable<PluginBrowserRow>)Named<ListBox>(window, "PluginList").ItemsSource!).Single(r => r.Info.Path == plugins[1].Path);
            Assert.Equal("Effect", row.Role);
            Assert.Equal(PluginApprovalPresenter.DoneText(1, 1, 0), Named<TextBlock>(window, "StatusText").Text);
            window.Close();
        }
        finally { PluginCatalog.ProbeOverride = null; }
    }

    [AvaloniaFact]
    public async Task ApproveSelectedCoversOnlyTheSelectedRowsAndNothingRunsByItself()
    {
        var asked = new List<int>();
        PluginCatalog.ProbeOverride = (_, _, _) => ("Effect", "V", "N");
        try
        {
            var (window, session, settings, trust, files, plugins) = Open(3, answer: true, asked);
            await Task.Delay(50);
            Assert.Empty(asked);   // opening or scanning never approves
            Assert.Empty(settings.ApprovedPluginPaths ?? new());
            var list = Named<ListBox>(window, "PluginList");
            Assert.Equal(SelectionMode.Multiple, list.SelectionMode);
            foreach (var row in list.Items.OfType<PluginBrowserRow>().Where(r => r.Info.Path == plugins[0].Path || r.Info.Path == plugins[2].Path).ToList()) list.SelectedItems!.Add(row);
            var item = list.ContextMenu!.Items.OfType<MenuItem>().Single();
            Assert.Equal("Approve selected…", item.Header);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Wait(() => asked.Count > 0 && !session.Approving && trust.IsTrusted(plugins[2].Path, settings, files));
            Assert.Equal(new[] { 2 }, asked);
            Assert.False(trust.IsTrusted(plugins[1].Path, settings, files));
            window.Close();
        }
        finally { PluginCatalog.ProbeOverride = null; }
    }

    [AvaloniaFact]
    public async Task ABatchShowsStopNotPauseAndARescanWaits()
    {
        var release = new System.Threading.ManualResetEventSlim();
        var asked = new List<int>();
        PluginCatalog.ProbeOverride = (_, _, _) => { release.Wait(5000); return ("Effect", "V", "N"); };
        try
        {
            var (window, session, _, _, _, _) = Open(2, answer: true, asked);
            await Press(window, "ApproveAllButton", () => session.Approving);
            Assert.True(session.Approving);
            Assert.True(Named<StackPanel>(window, "RunButtons").IsVisible);
            Assert.False(Named<Button>(window, "PauseButton").IsVisible);
            Assert.False(Named<Button>(window, "ApproveAllButton").IsEnabled);
            await session.ScanAsync();
            Assert.False(session.Scanning);
            Named<Button>(window, "StopButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            release.Set();
            await Wait(() => !session.Approving);
            Assert.False(session.Approving);
            Assert.Contains("stopped", Named<TextBlock>(window, "StatusText").Text);
            window.Close();
        }
        finally { release.Set(); PluginCatalog.ProbeOverride = null; }
    }
}

[CollectionDefinition("PluginProbeSeam", DisableParallelization = true)]
public sealed class PluginProbeSeamCollection;
