using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Presets;
using TabForge.Documents;
using TabForge.Playback;
using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;
using TabForge.Views.EffectEditors;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the decision for each parity.capture.json shot base (the shot name without its -dark or -light suffix): the Avalonia window or
/// pane that renders it, the size in DIP the shot was taken at, the state it reproduces, and for every shot without a twin the reason.
/// Does not own: the manifest (tests/parity/parity.capture.json), the rendering and the missing list (ParityShotsTests), the pixel
/// comparison (tools/ab/compare.py).
/// Tests: ParityShotsTests (every manifest shot must have an entry here).
/// </summary>
internal static class ParityShotMap
{
    private const string ContextReason = "Context menus open as popup windows, which the window renderer does not capture; no twin yet.";
    private const string EffectReason = "The note effect editor for track 0, bar 3 is not reproduced yet.";

    /// <summary>The 14 menu shots (each in both themes): the shot name without its prefix, the top-level menu header it opens, and what the twin shows.</summary>
    private const string OrnamentNote = "The demo song's track 0, bar 3 with a fret 5 typed on the cursor's string in beat 1 (the WPF note step).";
    private static readonly (string Menu, string Header, string Note)[] MenuShots =
    {
        ("File", "_File", "The File menu open, cropped to its popup."),
        ("Edit", "_Edit", "The Edit menu open, cropped to its popup."),
        ("Note", "_Note", "The Note menu open, cropped to its popup."),
        ("Bar", "_Bar", "The Bar menu open, cropped to its popup."),
        ("Track", "_Track", "The Track menu open, cropped to its popup."),
        ("Sections", "_Sections", "The Sections menu open, cropped to its popup."),
        ("View", "_View", "The View menu open, cropped to its popup."),
        ("Effects", "_Effects", "The Effects menu open, cropped to its popup."),
        ("Sound", "_Sound", "The Sound menu open, cropped to its popup."),
        ("Tools", "_Tools", "The Tools menu open, cropped to its popup."),
        ("Options", "_Options", "The Options menu open, cropped to its popup."),
        ("Help", "_Help", "The Help menu open, cropped to its popup."),
    };

    // Key: the shot base name. Ceiling: one entry per base of parity.capture.json (74 today). Thread: built once by the static
    // initialiser, read-only afterwards, read on the UI thread by the tests. MenuShots is declared above so Build can read it.
    public static IReadOnlyDictionary<string, ParityEntry> Entries { get; } = Build();

    private static Dictionary<string, ParityEntry> Build()
    {
        var map = new Dictionary<string, ParityEntry>(StringComparer.Ordinal);
        AddTwins(map);
        AddMissing(map);
        return map;
    }

    private static void AddTwins(IDictionary<string, ParityEntry> map)
    {
        map["win-default"] = new(() => new ParityCapture(new MainWindow { Width = 1600, Height = 1000 }), "Demo song at the default layout.");
        map["edit-score"] = new(() => MainPart<ScorePreviewView>(), "Score pane of the main window, cropped to the pane.");
        map["context-score"] = new(() => new ParityCapture(new MainWindow { Width = 1600, Height = 1000 }, window =>
        {
            var score = window.GetLogicalDescendants().OfType<ScorePreviewView>().Single();
            return MenuPopupCapture.OpenContext(score, new Point(10, 10), _ => ContextMenuRows.ScoreAt(score.ContextMenus, score.ScoreState()), score.ContextMenus.Commands);
        }),
            "The score's empty-area menu at the default score settings: standard notation and outline shown, zoom and page layout closed, no paste.");
        map["context-timeline"] = new(() => new ParityCapture(new MainWindow { Width = 1600, Height = 1000 }, window =>
        {
            var timeline = window.GetLogicalDescendants().OfType<TimelineView>().Single();
            return MenuPopupCapture.OpenContext(timeline, new Point(timeline.BarX(12) + 2, timeline.RowTop(0) + 4), p => ContextMenuRows.TimelineAt(timeline, p), timeline.ContextMenus.Commands);
        }), "The bar menu on bar 13 of the first track of the demo song, in its section, with the bar's Insert, Section and All tracks submenus.");
        map["context-track-row"] = new(() => new ParityCapture(new MainWindow { Width = 1600, Height = 1000 }, window =>
        {
            var timeline = window.GetLogicalDescendants().OfType<TimelineView>().Single();
            var header = window.GetLogicalDescendants().OfType<TrackHeaderColumn>().Single();
            return MenuPopupCapture.OpenContext(header, new Point(10, timeline.RowTop(0) + 4), p => ContextMenuRows.TrackRowAt(timeline, p), timeline.ContextMenus.Commands);
        }), "The track row menu of the first track of the demo song: a guitar track with master track hidden.");
        map["context-timeline-empty"] = new(() => new ParityCapture(new MainWindow { Width = 1600, Height = 1000 }, window =>
        {
            var timeline = window.GetLogicalDescendants().OfType<TimelineView>().Single();
            var header = window.GetLogicalDescendants().OfType<TrackHeaderColumn>().Single();
            return MenuPopupCapture.OpenContext(header, new Point(10, timeline.RowTop(timeline.Song.Tracks.Count) + 4), p => ContextMenuRows.TrackRowAt(timeline, p), timeline.ContextMenus.Commands);
        }), "The empty track list menu of the demo song, below its last track: groups off, master track hidden, the Colours submenu closed.");
        map["edit-timeline"] = new(() => MainPart<ArrangementView>(), "Arrangement pane of the main window, cropped to the pane.");
        map["edit-fretboard"] = new(() => MainPart<InstrumentPanelStrip>(), "Instrument pane of the main window, cropped to the pane.");
        map["panel-instrument"] = new(() => MainPart<InstrumentPanelStrip>(), "Same pane as edit-fretboard, cropped to the panel.");
        map["panel-timeline"] = new(() => MainPart<ArrangementView>(), "Same pane as edit-timeline, cropped to the panel.");
        map["panel-sections"] = new(() => Pane(new Window { Content = new SectionsPanelView(FullDemoSongFactory.Create().Markers) }, 333, 192), "The Sections pane with the demo song's sections in bar order (Intro: Embers ... Fake-out), no row selected. The WPF shot is the dock tab group (Sections and Lyrics tabs) and the light pane at 200 %; the twin is the pane only, without the tab strip.");
        map["win-TabStrip"] = new(() => TabStripTwin(),"Demo tabs at 1100 px as the WPF strip: playing elsewhere, playing here (active), unsaved, a long title, playing and unsaved. The WPF '(+)' is the only other control in the shot.");
        map["win-MessageBox"] = new(() => new ParityCapture(new AvaloniaMessageBox(PresetDeleteText, PresetManagerWindow.Caption,MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxAnswer.None, PresetDeleteQuestionPresenter.RememberText)), "The preset delete question for three presets ('Warm pad', 'Bright lead', 'Tight rhythm') with the 'Don't ask again' tick unticked.");
        map["win-Mixer"] = new(() => { var mixer = new MixerWindow(new SongDocument(() => null) { Project = FullDemoSongFactory.Create() }); mixer.EnsureStripsBuilt(); return Sized(mixer, 1180, 560); }, "Full demo song (the WPF capture's), default state.");
        map["win-GroupRules"] = new(() => Sized(new GroupRulesWindow(new MixerSettings()), 640, 660), "Default mixer settings.");
        map["win-FxChain"] = new(() => Sized(FxTestKit.Window(FxTestKit.Track(null)), 980, 620), "Empty chain on 'Sample track' (No plug-ins yet).");
        map["win-FxChain-example"] = new(() => Sized(FxTestKit.Window(FxTestKit.Track(ExampleChain())), 980, 620), "Two example plug-ins: 'Example instrument' (INST, selected) and 'Example effect' (FX, wet 80).");
        map["win-Render"] = new(() => Sized(new RenderWindow(FullDemoSongFactory.Create(), new RenderSettings(), false, null), 760, 860), "Demo song, default settings, no selection.");
        map["win-AddTrack"] = new(() => Sized(new PromptWindow(PromptTextsPresenter.AddTrack()), 800, 781), "The add-track prompt.");
        map["win-TrackProperties"] = new(() => Sized(new TrackPropertiesWindow(FullDemoSongFactory.Create().Tracks[0]), 800, 811), "Demo track 0.");
        map["win-Tuner"] = new(() => Sized(new TunerWindow(FullDemoSongFactory.Create().Tracks[0], TunerWindow.SampleHz), 420, 330), "Demo track 0, sample pitch.");
        map["win-ProjectSettings"] = new(() => Sized(new ProjectSettingsWindow(SongInfoSample, 3, 40, 2), 660, 600), "The Ashen Meridian song's settings (the same values as the Credits sample).");
        map["win-SongInfo"] = new(() => Sized(new ScoreInfoWindow(new ScoreInfoValues(SongInfoSample.Title, SongInfoSample.Subtitle, SongInfoSample.Artist, SongInfoSample.Album, SongInfoSample.MusicAuthor, SongInfoSample.LyricsAuthor, SongInfoSample.Copyright, SongInfoSample.TabAuthor, SongInfoSample.Instructions, SongInfoSample.Notice)), 480, 560), "Score information (the WPF Song info dialog, GpDialogs): one flat form of ten texts with the WPF song's values.");
        map["win-GlobalTuning"] = new(() => Sized(new GlobalTuningWindow(GlobalTuningPresenter.StandardE, TuningPresets.SixStringPresets), 640, 437), "Standard E.");
        map["win-ScaleFinder"] = new(() => { var song = FullDemoSongFactory.Create(); return Sized(new ScaleFinderWindow(song, song.Tracks[0], null, null), 560, 600); }, "Demo track 0, no selection.");
        map["win-InstrumentPicker"] = new(() => Sized(new InstrumentPickerWindow(InstrumentPickerWindow.CatalogueEntries(), "Distortion Guitar"), 760, 620), "Full catalogue with 'Distortion Guitar' current.");
        map["win-ScoreTextStyle"] = new(() => Sized(new ScoreTextStyleWindow(new Dictionary<string, ScoreTextAreaStyle>(), new[] { "Arial", "Consolas" }), 520, 281), "No styles, sample fonts.");
        map["win-MixTable"] = new(() => Sized(new MixTableWindow(MixTableWindow.SamplePresenter()), 470, 415), "Full demo song track 0 (the WPF capture's).");
        map["win-ChordFinder"] = new(() => Sized(new ChordFinderWindow(_ => false), 380, 330), "Default state.");
        map["win-SongStats"] = new(() => Sized(new SongStatsWindow(FullDemoSongFactory.Create(), "demo.tforge"), 400, 190), "The demo song saved as demo.tforge (the WPF shot shows that file name).");
        map["win-AddPlugin"] = new(() => Sized(new PluginBrowserWindow(new TabForge.Avalonia.Views.Fx.PluginBrowserSession(new TabForge.Services.PluginSettings(), new TabForge.Plugins.PluginTrust(), new TabForge.Platform.Windows.WindowsPluginFileTrust(), () => { }), new AvaloniaFileDialogs(), _ => { }), 760, 560), "No plug-ins found yet, nothing probed, nothing unidentified (as the WPF shot: no plug-in folder scanned).");
        map["win-PasteOptions"] = new(() => Sized(new PasteOptionsWindow(new[] { PasteQuestion.BeatsOntoNotes, PasteQuestion.Octave }), 470, 345),
            "The two paste questions of the WPF capture: beats onto existing notes, and the octave shift between instruments.");
        map["win-Preferences"] = new(() => Sized(PreferencesOn(SettingsCatalog.Appearance), 1294, 857), "Appearance page.");
        map["win-Shortcuts"] = new(() => Sized(PreferencesOn(SettingsCatalog.Hotkeys), 1294, 857), "Shortcuts (hotkeys) page.");
        map["win-CommandPalette"] = new(() => Sized(CommandPaletteWithQuery("tun"), 520, 420), "Sample commands, search text 'tun'.");
        map["win-PresetManager"] = new(() => Sized(new PresetManagerWindow(new AvaloniaDialogHost()), 900, 500), "Plugin presets, empty host; the WPF shot had a seeded preset panel.");
        map["win-VideoExport"] = new(() => Sized(new VideoExportWindow(FullDemoSongFactory.Create(), new VideoSettings(), false), 640, 760), "Demo song, default settings, no selection.");
        map["play-learn"] = new(() => PlayingMain("learn"), "The main window at 1600 x 1000 DIP playing bar 13 at 0.4 (PlayingStateStub) with the docked Keyboard mode pane open.");
        map["play-playhead"] = new(() => PlayingMain(null), "The main window at 1600 x 1000 DIP playing bar 13 at 0.4 (PlayingStateStub): score band and playhead, timeline playhead.");
        map["play-band"] = new(() => PlayingMain("band"), "The main window at 1600 x 1000 DIP playing bar 13 at 0.4 (PlayingStateStub) with the Band view pane open.");
        map["win-KeyboardModePopout"] = new(KeyboardPopoutPlaying,"The WPF capture's play-along state (KeyboardPlayAlongStub: bar 13 at 0.4, track 0 as a two-hand riff), the bar and the notes; the MIDI device list is not reproduced.");
        map["win-Wiring"] = new(() => WiringFor(FullDemoSongFactory.Create()), "Sample slot on demo track 0; the WPF example plugins are not reproduced.");
        map["win-MidiProcessing"] = new(() => Sized(MidiShot(), 1020, 620), "Empty processor list (No processors yet).");
        foreach (var (menu, header, note) in MenuShots)
            map["menu-" + menu] = new(() => MenuPopup(header), note);
        map["menu-View-Layouts"] = new(() => SubmenuPopup("_View", "Layouts"), "The View menu's Layouts submenu open, cropped to its popup.");
        map["menu-View-Panels"] = new(() => SubmenuPopup("_View", "Panels"), "The View menu's Panels submenu open, cropped to its popup.");
        foreach (var (id, title, tools) in PaletteToolCatalog.PaletteTabs)
            map["panel-" + id] = new(() => PaletteTabTwin(title, tools), $"The {title} tab: the palette's tab strip drawn by the test (the dock draws it in the app) above the pane, at the WPF shot size.");
    }

    private static void AddMissing(IDictionary<string, ParityEntry> map)
    {
        map["win-NewFromTemplate"] = new(null, "No Avalonia new-from-template window yet.");
        map["win-NoteMapEditor"] = new(null, "No Avalonia note map editor window yet.");
        map["win-DeletePrompt"] = new(null, "Needs the bar selection (12 to 14) and the delete prompt state, which are not reproduced yet.");
        foreach (var context in new[] { "note", "fretboard", "mixer-colour" })
            map["context-" + context] = new(null, ContextReason);
        // panel-structure, panel-rhythm and panel-layout have twins (AddPaletteTabTwins).
        map["effect-bend"] = new(() => Pane(CurveEffectTwin(EffectEditorKind.Bend), 824, 385),
            "The bend editor for track 0, bar 3 with no bend yet: the default Bend curve, the built-in presets, Clean disabled, OK and Cancel.");
        map["effect-tremolobar"] = new(() => Pane(CurveEffectTwin(EffectEditorKind.TremoloBar), 824, 503),
            "The tremolo bar editor for track 0, bar 3 with no tremolo bar yet: the default Dip curve, the built-in presets, Clean disabled, OK and Cancel.");
        map["effect-trill"] = new(() => OrnamentTwin(flow => flow.BuildTrill(), 606, 379), OrnamentNote + " The trill editor with its starting values.");
        map["effect-grace"] = new(() => OrnamentTwin(flow => flow.BuildGrace(), 714, 426), OrnamentNote + " The grace note editor with its starting values.");
        map["effect-harmonic"] = new(() => OrnamentTwin(flow => flow.BuildHarmonic(), 707, 402), OrnamentNote + " The harmonic editor with its starting values.");
        map["instrument-drums"] = new(null, "The instrument pane for track 3 set to drums is not reproduced yet.");
        map["instrument-keyboard"] = new(null, "The instrument pane for track 0 set to keyboard is not reproduced yet.");
    }

    /// <summary>The curve editor as the note-effect editor opens on a note without the effect: its default curve and the built-in presets.
    /// Its size is set by the caller, so the window is not sized to its content.</summary>
    private static CurveEffectWindow CurveEffectTwin(EffectEditorKind kind)
    {
        var bend = kind == EffectEditorKind.Bend;
        var window = new CurveEffectWindow(kind, bend ? "Bend editor" : "Tremolo bar editor", bend ? 0 : -EffectCurve.TremoloRangeValue,
            bend ? EffectCurve.BendMaxValue : EffectCurve.TremoloRangeValue, EffectPresetStore.BuiltIn(kind)[0].Points, new EffectPresetStore(new AppSettings()),
            false, () => { });
        window.SizeToContent = SizeToContent.Manual;
        return window;
    }

    private static ParityCapture MainPart<T>() where T : Control =>
        new(new MainWindow { Width = 1600, Height = 1000 }, window => window.GetLogicalDescendants().OfType<T>().Single());

    private static ParityCapture SubmenuPopup(string topHeader, string submenuHeader) =>
        new(new MainWindow { Width = 1600, Height = 1000 }, window => MenuPopupCapture.OpenSubmenu(window, topHeader, submenuHeader));

    private static ParityCapture MenuPopup(string header) =>
        new(new MainWindow { Width = 1600, Height = 1000 }, window => MenuPopupCapture.OpenTopLevel(window, header));

    /// <summary>The strip shot: the tab strip on the title-bar colour, 4 px above and below, 1100 DIP wide.</summary>
    private static ParityCapture TabStripTwin()
    {
        var strip = new TabStripView(new TabForge.Services.TabSettings());
        strip.Show(new[]
        {
            new TabStripDocument(null, "Idle song"),
            new TabStripDocument(null, "Playing elsewhere", Playing: true),
            new TabStripDocument(null, "Playing here", Playing: true),
            new TabStripDocument(null, "Unsaved song", Unsaved: true),
            new TabStripDocument(null, "A very long song title that keeps going and going"),
            new TabStripDocument(null, "Playing and unsaved", Unsaved: true, Playing: true),
        }, 2, 1100);
        var chrome = new Border { Padding = new Thickness(0, 4), Child = strip };
        chrome[!Border.BackgroundProperty] = new DynamicResourceExtension("ChromeStrip");
        // Not Sized: that adds the window frame padding (39 DIP at the bottom), which would clip this 44 DIP strip to a few pixels.
        return new ParityCapture(new Window { Width = 1100, Height = 44, Content = chrome });
    }

    private static string PresetDeleteText => PresetDeleteQuestionPresenter.TextFor(new[] { "Warm pad", "Bright lead", "Tight rhythm" });

    /// <summary>The playback captures: the Ashen Meridian song playing bar 13 at 0.4 in a 1600 x 1000 main window, with <paramref name="pane"/> ("band" or "learn") open, or none.</summary>
    private static ParityCapture PlayingMain(string? pane)
    {
        var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(global::Avalonia.Threading.Dispatcher.UIThread), () => new PlayingStateStub(12, 0.4));
        var document = new SongDocument(() => null) { Project = FullDemoSongFactory.Create() };
        document.MarkClean();
        var window = new MainWindow(null, null, document, sessions) { Width = 1600, Height = 1000 };
        if (pane is not null) window.DockWorkspace.SetPanelVisible(pane, true);
        var session = sessions.For(document)!;
        session.PlayOrPause();
        session.PumpFrame();
        return new ParityCapture(window);
    }

    private static ParityCapture KeyboardPopoutPlaying()
    {
        var song = FullDemoSongFactory.Create();
        var window = new KeyboardModePopoutWindow();
        window.ShowPlayAlong(KeyboardPlayAlongStub.Build(song, 0, 12, 0.4), song.Tracks[0].Name);
        return Sized(window, 1100, 680);
    }

    /// <summary>A palette tab: the strip of four tab names (the selected one boxed) above the pane's rows.</summary>
    private static ParityCapture PaletteTabTwin(string selected, PaletteTool[] tools)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 4, 4, 0), Spacing = 6 };
        foreach (var (_, title, _) in PaletteToolCatalog.PaletteTabs)
        {
            var label = new TextBlock { Text = title, FontSize = 12, FontWeight = title == selected ? global::Avalonia.Media.FontWeight.SemiBold : global::Avalonia.Media.FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
            var chip = new Border { Padding = new Thickness(9, 7), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Child = label };
            if (title == selected)
            {
                chip[!Border.BackgroundProperty] = new DynamicResourceExtension("AccentSoftBrush");
                chip[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
            }
            strip.Children.Add(chip);
        }
        var pane = new ToolsPaneView(new ScorePreviewView(), tools) { Margin = new Thickness(8, 3, 8, 8) };
        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(strip, Dock.Top);
        body.Children.Add(strip);
        body.Children.Add(pane);
        body[!DockPanel.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        return Pane(new Window { Content = body }, 333, 211);
    }

    /// <summary>
    /// The non-client frame of a WPF window at 100 % DPI, which WPF's Window.Width and Height include: the resize border is 8 DIP
    /// on each side (16 in all across the width). Measured on the WPF Global tuning shot: its content, with 14 DIP margins,
    /// ends 15.5 DIP before the right edge of the 640 DIP shot.
    /// </summary>
    private const double WpfWindowFrameWidth = 16;

    /// <summary>The frame's height: caption (23) and the padded resize border on top (8), plus the resize border at the bottom (8).</summary>
    private const double WpfWindowFrameHeight = 39;

    /// <summary>A note-effect editor opened on the cursor note of the demo song (track 0, bar index 2 (bar 3), string index 2, fret 5 typed in memory), not shown.
    /// The window keeps the dialog's outer size, so its layout is not resized to its content.</summary>
    private static ParityCapture OrnamentTwin(Func<OrnamentEditorFlow, OrnamentEditorFlow.OrnamentDialog?> build, double width, double height)
    {
        var view = new ScorePreviewView();
        view.Editor.SetPosition(2, 0, 2);
        view.Editor.Effects.EnterFret(5, autoAdvance: false);
        var flow = new OrnamentEditorFlow(view, _ => System.Threading.Tasks.Task.FromResult<EffectValuesAnswer?>(null));
        var dialog = build(flow)!.Window;
        dialog.SizeToContent = SizeToContent.Manual;
        return Sized(dialog, width, height);
    }

    /// <summary>
    /// A WPF window shot at the outer Width and Height the window was given. The shot paints the frame area with the window
    /// background and lays the content out in the client area, so the window keeps the outer size and its content gets the frame as padding on the right and bottom.
    /// </summary>
    private static ParityCapture Sized(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        window.Padding = new Thickness(0, 0, WpfWindowFrameWidth, WpfWindowFrameHeight);
        return new ParityCapture(window);
    }

    /// <summary>A pane shot whose WPF reference is the element itself, not a window: the size is used as it is.</summary>
    private static ParityCapture Pane(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        return new ParityCapture(window);
    }

    private static PreferencesWindow PreferencesOn(string page)
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
        window.ShowPage(page);
        return window;
    }

    private static CommandPaletteWindow CommandPaletteWithQuery(string query)
    {
        var window = new CommandPaletteWindow(CommandPaletteWindow.SampleEntries);
        window.GetLogicalDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Search commands").Text = query;
        return window;
    }

    private static ParityCapture WiringFor(SongProject song) =>
        Sized(new WiringWindow(song.Tracks, song.Tracks[0], WiringWindow.SampleSlot(), change => change()), 420, 467);

    // The WPF SongInfo shot's credits (tempo and time signature are not on that page; the sample values stand in).
    private static readonly ProjectSettingsResult SongInfoSample = new("Ashen Meridian", "Feature test song: not meant to be taken seriously",
        "TabForge Demo", "Examples", "TabForge contributors", "TabForge contributors", "TabForge", "(c) 2026 TabForge contributors, CC0 1.0",
        120, 4, 4, 0, false, false,
        "Original composition written for TabForge. Public domain (CC0 1.0). RhyL 7-string drop A, RhyR 8-string, Clean capo 5.",
        "A demo and test song: it deliberately uses every technique, notation and playback option TabForge supports, so it is not meant to be taken seriously as music. Every riff, melody, lyric and drum part was written for TabForge.",
        "");

    private const string DeleteBarsText = "Bars 12-14 are selected. What should Delete do?";

    /// <summary>The two example plug-ins of the WPF FxChain-example shot: an instrument, then an effect at 80 % wet.</summary>
    private static MidiProcessingWindow MidiShot()
    {
        var slot = new PluginSlot { Name = "Sample synth", Type = PluginSlotType.Instrument };
        var track = FxTestKit.Track(new[] { slot });
        return new MidiProcessingWindow(FxTestKit.Host(track), track, slot, apply => apply());
    }

    private static List<PluginSlot> ExampleChain() =>
    [
        new PluginSlot { Name = "Example instrument", Path = @"example\Example instrument.dll", Format = "VST2", Type = PluginSlotType.Instrument },
        new PluginSlot { Name = "Example effect", Path = @"example\Example effect.dll", Format = "VST2", Wet = 80 },
    ];

    /// <summary>The Credits tab of the project settings, selected, with the song's credits.</summary>
}

/// <summary>What one shot shows: the window to show and, for a pane, the control inside it to crop to (null for the whole window).</summary>
internal sealed record ParityCapture(Window Window, Func<Window, Control>? Part = null);

/// <summary>The twin of one shot base. Build is null when the shot has no Avalonia twin yet; Note then says why, otherwise it says the state the twin reproduces.</summary>
internal sealed record ParityEntry(Func<ParityCapture>? Build, string Note);
