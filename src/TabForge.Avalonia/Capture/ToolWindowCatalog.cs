using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Capture;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;

namespace TabForge.Avalonia.Capture;

// Owns: the tool windows a script can open by name, each with the demo song or default settings and its default size (DIPs).
// Does not own: opening, placing and closing them (ToolSteps), windows that need a real plug-in (see CaptureSteps notes in tools/capture).
// Tests: TabForge.Avalonia.Tests.CaptureSessionTests.
internal sealed class ToolWindowCatalog(CaptureSession session)
{
    private readonly Dictionary<string, Func<CaptureStep, Window>> _windows = new(StringComparer.Ordinal)
    {
        ["Mixer"] = (_ => new MixerWindow(session.Document)),
        ["GroupRules"] = (_ => new GroupRulesWindow(new MixerSettings())),
        ["Render"] = (_ => new RenderWindow(session.Document.Project, new RenderSettings(), false, null)),
        ["VideoExport"] = (_ => new VideoExportWindow(session.Document.Project, new VideoSettings(), false)),
        ["AddTrack"] = (_ => new PromptWindow(PromptTextsPresenter.AddTrack())),
        ["TrackProperties"] = (s => new TrackPropertiesWindow(session.Document.Project.Tracks[s.Option("track", 0)])),
        ["Tuner"] = (s => new TunerWindow(session.Document.Project.Tracks[s.Option("track", 0)], TunerWindow.SampleHz)),
        ["GlobalTuning"] = (_ => new GlobalTuningWindow(GlobalTuningPresenter.StandardE, TuningPresets.SixStringPresets)),
        ["InstrumentPicker"] = (s => new InstrumentPickerWindow(InstrumentPickerWindow.CatalogueEntries(), s.Option("current", "Distortion Guitar")!)),
        ["ScoreTextStyle"] = (_ => new ScoreTextStyleWindow(new Dictionary<string, ScoreTextAreaStyle>(), new[] { "Arial", "Consolas" })),
        ["MixTable"] = (_ => new MixTableWindow(MixTableWindow.SamplePresenter())),
        ["ChordFinder"] = (_ => new ChordFinderWindow(_ => false)),
        ["SongStats"] = (_ => new SongStatsWindow(session.Document.Project, "demo.tforge")),
        ["PasteOptions"] = (_ => new PasteOptionsWindow(new[] { PasteQuestion.BeatsOntoNotes, PasteQuestion.Octave })),
        ["Preferences"] = (s => Preferences(s)),
        ["CommandPalette"] = (s => CommandPalette(s)),
    };

    /// <summary>The names a script can open.</summary>
    public IEnumerable<string> Names => _windows.Keys;

    /// <summary>Builds the named window at its default size (the step's "size" option overrides it); not shown.</summary>
    public Window Build(string name, CaptureStep step)
    {
        if (!_windows.TryGetValue(name, out var entry)) throw new InvalidOperationException($"No tool window named \"{name}\". Known: {string.Join(", ", Names)}.");
        var window = entry(step);
        var (across, down) = CaptureMetrics.ToolSize(name);
        var size = step.NumbersOption("size");
        window.Width = size?[0] ?? across;
        window.Height = size?[1] ?? down;
        return window;
    }

    private static PreferencesWindow Preferences(CaptureStep step)
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
        window.ShowPage(step.Option("page", SettingsCatalog.Appearance)!);
        if (step.Option("search") is { } query) window.Search(query);
        return window;
    }

    private static CommandPaletteWindow CommandPalette(CaptureStep step)
    {
        var window = new CommandPaletteWindow(CommandPaletteWindow.SampleEntries);
        if (step.Option("text") is { } text)
            window.GetLogicalDescendants().OfType<TextBox>().Single(t => global::Avalonia.Automation.AutomationProperties.GetName(t) == "Search commands").Text = text;
        return window;
    }
}
