using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Docking;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Reset all settings: every Settings row and every shortcut goes back to its default, while presets, layouts, folders,
/// window sizes, mixer rules and last-used export dialog options stay exactly as they were. Cancel still restores them all.
/// Tests: this class.
/// </summary>
public class PreferencesWindowResetTests
{
    /// <summary>The AppSettings properties whose values a Settings row (or the shortcut page) writes.</summary>
    private static readonly string[] RowOwnedProperties =
    {
        nameof(AppSettings.General), nameof(AppSettings.Appearance), nameof(AppSettings.Audio), nameof(AppSettings.Plugins),
        nameof(AppSettings.Editing), nameof(AppSettings.Follow), nameof(AppSettings.Timeline), nameof(AppSettings.Tabs),
        nameof(AppSettings.Learn), nameof(AppSettings.LiveVideo), nameof(AppSettings.MasterMeter), nameof(AppSettings.Hotkeys),
        nameof(AppSettings.Notation), nameof(AppSettings.NotationPreferenceSet), nameof(AppSettings.PreferredContinuousScoreView),
        nameof(AppSettings.PreferredHorizontalScoreView), nameof(AppSettings.Render),
    };

    /// <summary>The AppSettings properties that are user data or chrome, never a Settings row: Reset all must leave them alone.</summary>
    private static readonly string[] PreservedProperties =
    {
        nameof(AppSettings.InstrumentHeight), nameof(AppSettings.ArrangementHeight), nameof(AppSettings.BottomTabsWidth),
        nameof(AppSettings.SectionsPanelHeight), nameof(AppSettings.ShowInstrument), nameof(AppSettings.ShowArrangement),
        nameof(AppSettings.DarkPaper), nameof(AppSettings.LeftHanded), nameof(AppSettings.ShowNoteNames), nameof(AppSettings.PreviewNotes),
        nameof(AppSettings.PreviewHorizon), nameof(AppSettings.ScaleHighlight), nameof(AppSettings.FretboardFrets),
        nameof(AppSettings.ZoomFactor), nameof(AppSettings.Metronome), nameof(AppSettings.CountIn), nameof(AppSettings.Speed),
        nameof(AppSettings.WindowWidth), nameof(AppSettings.WindowHeight), nameof(AppSettings.WindowLeft), nameof(AppSettings.WindowTop),
        nameof(AppSettings.Maximised), nameof(AppSettings.SettingsWindowWidth), nameof(AppSettings.SettingsWindowHeight),
        nameof(AppSettings.MixerWindowWidth), nameof(AppSettings.MixerWindowHeight), nameof(AppSettings.MixerRules),
        nameof(AppSettings.MidiExport), nameof(AppSettings.Video), nameof(AppSettings.TrackMeter), nameof(AppSettings.Workspace),
        nameof(AppSettings.WorkspaceBeforeSideHide), nameof(AppSettings.SavedLayouts), nameof(AppSettings.LastLayout),
        nameof(AppSettings.EffectPresets), nameof(AppSettings.RememberedFolders),
    };

    /// <summary>A new AppSettings property must be classified here before it can be silently wiped by a reset.</summary>
    [Fact]
    public void EveryAppSettingsPropertyIsClassifiedAsARowOrPreserved()
    {
        var all = typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(RowOwnedProperties.Intersect(PreservedProperties, StringComparer.Ordinal));
        Assert.Empty(all.Except(RowOwnedProperties).Except(PreservedProperties).OrderBy(name => name));
        Assert.Equal(all.Count, RowOwnedProperties.Length + PreservedProperties.Length);
    }

    [AvaloniaFact]
    public async Task ResetAllRestoresEveryRowAndShortcutButKeepsUserData()
    {
        var saved = new AppSettings();
        var defaults = PreferencesCatalog.Build(new AppSettings()).ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        // Give the safe rows (no audio device probes) a value that differs from their default.
        foreach (var row in PreferencesCatalog.Build(saved).Where(r => r.Kind != SettingKind.Button && !r.Key.StartsWith("vst.", StringComparison.OrdinalIgnoreCase)))
            row.Set(NonDefault(row, defaults[row.Key].Get()));
        saved.Hotkeys.Preset = "My layout";
        saved.Hotkeys.Bindings["Edit.Undo"] = "Ctrl+Alt+Z";
        saved.Hotkeys.Bindings2["Edit.Redo"] = "Ctrl+Alt+Y";
        saved.Hotkeys.DisabledActions.Add("File.New");
        saved.Hotkeys.DisabledActions2.Add("File.Open");
        SeedUserData(saved);
        var before = SnapshotUserData(saved);

        var questions = new List<string>();
        var window = new PreferencesWindow(Host(questions), saved);
        await window.ResetAllAsync();

        Assert.Single(questions);
        Assert.Contains(PreferencesPagePresenter.ResetAll().Message, questions[0]);
        var working = PreferencesCatalog.Build(window.Working).ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, row) in working.Where(pair => pair.Value.Kind != SettingKind.Button))
            Assert.Equal(PreferencesPagePresenter.ShowValue(defaults[key].Get()), PreferencesPagePresenter.ShowValue(row.Get()));
        var live = PreferencesCatalog.Build(saved).ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, row) in live.Where(pair => pair.Value.Kind != SettingKind.Button))
            Assert.Equal(PreferencesPagePresenter.ShowValue(defaults[key].Get()), PreferencesPagePresenter.ShowValue(row.Get()));

        var freshKeys = new HotkeySettings();
        Assert.Equal(freshKeys.Preset, window.Working.Hotkeys.Preset);
        Assert.Empty(window.Working.Hotkeys.Bindings);
        Assert.Empty(window.Working.Hotkeys.Bindings2);
        Assert.Empty(window.Working.Hotkeys.DisabledActions);
        Assert.Empty(window.Working.Hotkeys.DisabledActions2);
        Assert.Equal(before, SnapshotUserData(window.Working));
        Assert.Equal(before, SnapshotUserData(saved));
    }

    [AvaloniaFact]
    public async Task ResetAllPutsPreviewNextOnAndPreviewPreviousOff()
    {
        var saved = new AppSettings();
        saved.Editing.PreviewNotesEnabled = false;
        saved.Editing.PreviewPreviousNotesEnabled = true;

        var window = new PreferencesWindow(Host(new List<string>()), saved);
        await window.ResetAllAsync();

        Assert.True(window.Working.Editing.PreviewNotesEnabled);
        Assert.False(window.Working.Editing.PreviewPreviousNotesEnabled);
    }

    [AvaloniaFact]
    public async Task CancelAfterResetAllRestoresTheRowsAndShortcuts()
    {
        var saved = new AppSettings();
        saved.General.RestoreWindow = false;
        saved.Appearance.UiScale = 1.3;
        saved.Hotkeys.Bindings["Edit.Undo"] = "Ctrl+Alt+Z";
        SeedUserData(saved);
        var before = SnapshotUserData(saved);

        var window = new PreferencesWindow(Host(new List<string>()), saved);
        window.Show();
        await window.ResetAllAsync();
        Assert.True(saved.General.RestoreWindow);
        Assert.Empty(saved.Hotkeys.Bindings);

        window.Close();
        Assert.False(saved.General.RestoreWindow);
        Assert.Equal(1.3, saved.Appearance.UiScale);
        Assert.Equal("Ctrl+Alt+Z", saved.Hotkeys.Bindings["Edit.Undo"]);
        Assert.Equal(before, SnapshotUserData(saved));
    }

    private static object? NonDefault(SettingDescriptor row, object? current) => row.Kind switch
    {
        SettingKind.Bool => current is not true,
        SettingKind.Choice => row.Choices.FirstOrDefault(choice => !string.Equals(choice, current?.ToString(), StringComparison.Ordinal)) ?? current,
        SettingKind.Number => current is double number && Math.Abs(number - row.Max) < 0.0001 ? row.Min : row.Max,
        SettingKind.Text => "custom-" + row.Key,
        SettingKind.Colour => "#010203",
        SettingKind.FolderList => @"C:\custom\" + row.Key.Replace('.', '-'),
        _ => current,
    };

    private static void SeedUserData(AppSettings saved)
    {
        saved.Workspace = Layout("workspace");
        saved.WorkspaceBeforeSideHide = Layout("side-hide");
        saved.SavedLayouts.Add(new SavedLayout { Name = "My layout", State = Layout("saved"), Maximised = false, WindowWidth = 1111, WindowHeight = 777 });
        saved.LastLayout = "My layout";
        saved.EffectPresets.Add(new EffectPresetEntry { Kind = "Bend", Name = "My bend", Values = { ["Depth"] = 2 } });
        saved.RememberedFolders["OpenSong"] = @"C:\Songs";
        saved.MixerRules = new MixerAppRules
        {
            Fallback = "My group",
            Groups = new List<MixerGroupDef> { new() { Name = "My group", Colour = "#112233" } },
        };
        saved.WindowWidth = 1234;
        saved.WindowHeight = 777;
        saved.WindowLeft = 12;
        saved.WindowTop = 34;
        saved.Maximised = false;
        saved.SettingsWindowWidth = 999;
        saved.SettingsWindowHeight = 701;
        saved.MixerWindowWidth = 812;
        saved.MixerWindowHeight = 613;
        saved.Render.Source = 2;
        saved.Render.Bounds = 3;
        saved.Render.Directory = @"C:\Render";
        saved.Render.Pattern = "my-pattern";
        saved.Render.Mono = true;
        saved.MidiExport.Bounds = 2;
        saved.MidiExport.UseArticulationMap = true;
        saved.MidiExport.TrackNames = new List<string> { "Lead" };
        saved.Video.Range = 2;
        saved.Video.Directory = @"C:\Video";
        saved.TrackMeter.Tap = TabForge.Audio.Metering.TrackMeterSettings.PreFader;
        saved.TrackMeter.ApplyToAll = false;
    }

    private static DockWorkspaceState Layout(string panel) => new()
    {
        Version = 7,
        Root = new DockNodeState { Kind = "tabs", Panels = { panel }, SelectedPanel = panel },
        ClosedPanels = { panel + ".closed" },
    };

    /// <summary>The user data and chrome Reset all must not touch (Render's SafetyLimiter is a Settings row, so it is left out).</summary>
    private static string SnapshotUserData(AppSettings s) => JsonSerializer.Serialize(new
    {
        s.Workspace, s.WorkspaceBeforeSideHide, s.SavedLayouts, s.LastLayout, s.EffectPresets, s.RememberedFolders, s.MixerRules,
        s.WindowWidth, s.WindowHeight, s.WindowLeft, s.WindowTop, s.Maximised,
        s.SettingsWindowWidth, s.SettingsWindowHeight, s.MixerWindowWidth, s.MixerWindowHeight,
        s.MidiExport, s.Video, s.TrackMeter,
        Render = new
        {
            s.Render.Source, s.Render.StemTrackNames, s.Render.Bounds, s.Render.CustomStartSec, s.Render.CustomEndSec,
            s.Render.CustomFromBar, s.Render.CustomToBar, s.Render.CustomFromSectionBar, s.Render.CustomToSectionBar,
            s.Render.TailMode, s.Render.TailMs, s.Render.Directory, s.Render.Pattern, s.Render.SampleRate, s.Render.Mono,
            s.Render.SingleThread, s.Render.RealtimePace, s.Render.Format, s.Render.Mp3Kbps, s.Render.OpenFolder,
        },
    });

    private static AvaloniaDialogHost Host(List<string> questions) => new()
    {
        Capture = dialog =>
        {
            questions.Add(string.Join("\n", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)));
            return MessageBoxAnswer.Yes;
        },
    };
}
