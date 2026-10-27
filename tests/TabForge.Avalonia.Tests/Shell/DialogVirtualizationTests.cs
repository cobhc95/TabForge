using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Avalonia.Views.Presets;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Windows;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the narrow headless checks of the perf fixes H13/H15 (virtualized preset and plug-in lists, mixer shell-first)
/// and H7 (mixer theme switch repaints instead of rebuilding). Behavioural suites (PresetManagerWindowTests,
/// PluginBrowserWindowTests, MixerWindowTests, PreferencesWindowTests) own the features themselves.
/// Tests: this class.
/// </summary>
public class DialogVirtualizationTests
{
    private static SongDocument DocumentOf(SongProject project) => new(() => null) { Project = project };

    [AvaloniaFact]
    public void PresetListIsVirtualized()
    {
        var window = new PresetManagerWindow(new AvaloniaDialogHost());
        try
        {
            var list = Named<ListBox>(window, "PresetList");
            Assert.IsType<VirtualizingStackPanel>(list.ItemsPanel?.Build());
            Assert.IsType<FuncDataTemplate<PresetEntry>>(list.ItemTemplate);
            var source = Assert.IsAssignableFrom<IReadOnlyList<PresetEntry>>(list.ItemsSource);
            Assert.Equal(window.Listed.Count, source.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PluginListIsVirtualized()
    {
        var settings = new PluginSettings
        {
            LastScan = new List<VstPluginInfo>
            {
                new("Plate Reverb", @"C:\Program Files\Common Files\VST3\Plate Reverb.vst3", "VST3", "Sample Audio", "Effect"),
                new("Grand Piano", @"C:\Program Files\Common Files\VST3\Grand Piano.vst3", "VST3", "Example Labs", "Instrument"),
            },
        };
        var session = new PluginBrowserSession(settings, new TabForge.Plugins.PluginTrust(), new WindowsPluginFileTrust(), () => { });
        var window = new PluginBrowserWindow(session, new AvaloniaFileDialogs(), _ => { });
        try
        {
            window.Show();
            var list = window.GetLogicalDescendants().OfType<ListBox>().Single(l => l.Name == "PluginList");
            Assert.IsType<VirtualizingStackPanel>(list.ItemsPanel?.Build());
            Assert.Equal(2, list.ItemCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MixerShowsShellFirstThenStrips()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create()));
        try
        {
            // Shell only: the column header and Master show with the loading placeholder, no group strips yet.
            Assert.False(window.StripsBuilt);
            Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Name == "MixerLoading");
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Border>(), b => b.Name?.StartsWith("Mixer group:") == true);

            window.EnsureStripsBuilt();

            Assert.True(window.StripsBuilt);
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Name == "MixerLoading");
            Assert.Contains(window.GetLogicalDescendants().OfType<Border>(), b => b.Name?.StartsWith("Mixer group:") == true);
            // A second call is free and keeps the same strips.
            var before = window.GetLogicalDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("Mixer group:") == true).ToList();
            window.EnsureStripsBuilt();
            Assert.Equal(before, window.GetLogicalDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("Mixer group:") == true).ToList());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MixerThemeSwitchKeepsTheStrips()
    {
        var window = new MixerWindow(DocumentOf(DemoSongFactory.Create()));
        window.Show();
        window.EnsureStripsBuilt();
        try
        {
            var before = window.GetLogicalDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("Mixer group:") == true).ToList();
            Assert.NotEmpty(before);
            window.RequestedThemeVariant = window.ActualThemeVariant == ThemeVariant.Light ? ThemeVariant.Dark : ThemeVariant.Light;
            RunQueuedUiWork();
            var after = window.GetLogicalDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("Mixer group:") == true).ToList();
            Assert.True(window.StripsBuilt);
            Assert.Equal(before.Count, after.Count);
            Assert.All(before.Zip(after), pair => Assert.Same(pair.First, pair.Second));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task PreferencesResetThemeTwiceKeepsWorking()
    {
        var questions = new List<string>();
        var host = new AvaloniaDialogHost
        {
            Capture = dialog =>
            {
                questions.Add(string.Join("\n", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)));
                return MessageBoxAnswer.Yes;
            },
        };
        var window = new PreferencesWindow(host, new AppSettings());
        window.ShowPage(PreferencesCatalog.Appearance);
        window.Write("appearance.accent", "#FF0000");
        await window.ResetThemeAsync();
        Assert.NotEqual("#FF0000", window.Working.Appearance.Accent);
        window.Write("appearance.accent", "#00FF00");
        await window.ResetThemeAsync();
        Assert.NotEqual("#00FF00", window.Working.Appearance.Accent);
    }
}
