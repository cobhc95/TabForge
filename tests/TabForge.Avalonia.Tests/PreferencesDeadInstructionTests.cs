using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Settings rows that promise a control have it (folder lists with Add folder… / Remove / Reset to defaults, the ASIO Configure button), the menu paths and
/// keys that setting descriptions quote exist in the real menu and hotkey catalogue, and the shown-but-not-ready buttons and menu rows are disabled with a tooltip.
/// Owns: these checks. Does not own: the rows (SettingsCatalog.Audio) or the editors (PreferencesWindow.Folders).
/// Tests: this class.
/// </summary>
public class PreferencesDeadInstructionTests
{
    private static PreferencesRowActions Actions(Func<Window, string[]?>? pick = null, Func<string, Task<string>>? asio = null, string? running = null) =>
        new() { RunCommand = _ => { }, ManageLinkedAudio = _ => Task.CompletedTask, ManageQuarantine = _ => Task.CompletedTask, PickFolders = pick, ConfigureAsio = asio, RunningAsioDevice = () => running };

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetLogicalDescendants().Concat(root.GetVisualDescendants()).OfType<T>().First(c => c.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void PluginFolderListAddsAndRemovesAndKeepsTheStoredSetting()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings(), Actions(pick: _ => new[] { @"C:\Plugs\One", @"C:\Plugs\Two" }));
        window.ShowPage(SettingsCatalog.AudioVst);
        var editor = window.EditorOf("vst.folders");

        Click(Find<Button>(editor, "AddFolderButton"));
        Assert.Equal(new[] { @"C:\Plugs\One", @"C:\Plugs\Two" }, window.Working.Plugins.Folders);

        var list = Find<ListBox>(editor, "FolderList");
        list.SelectedItem = @"C:\Plugs\One";
        Click(Find<Button>(editor, "RemoveFolderButton"));
        Assert.Equal(new[] { @"C:\Plugs\Two" }, window.Working.Plugins.Folders);

        window.Apply();
        Assert.Equal(new[] { @"C:\Plugs\Two" }, window.Saved.Plugins.Folders);
    }

    [AvaloniaFact]
    public void CommonFolderListResetsToTheBuiltInList()
    {
        var settings = new AppSettings();
        settings.Plugins.CommonFolders = new List<string> { @"D:\Mine" };
        var window = new PreferencesWindow(new AvaloniaDialogHost(), settings, Actions());
        window.ShowPage(SettingsCatalog.AudioVst);
        var editor = window.EditorOf("vst.commonfolders");
        Assert.Equal(new[] { @"D:\Mine" }, Find<ListBox>(editor, "FolderList").Items.Cast<string>());

        Click(Find<Button>(editor, "ResetFoldersButton"));
        Assert.Empty(window.Working.Plugins.CommonFolders);
    }

    [AvaloniaFact]
    public void ConfigureAsioIsEnabledOnlyForAsio()
    {
        var opened = new List<string>();
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings(), Actions(asio: driver => { opened.Add(driver); return Task.FromResult(""); }));
        window.Write("vst.driver", AudioDrivers.Asio);
        window.Write("vst.device", "My ASIO");
        window.ShowPage(SettingsCatalog.AudioVst);
        Assert.True(window.IsRowEnabled("vst.asio.configure"));
        Click(Assert.IsType<Button>(window.EditorOf("vst.asio.configure")));
        Assert.Equal(new[] { "My ASIO" }, opened);

        window.Write("vst.driver", AudioDrivers.All.First(d => d != AudioDrivers.Asio));
        Assert.False(window.IsRowEnabled("vst.asio.configure"));
    }

    [AvaloniaFact]
    public async Task AnAsioOutputDeviceListHasNoWindowsDefaultAndAnEmptyDeviceIsNotShownAsOne()
    {
        var saved = new AppSettings();
        saved.Plugins!.Driver = AudioDrivers.Asio;
        saved.Plugins.Device = "My ASIO";
        var window = new PreferencesWindow(new AvaloniaDialogHost(), saved);
        window.ShowPage(SettingsCatalog.AudioVst);
        var names = ((TabForge.Avalonia.App)global::Avalonia.Application.Current!).DeviceNames;
        IEnumerable<string> Shown() => ((ComboBox)window.EditorOf("vst.device")).Items.Cast<string>();
        Assert.Equal(new[] { PendingChoices.CheckingText }, Shown());   // listed on a worker: the picker opened without waiting
        await names.Settled(AudioDrivers.Asio);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(PendingChoices.CheckingText, Shown());
        Assert.Contains("My ASIO", Shown());
        Assert.DoesNotContain(SettingsCatalog.DefaultAudioDevice, Shown());
        var other = AudioDrivers.All.First(d => d != AudioDrivers.Asio);
        window.Write("vst.driver", other);
        await names.Settled(other);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(SettingsCatalog.DefaultAudioDevice, Shown());
    }

    [AvaloniaFact]
    public void ConfigureAsioIsOffWithATooltipWhileTheEngineRunsAnotherDriver()
    {
        var opened = new List<string>();
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings(), Actions(asio: driver => { opened.Add(driver); return Task.FromResult(""); }, running: "Other ASIO"));
        window.Write("vst.driver", AudioDrivers.Asio);
        window.Write("vst.device", "My ASIO");
        window.ShowPage(SettingsCatalog.AudioVst);
        var button = Assert.IsType<Button>(window.EditorOf("vst.asio.configure"));
        Assert.False(button.IsEnabled);
        Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(button) as string));
        Click(button);
        Assert.Empty(opened);
    }

    private static string Clean(string header) => header.Replace("_", "").Replace("…", "").Trim();

    /// <summary>Every "Menu > Item" a setting description quotes names a real item of that main menu, and every key it quotes is in the hotkey catalogue.</summary>
    [AvaloniaFact]
    public void MenuPathsAndKeysQuotedInSettingDescriptionsExist()
    {
        var menus = MainMenuTable.WithFeatureRows().GroupBy(g => Clean(g.Menu))
            .ToDictionary(g => g.Key, g => g.SelectMany(x => x.Rows).Where(r => !r.IsSeparator).Select(r => Clean(r.Header!)).ToList());
        var gestures = HotkeyCatalog.All.SelectMany(a => new[] { a.DefaultGesture, a.DefaultGesture2 }).Where(g => g.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        foreach (var row in PreferencesCatalog.Build(new AppSettings()))
        {
            foreach (Match m in Regex.Matches(row.Description, @"\b(" + string.Join("|", menus.Keys.Select(Regex.Escape)) + @") > ([^.;:,()]+)"))
            {
                var item = m.Groups[2].Value.Trim().TrimEnd('…');
                if (!menus[m.Groups[1].Value].Any(h => item.StartsWith(h, StringComparison.OrdinalIgnoreCase))) problems.Add($"{row.Key}: {m.Value}");
            }
            foreach (Match m in Regex.Matches(row.Description, @"\b(?:(?:Ctrl|Alt|Shift)\+)+\w+|\bF\d{1,2}\b"))
                if (!gestures.Contains(m.Value)) problems.Add($"{row.Key}: key {m.Value}");
        }
        Assert.Empty(problems);
    }

    [AvaloniaFact]
    public void PrintMenuRowsAreDisabledWithATooltip()
    {
        var main = new MainWindow();
        main.Show();
        foreach (var id in new[] { "File.Print", "File.PrintPreview" })
        {
            var item = main.GetLogicalDescendants().OfType<MenuItem>().First(m => (m.Tag as string) == id);
            Assert.False(item.IsEnabled);
            Assert.Equal("Not available yet", ToolTip.GetTip(item));
        }
        main.Close();
    }

    [AvaloniaFact]
    public void PrintHotkeysDoNothingWhileThePrintRowsAreDisabled()
    {
        var main = new MainWindow();
        main.Show();
        main.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, "p");
        main.KeyPress(Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P, "P");
        Assert.Empty(main.OwnedWindows);
        main.Close();
    }

    [AvaloniaFact]
    public void NotAvailableButtonIsDisabledWithATooltip()
    {
        var button = new Button();
        ToolWindowParts.NotAvailable(button);
        Assert.False(button.IsEnabled);
        Assert.Equal("Not available yet", ToolTip.GetTip(button));
    }
}
