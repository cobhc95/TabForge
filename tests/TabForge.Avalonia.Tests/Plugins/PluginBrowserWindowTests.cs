using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Avalonia.Views.Fx;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the Add plug-in window (structure, presenter texts, filter, sort, keys, renders, contrast).
/// Does not own: the window or the presenter; expected rows and texts come from PluginBrowserListPresenter.
/// Tests: this class.
/// </summary>
public class PluginBrowserWindowTests
{
    private static readonly IReadOnlyList<(string Path, string Vendor, string Role)> NoProbed = [];

    private static readonly IReadOnlyList<VstPluginInfo> Samples =
    [
        new("Plate Reverb", @"C:\Program Files\Common Files\VST3\Plate Reverb.vst3", "VST3", "Sample Audio", "Effect"),
        new("Grand Piano", @"C:\Program Files\Common Files\VST3\Grand Piano.vst3", "VST3", "Example Labs", "Instrument"),
        new("Tape Echo", @"C:\Program Files\VstPlugins\TapeEcho.dll", "VST2", "Sample Audio", ""),
        new("Bass Amp", @"C:\Program Files\Common Files\VST3\Bass Amp.vst3", "VST3", "Example Labs", "Effect"),
    ];

    // The sample plug-in with no role, marked not approved, so its row shows the "approve to identify" text.
    private static readonly IReadOnlySet<string> NotIdentified =
        Samples.Where(p => p.Role.Length == 0).Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static PluginBrowserSession Session(IReadOnlyList<VstPluginInfo> plugins, out PluginSettings settings)
    {
        settings = new PluginSettings { LastScan = plugins.ToList(), RememberScan = false };
        var session = new PluginBrowserSession(settings, new TabForge.Plugins.PluginTrust(), new TabForge.Platform.Windows.WindowsPluginFileTrust(), () => { });
        foreach (var path in NotIdentified) session.NotIdentified.Add(path);
        return session;
    }

    private static PluginBrowserWindow New(IReadOnlyList<VstPluginInfo>? plugins = null, IFileDialogs? files = null) =>
        new(Session(plugins ?? Samples, out _), files ?? new AvaloniaFileDialogs(), _ => { });

    // WPF src/TabForge/Views/PluginBrowserDialog.cs tree order: the search box (top), the bottom section (status line, remember option,
    // buttons), then the list that fills the rest (column headers, rows).
    private static readonly (string Kind, string Id)[] ExpectedControls =
    [
        ("TextBox", "Plugins.Search"),
        ("TextBlock", "StatusText"),
        ("Button", "Plugins.Pause"),
        ("Button", "Plugins.Stop"),
        ("CheckBox", "RememberBox"),
        ("CheckBox", "ClapBox"),
        ("Button", "Plugins.ScanStandard"), ("Button", "Plugins.AddFolder"), ("Button", "Plugins.BrowseFile"), ("Button", "Plugins.Rescan"),
        ("Button", "Plugins.ScanCommon"), ("Button", "Plugins.EditList"), ("Button", "Plugins.FolderSettings"), ("Button", "Plugins.Cancel"),
        ("Button", "SortByName"), ("Button", "SortByVendor"), ("Button", "SortByFormat"), ("Button", "SortByRole"),
        ("ListBox", "Plugins.List"),
    ];

    private static readonly string[] ExpectedButtonTexts =
    [
        "Scan the standard VST folders", "Add folder…", "Browse for a plug-in file…", "Rescan", "Scan common folders", "Edit list…",
        "Folder settings…", "Cancel",
    ];

    [AvaloniaFact]
    public void ControlsMatchWpfOrderTitleAndSize()
    {
        var window = Open();
        Assert.Equal(("Add plug-in", 760d, 560d, 520d, 320d, false), (window.Title, window.Width, window.Height, window.MinWidth, window.MinHeight, window.ShowInTaskbar));
        Assert.Equal(WindowStartupLocation.CenterOwner, window.WindowStartupLocation);
        var found = DialogTestKit.LayoutControls(window).Where(c => c is TextBox or ListBox or Button || c.Name == "StatusText")
            .Select(c => (Kind: c.GetType().Name, Id: AutomationProperties.GetAutomationId(c) ?? c.Name ?? "")).ToList();
        Assert.Equal(ExpectedControls, found);
        Assert.Equal("Search plug-ins", AutomationProperties.GetName(Named<TextBox>(window, "SearchBox")));
        Assert.Equal("Plug-ins", AutomationProperties.GetName(List(window)));
        Assert.Equal(ExpectedButtonTexts, Named<WrapPanel>(window, "ButtonBar").Children.OfType<Button>().Select(b => (string)b.Content!));
        Assert.Equal("Type to filter by name or vendor", ToolTip.GetTip(Named<TextBox>(window, "SearchBox")));
        window.Close();
    }

    [AvaloniaFact]
    public void StatusTextComesFromThePresenter()
    {
        var window = Open();
        var count = Samples.Count;
        Assert.Equal(PluginBrowserListPresenter.StatusText(count, false, false), Status(window));
        Assert.False(Named<Button>(window, "Plugins.ScanStandard", byAutomationId: true).IsVisible);
        Named<CheckBox>(window, "RememberBox").IsChecked = true;
        Assert.Equal(PluginBrowserListPresenter.StatusText(count, false, true), Status(window));
        window.Close();

        var empty = New([]);
        empty.Show();
        Assert.Equal(PluginBrowserListPresenter.StatusText(0, false, false), Status(empty));
        Assert.True(Named<Button>(empty, "Plugins.ScanStandard", byAutomationId: true).IsVisible);
        empty.Close();
    }

    [AvaloniaFact]
    public void BrowseForAPluginFileChoosesItAndCloses()
    {
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-browse-{Guid.NewGuid():N}", "Hand Picked.vst3");
        var window = New(files: new FxTestKit.PickFile(file));
        window.Show();
        FxTestKit.Press(window, Named<Button>(window, "Plugins.BrowseFile", byAutomationId: true));
        Assert.Equal(("Hand Picked", "VST3", file), (window.Chosen!.Name, window.Chosen.Format, window.Chosen.Path));
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void BrowseForANonPluginFileSaysSoOnTheStatusLine()
    {
        var file = Path.Combine(Path.GetTempPath(), $"tf-fx-notplugin-{Guid.NewGuid():N}.dll");
        File.WriteAllText(file, "not a plug-in");
        try
        {
            var window = New(files: new FxTestKit.PickFile(file));
            window.Show();
            FxTestKit.Press(window, Named<Button>(window, "Plugins.BrowseFile", byAutomationId: true));
            Assert.Equal(PluginBrowserSession.NotAPluginText, Status(window));
            Assert.Null(window.Chosen);
            window.Close();
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public void TheClapOptionIsOffByDefaultAndSavedWhenChosen()
    {
        var session = Session(Samples, out var settings);
        Assert.False(settings.ScanClap);
        var window = new PluginBrowserWindow(session, new AvaloniaFileDialogs(), _ => { });
        window.Show();
        Assert.False(Named<CheckBox>(window, "ClapBox").IsChecked);
        window.Close();
        session.SetScanClap(true);
        Assert.True(settings.ScanClap);
        session.SetScanClap(false);
        Assert.False(settings.ScanClap);
    }

    [AvaloniaFact]
    public void RememberOptionIsSavedInTheSettings()
    {
        var session = Session(Samples, out var settings);
        var window = new PluginBrowserWindow(session, new AvaloniaFileDialogs(), _ => { });
        window.Show();
        Named<CheckBox>(window, "RememberBox").IsChecked = true;
        Assert.True(settings.RememberScan);
        Assert.Equal(Samples.Count, settings.ScanCache.Count);
        Named<CheckBox>(window, "RememberBox").IsChecked = false;
        Assert.False(settings.RememberScan);
        Assert.Empty(settings.ScanCache);
        window.Close();
    }

    [AvaloniaFact]
    public void FolderSettingsButtonClosesAndOpensTheSettingsPage()
    {
        var opened = 0;
        var window = new PluginBrowserWindow(Session(Samples, out _), new AvaloniaFileDialogs(), _ => opened++);
        window.Show();
        FxTestKit.Press(window, Named<Button>(window, "Plugins.FolderSettings", byAutomationId: true));
        Assert.Equal(1, opened);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void ScanStandardFoldersTurnsTheOptionOnAndHidesTheButton()
    {
        var window = New([]);
        window.Show();
        var button = Named<Button>(window, "Plugins.ScanStandard", byAutomationId: true);
        Assert.True(button.IsVisible);
        FxTestKit.Press(window, button);
        Assert.False(button.IsVisible);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("echo")]
    [InlineData("SAMPLE")]
    [InlineData("  labs ")]
    [InlineData("zzqx")]
    public void FilterShowsThePresenterRows(string query)
    {
        var window = Open();
        Named<TextBox>(window, "SearchBox").Text = query;
        Assert.Equal(ExpectedRows(query, 0, true), Rows(window));
        window.Close();
    }

    [AvaloniaFact]
    public void HeaderClickSortsAscendingThenDescending()
    {
        var window = Open();
        Assert.Equal(ExpectedRows("", 0, true), Rows(window));
        Assert.Contains(Rows(window), r => r.Role == "approve to identify");
        foreach (var (header, column) in new[] { ("SortByVendor", 1), ("SortByFormat", 2), ("SortByRole", 3), ("SortByName", 0) })
        {
            DialogTestKit.Click(Named<Button>(window, header));
            Assert.Equal(ExpectedRows("", column, true), Rows(window));
            DialogTestKit.Click(Named<Button>(window, header));
            Assert.Equal(ExpectedRows("", column, false), Rows(window));
        }
        window.Close();
    }

    [AvaloniaFact]
    public void EnterInTheListChoosesTheSelectedRowAndCloses()
    {
        var window = Open();
        var list = List(window);
        list.SelectedIndex = 2;
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = list });
        Assert.Equal(ExpectedRows("", 0, true)[2].Info, window.Chosen);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void EnterInTheSearchBoxChoosesTheFirstRowWhenNoneIsSelected()
    {
        var window = Open();
        var search = Named<TextBox>(window, "SearchBox");
        search.Text = "labs";
        search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, Source = search });
        Assert.Equal(0, List(window).SelectedIndex);
        List(window).SelectedIndex = -1;
        search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = search });
        Assert.Equal(ExpectedRows("labs", 0, true)[0].Info, window.Chosen);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void EscClosesWithoutChoosing()
    {
        var window = Open();
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = window });
        Assert.Null(window.Chosen);
        Assert.False(window.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RendersInDarkAndLight(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = New();
        try
        {
            var path = DialogTestKit.SavePng(window, nameof(PluginBrowserWindowTests), $"plugin-browser-{(light ? "light" : "dark")}.png");
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextContrastOverThePanelIsLegible(bool light)
    {
        var panel = AvaloniaPalette.Get("PanelBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), panel) >= 4.5, $"{text} on PanelBackground (light: {light})");
    }

    private static PluginBrowserWindow Open()
    {
        var window = New();
        window.Show();
        return window;
    }

    private static List<PluginBrowserRow> ExpectedRows(string query, int column, bool ascending) =>
        PluginBrowserListPresenter.Rows(Samples, NoProbed, NotIdentified, query, column, ascending);

    private static ListBox List(Window window) => Named<ListBox>(window, "PluginList");

    private static List<PluginBrowserRow> Rows(Window window) => ((IEnumerable<PluginBrowserRow>)List(window).ItemsSource!).ToList();

    private static string? Status(Window window) => Named<TextBlock>(window, "StatusText").Text;

    private static T Named<T>(Window window, string name, bool byAutomationId = false) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => (byAutomationId ? AutomationProperties.GetAutomationId(c) : c.Name) == name);
}
