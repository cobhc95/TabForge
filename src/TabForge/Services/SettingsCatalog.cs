using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

public enum SettingKind { Bool, Choice, Number, Text, Colour, Button }

/// <summary>A searchable, grouped setting descriptor bound to a live settings model.</summary>
public sealed class SettingDescriptor
{
    public required string Key { get; init; }
    public required string Category { get; set; }
    public required string Group { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public SettingKind Kind { get; init; } = SettingKind.Bool;
    public IReadOnlyList<string> Choices { get; init; } = Array.Empty<string>();
    public double Min { get; init; }
    public double Max { get; init; } = 100;
    public double Step { get; init; } = 1;
    public int Decimals { get; init; }
    public string Unit { get; init; } = "";
    public string? DependsOn { get; init; }
    public string? DependsOnValue { get; init; }
    public string? HotkeyAction { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
    public required Func<object?> Get { get; init; }
    public required Action<object?> Set { get; init; }
    public string SearchText { get; init; } = "";
    /// <summary>True for a row shown inside its group's collapsed "More options"; false for a Basic row that is always visible.</summary>
    public bool More { get; init; }
    /// <summary>Sort key inside a page: the group's position, then the row's position (see <see cref="SettingsCatalog"/> layout table).</summary>
    public int Order { get; init; }

    public string Tooltip(HotkeySettings hotkeys)
    {
        var text = string.IsNullOrWhiteSpace(Description) ? Title : Description;
        return HotkeyAction is null ? text : HotkeyCatalog.TooltipWithKey(hotkeys, text, HotkeyAction);
    }
}

/// <summary>Declarative catalogue of settings exposed in the Settings window.</summary>
public static class SettingsCatalog
{
    // Pages, in rail order (Preferences reorganisation, owner decisions 2026-09-30). The display name is the page key.
    public const string Home = "Common settings";
    public const string General = "General";
    public const string Appearance = "Appearance";
    public const string Score = "Score & Notation";
    public const string Fretboard = "Fretboard & Keyboard";
    public const string Timeline = "Timeline & Tracks";
    public const string Editing = "Editing";
    /// <summary>Scrolling while playing, the playing highlight, speed, metronome, count-in and note preview.</summary>
    public const string Playback = "Playback & Practice";
    public const string Audio = Playback;
    public const string AudioVst = "Audio & Plug-ins";
    public const string Recording = "Recording";
    public const string Tabs = "Tabs & Windows";
    public const string Hotkeys = "Shortcuts";
    public const string Files = "Files & Backups";
    public const string Advanced = "Advanced";

    /// <summary>Stable page ids (for deep links that should survive a rename) and the old display names, all mapped to the page.</summary>
    private static readonly Dictionary<string, string> PageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = Home, ["general"] = General, ["appearance"] = Appearance, ["score"] = Score, ["fretboard"] = Fretboard,
        ["timeline"] = Timeline, ["editing"] = Editing, ["playback"] = Playback, ["audio"] = AudioVst, ["recording"] = Recording,
        ["tabs"] = Tabs, ["shortcuts"] = Hotkeys, ["files"] = Files, ["advanced"] = Advanced,
        // Display names before the reorganisation.
        ["Appearance & colours"] = Appearance, ["Score & notation"] = Score, ["Playback & sound"] = Playback,
        ["Audio & VST"] = AudioVst, ["Timeline & sections"] = Timeline, ["Tabs & windows"] = Tabs, ["Hotkeys"] = Hotkeys,
    };

    /// <summary>Resolves a page id, a current name or an old name to the page's name; null when unknown.</summary>
    public static string? ResolvePage(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        if (Categories.Contains(idOrName, StringComparer.OrdinalIgnoreCase)) return Categories.First(c => c.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
        return PageAliases.TryGetValue(idOrName.Trim(), out var page) ? page : null;
    }

    /// <summary>The rail's captions and the pages under each (non-clickable captions, like Windows 11 Settings).</summary>
    public static readonly IReadOnlyList<(string Band, string[] Pages)> Bands = new[]
    {
        ("", new[] { Home }),
        ("BASICS", new[] { General, Appearance }),
        ("MUSIC", new[] { Score, Fretboard, Timeline, Editing }),
        ("SOUND", new[] { Playback, AudioVst, Recording }),
        ("SYSTEM", new[] { Tabs, Hotkeys, Files, Advanced }),
    };

    public const string DefaultAudioDevice = "(Windows default)";

    /// <summary>The ASIO driver's channels as the driver names them: "1: Analogue 1".</summary>
    private static List<string> AsioInputs(PluginSettings pl) => Numbered(pl.Driver == AudioDrivers.Asio
        ? TabForge.Audio.AudioDevices.AsioChannelNames(pl.Device).Inputs : Enumerable.Range(1, 8).Select(i => $"Input {i}").ToArray());
    private static List<string> AsioOutputs(PluginSettings pl) => Numbered(pl.Driver == AudioDrivers.Asio
        ? TabForge.Audio.AudioDevices.AsioChannelNames(pl.Device).Outputs : Enumerable.Range(1, 8).Select(i => $"Output {i}").ToArray());
    private static List<string> Numbered(string[] names) => names.Select((n, i) => $"{i + 1}: {n}").ToList();

    private static string[] AudioInputChoices(PluginSettings settings)
    {
        var names = new List<string> { DefaultAudioDevice };
        names.AddRange(TabForge.Audio.AudioDevices.InputNames());
        if (settings.InputDevice.Length > 0 && !names.Contains(settings.InputDevice)) names.Add(settings.InputDevice);
        return names.Distinct().ToArray();
    }

    /// <summary>Output devices for the chosen driver (names only: no driver is loaded), plus the current choice.</summary>
    private static string[] AudioDeviceChoices(PluginSettings settings)
    {
        var names = new List<string> { DefaultAudioDevice };
        try
        {
            names.AddRange(TabForge.Audio.AudioDevices.Names(settings.Driver));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TypeInitializationException)
        {
            System.Diagnostics.Debug.WriteLine($"audio device list failed: {ex.Message}");
        }
        if (settings.Device.Length > 0 && !names.Contains(settings.Device)) names.Add(settings.Device);
        return names.Distinct().ToArray();
    }

    public static readonly IReadOnlyList<string> Categories = new[]
    {
        Home, General, Appearance, Score, Fretboard, Timeline, Editing, Playback, AudioVst, Recording, Tabs, Hotkeys, Files, Advanced
    };

    /// <summary>Everyday words that find a row by another name: alias -> the word the row's text uses (the alias itself still matches).</summary>
    private static readonly Dictionary<string, string[]> Synonyms = BuildSynonyms(
        "theme: dark light colour color skin mode",
        "audio: soundcard speakers headphones interface output driver device",
        "shortcuts: hotkey hotkeys keybinding keybindings key",
        "buffer: latency delay lag sync",
        "follow: scroll jump page-turn",
        "bar: measure",
        "metronome: click count-in",
        "fretboard: neck frets",
        "recording: input microphone take",
        "autosave: backup recovery",
        "font: text typeface size",
        "tablature: tab");

    private static Dictionary<string, string[]> BuildSynonyms(params string[] groups)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var parts = group.Split(':', 2);
            var head = parts[0].Trim();
            foreach (var alias in parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!map.TryGetValue(alias, out var list)) map[alias] = list = new List<string>();
                list.Add(head);
            }
        }
        return map.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    /// <summary>A query matches when every word of it is found (itself or through a synonym) in the row's text, in any order.</summary>
    public static bool Matches(SettingDescriptor descriptor, string lowerCaseQuery)
    {
        if (lowerCaseQuery.Length == 0 || descriptor.SearchText.Contains(lowerCaseQuery, StringComparison.Ordinal)) return true;
        var words = lowerCaseQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        foreach (var word in words)
        {
            if (descriptor.SearchText.Contains(word, StringComparison.Ordinal)) continue;
            if (Synonyms.TryGetValue(word, out var heads) && heads.Any(h => descriptor.SearchText.Contains(h, StringComparison.Ordinal))) continue;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Where every catalogue row lives: page, group, and Basic (always shown) or More (inside the group's collapsed "More options",
    /// marked with a trailing *). The order here is the order on the page. Only the place a row is shown changes; nothing is stored differently.
    /// </summary>
    private static readonly (string Page, string Group, string Keys)[] Layout =
    {
        (General, "Updates", "general.checkupdates"),
        (General, "Window", "general.restorewindow"),
        (General, "Confirmations", "general.confirmclose general.confirmdiscardsettings editing.confirmdelete timeline.confirmdelete"),

        (Appearance, "Theme", "appearance.thememode"),
        (Appearance, "Size and text", "appearance.uiscale appearance.density appearance.font* appearance.fontsize* appearance.iconsize* appearance.toolbaricons*"),
        (Appearance, "Panels and toolbars", "general.toolbar general.statusbar appearance.tabstrip"),
        (Appearance, "Track colours", "appearance.tracktint appearance.groupcolour.guitars* appearance.groupcolour.basses* appearance.groupcolour.keys* appearance.groupcolour.drums* appearance.groupcolour.other* appearance.groupcolour.other-instruments*"),
        (Appearance, "Interface colours", "appearance.accent"),
        (Appearance, "Motion", "appearance.reduceanimations appearance.animationspeed* timeline.draganimation*"),
        (Appearance, "Custom palette", "appearance.background* appearance.panel* appearance.titlebar* appearance.tabactive* appearance.tabhover* appearance.text* appearance.muted*"),

        (Score, "What is shown", "score.defaultnotation score.pagelayout score.scrolling score.barnumbers score.sectionheadings score.dynamics score.barnumberfrequency* score.ledger*"),
        (Score, "Spacing and size", "appearance.spacing score.systemspacing* score.measurespacing*"),
        (Score, "Text and fonts", "appearance.scorefont appearance.scorefontsize score.textfonts appearance.scorebold* appearance.scoreitalic*"),
        (Score, "Appearance", "appearance.paper appearance.scorepaper.dark* appearance.scorepaper.light* appearance.scoreink.dark* appearance.scoreink.light* appearance.scorelines.dark* appearance.scorelines.light* appearance.cursor* score.staffopacity* appearance.selection* appearance.hover* score.hoverintensity* score.selectionintensity*"),

        (Fretboard, "Panel", "appearance.fretboard fretboard.position fretboard.instrumentview fretboard.showallas fretboard.locksize"),
        (Fretboard, "Guitar fretboard", "editing.frets editing.lefthanded editing.notenames"),
        (Fretboard, "Keyboard", "fretboard.keyboardkeys fretboard.keyboardcolours*"),
        (Fretboard, "Practice aids", "editing.horizon.enabled editing.scale follow.fretboard editing.horizon* audio.fretboardstyle*"),
        (Fretboard, "Appearance", "fretboard.scalestyle* fretboard.scalecolour* fretboard.scalestrength* fretboard.markercolour* fretboard.markerbrightness* fretboard.numbersize* fretboard.stringspacing*"),

        (Timeline, "Track list", "timeline.volumestyle timeline.panstyle timeline.autofit timeline.trackgroups"),
        (Timeline, "Clip lanes", "timeline.removeemptylanes"),
        (Timeline, "Timeline display", "appearance.arrangement timeline.numbers timeline.individualnotes timeline.continuousline timeline.hideemptygrid* timeline.barglow* timeline.playheadstyle* appearance.timelinescrollbar*"),
        (Timeline, "Sections", "timeline.similarcolours timeline.brackets timeline.names follow.sectionglow* appearance.sectionbracket*"),

        (Editing, "Note entry", "editing.duration editing.advance editing.reverseplusminus* editing.preventoverflow*"),
        (Editing, "Mouse and scrolling", "editing.scorewheel*"),
        (Editing, "Copy and paste", "editing.paste.beats* editing.paste.octave* editing.paste.bars* editing.paste.barsettings* editing.paste.drums*"),

        (Playback, "Scrolling and following", "follow.mode follow.horizontal follow.vertical follow.anticipation* follow.verticaltrigger* follow.margin* follow.stopmanual* follow.stopatend* follow.fps*"),
        (Playback, "Appearance", "follow.highlight follow.colour follow.playhead follow.bg* follow.duration.enabled* follow.durationglow* follow.durationopacity* follow.playhead.thickness*"),
        (Playback, "Practice", "audio.metronome audio.countin audio.speed audio.countinbars*"),
        (Playback, "Metronome", "audio.metrovolume audio.metroclick* audio.metroaccent* audio.metroaccentvolume* audio.metroclickvolume* audio.metrodivision*"),
        (Playback, "Note preview", "audio.preview audio.previewlen* audio.letring*"),
        (Playback, "Several tabs", "tabs.playbackonswitch"),

        (AudioVst, "Output device", "vst.driver vst.device vst.buffer vst.renderlimiter vst.samplerate* vst.asio.out* vst.asio.outlast* vst.followvolume* vst.livelimiter*"),
        (AudioVst, "Playback engine", "vst.playall* vst.autogm* vst.winmidilatency*"),
        (AudioVst, "Plug-ins", "vst.folders vst.commonfolders* vst.rememberscan* vst.dock* vst.ontop* vst.scanstandard* vst.isolate* vst.autopitch* vst.startuptracks* vst.quarantine*"),

        (Recording, "Input device", "vst.input vst.asio.inputs vst.asio.infirst* vst.asio.inlast*"),
        (Recording, "Timing", "vst.recordoffset*"),

        (Tabs, "Opening and closing tabs", "tabs.closebutton tabs.lastclosed tabs.newposition tabs.openincurrent tabs.playing"),
        (Tabs, "Tab look", "tabs.style* tabs.width* tabs.maxwidth* tabs.minwidth* tabs.height* tabs.fontsize*"),
        (Tabs, "Mouse actions", "tabs.doubleclick* tabs.middleclick* tabs.middlebar*"),
        (Tabs, "Dragging between windows", "tabs.detach* tabs.merge*"),

        (Files, "Opening and saving", "general.openfromexplorer general.saveformat"),
        (Files, "Backups and recovery", "general.autosave"),
        (Files, "Windows integration", "general.associate*"),
        (Files, "Linked audio", "audio.linkedmedia*"),
    };

    private sealed record Place(string Page, string Group, bool More, int Order);

    private static readonly Dictionary<string, Place> Places = BuildPlaces();

    private static Dictionary<string, Place> BuildPlaces()
    {
        var places = new Dictionary<string, Place>(StringComparer.OrdinalIgnoreCase);
        var order = 0;
        foreach (var (page, group, keys) in Layout)
            foreach (var entry in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var more = entry.EndsWith('*');
                places[more ? entry[..^1] : entry] = new Place(page, group, more, order++);
            }
        return places;
    }

    /// <summary>Where a row lives now (page, group, level); null when the key is not in the layout table.</summary>
    internal static (string Page, string Group, bool More)? PlaceOf(string key) =>
        Places.TryGetValue(key, out var place) ? (place.Page, place.Group, place.More) : null;

    /// <summary>Menu wording, old titles and old page names that still find a row in search (the audit's "old names" rule).</summary>
    private static readonly Dictionary<string, string> OldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio.fretboardstyle"] = "preview layout", ["editing.horizon.enabled"] = "preview next notes", ["editing.horizon"] = "preview length",
        ["fretboard.numbersize"] = "number size", ["appearance.paper"] = "dark page light page", ["follow.mode"] = "page turn smooth page turn follow the playhead",
        ["timeline.barglow"] = "subtle bar glow", ["timeline.individualnotes"] = "timeline display individual notes",
        ["timeline.continuousline"] = "timeline display continuous line", ["timeline.hideemptygrid"] = "timeline display hide grid",
        ["appearance.density"] = "density", ["score.defaultnotation"] = "show standard notation default score display",
        ["score.pagelayout"] = "page continuous", ["score.scrolling"] = "vertical horizontal", ["score.textfonts"] = "text fonts",
        ["fretboard.showallas"] = "show all tracks as", ["fretboard.instrumentview"] = "show this track as",
        ["vst.driver"] = "audio vst", ["vst.folders"] = "audio vst", ["general.confirmclose"] = "stop asking are you sure",
    };

    private static readonly Dictionary<string, string> PageWords = new(StringComparer.OrdinalIgnoreCase)
    {
        [AudioVst] = "audio vst plugins", [Hotkeys] = "hotkeys", [Appearance] = "colours colors look", [Playback] = "playback sound",
        [Score] = "notation", [Timeline] = "sections arrangement", [Tabs] = "windows",
    };

    public static List<SettingDescriptor> Build(AppSettings s)
    {
        var tabs = s.Tabs ??= new TabSettings();
        var g = s.General ??= new GeneralSettings();
        var a = s.Appearance ??= new AppearanceSettings();
        var au = s.Audio ??= new AudioSettings();
        var ed = s.Editing ??= new EditingSettings();
        var pl = s.Plugins ??= new PluginSettings();
        var fv = s.Follow ??= new FollowSettings();
        var timeline = s.Timeline ??= new TimelineSettings();
        var list = new List<SettingDescriptor>
        {
            // General
            Bool(General, "Updates", "general.checkupdates", "Check for updates automatically", v => g.CheckForUpdates = v, () => g.CheckForUpdates,
                "Once a day, ask GitHub whether a newer TabForge release exists and offer to open its download page. One anonymous HTTPS request; nothing is downloaded or installed automatically. Help > Check for updates works either way.",
                "update updates new version release github check automatic notify beta download"),
            Choice(General, "Files", "general.openfromexplorer", "Open songs from Explorer in", v => g.OpenFromExplorer = v, () => g.OpenFromExplorer,
                new[] { "A new tab", "A new window" },
                "When TabForge is already running, a song you double-click in Explorer opens as a new tab in that window (default), or in a separate TabForge window.",
                "open explorer double click file new tab window instance single"),
            Choice(General, "Files", "general.autosave", "Autosave unsaved songs", v => g.AutosaveMinutes = AutosaveChoices.ToMinutes(v),
                () => AutosaveChoices.ToLabel(g.AutosaveMinutes), AutosaveChoices.Labels,
                "Copies songs with unsaved changes into TabForge's Recovery folder at this interval (skipped while playing). Your own files are never overwritten; the copies are offered the next time TabForge starts after a crash, and removed when you save or close.",
                "autosave auto save backup recovery crash power loss interval minutes"),
            Choice(General, "Files", "general.saveformat", "Default save format", v => g.DefaultSaveFormat = v == "TabForge project (.tforge)" ? "tforge" : "gp",
                () => g.DefaultSaveFormat == "tforge" ? "TabForge project (.tforge)" : "Guitar Pro 7/8 (.gp)",
                new[] { "Guitar Pro 7/8 (.gp)", "TabForge project (.tforge)" },
                ".gp opens in Guitar Pro 7/8 and keeps every TabForge feature (stored inside the file). .tforge is TabForge-only.",
                "save format extension gp gp7 gp8 guitar pro tforge default"),
            Bool(General, "Windows integration", "general.associate", "Open Guitar Pro and TabForge files with TabForge", v => g.AssociateFiles = v, () => g.AssociateFiles,
                "Adds TabForge to Explorer's Open with for .gp, .gp5, .gp4, .gp3, .gpx and .tforge (for your Windows account only). Turning it off removes TabForge's entries again.",
                "file association windows integration explorer open with gp gp5 gpx tforge double click"),
            Bool(General, "Application", "general.toolbar", "Show the toolbar", v => g.ShowToolbar = v, () => g.ShowToolbar,
                "Show the transport and editing toolbar.", "toolbar buttons transport bar"),
            Bool(General, "Application", "general.statusbar", "Show the status bar", v => g.ShowStatusBar = v, () => g.ShowStatusBar,
                "Show cursor position and status messages at the bottom of the window.", "status bar footer messages"),
            Bool(General, "Safety", "general.confirmclose", "Confirm before closing unsaved work", v => g.ConfirmOnClose = v, () => g.ConfirmOnClose,
                "Ask before discarding unsaved changes when closing a tab or window.", "confirm prompt unsaved dirty"),
            Bool(General, "Safety", "general.confirmdiscardsettings", "Warn before discarding unapplied changes", v => g.ConfirmDiscardSettingsChanges = v, () => g.ConfirmDiscardSettingsChanges,
                "Ask before closing Settings, Track properties or Project settings without applying edits. Turned off by \"Don't ask me again\".",
                "preferences settings cancel discard track project dont ask again remember warning"),

            // Appearance
            Choice(Appearance, "Interface", "appearance.thememode", "Theme", v => ThemeService.ApplyPreset(a, v), () => a.ThemeMode,
                new[] { "System", "Dark", "Light", "Custom" }, "Theme preset: Dark, Light or System sets the interface, accent and score page colours at once; any colour can still be changed afterwards. Custom keeps your colours as they are.", "theme system dark light custom"),
            Number(Appearance, "Interface", "appearance.uiscale", "UI scale", v => a.UiScale = v, () => a.UiScale, 0.8, 1.5,
                "Scale interface text and icon controls.", "zoom interface size scale dpi", "x", 0.05, 2),
            Int(Appearance, "Interface", "appearance.tracktint", "Track colour tint", v => a.TrackTintPercent = v, () => a.TrackTintPercent, 0, 60,
                "How strongly each track's row and timeline lane take the track's colour (0 = off). Subtle and darker in the dark theme. Tracks can turn it off in Track properties.",
                "track colour color tint background row lane intensity transparency", unit: "%"),
            Choice(Appearance, "Interface", "appearance.density", "Spacing", v => a.Density = v, () => a.Density,
                new[] { "Compact", "Comfortable", "Spacious" }, "How tightly the toolbar and buttons are packed.", "compact comfortable spacious density"),
            Bool(Appearance, "Motion", "appearance.reduceanimations", "Reduce animations", v => a.ReduceAnimations = v, () => a.ReduceAnimations,
                "Disable short tab and timeline drag transitions.", "motion accessibility reduce animation"),
            Number(Appearance, "Motion", "appearance.animationspeed", "Animation speed", v => a.AnimationSpeed = v, () => a.AnimationSpeed, 0.25, 2,
                "Scale the duration of tab and timeline drag animations.", "motion animation faster slower duration", "x", 0.05, 2),
            Colour(Appearance, "Interface colours", "appearance.accent", "Accent colour", v => a.Accent = v, () => a.Accent,
                "Focus, active controls and the primary selection colour.", "theme accent selection focus"),
            Colour(Appearance, "Interface colours", "appearance.selection", "Selection colour", v => a.SelectionColour = v, () => a.SelectionColour,
                "Colour used for selected score ranges.", "score selected range highlight"),
            Colour(Appearance, "Interface colours", "appearance.hover", "Hover colour", v => a.HoverColour = v, () => a.HoverColour,
                "Base colour for score hover feedback.", "score hover pointer"),
            Colour(Appearance, "Custom palette", "appearance.background", "Window background", v => a.Background = v, () => a.Background,
                "Background behind panels in Custom theme.", "theme background dark", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.panel", "Panel background", v => a.Panel = v, () => a.Panel,
                "Toolbar and side-panel background in Custom theme.", "theme panel chrome", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.titlebar", "Title bar background", v => a.TitleBarColour = v, () => a.TitleBarColour,
                "Document title-bar background in Custom theme.", "title bar chrome background", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.tabactive", "Selected tab background", v => a.ActiveTabColour = v, () => a.ActiveTabColour,
                "Background of the active document tab.", "tab active selected background", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.tabhover", "Tab hover background", v => a.TabHoverColour = v, () => a.TabHoverColour,
                "Background when the pointer is over a document tab.", "tab hover background", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.text", "Text colour", v => a.Text = v, () => a.Text,
                "Primary interface text in Custom theme.", "theme foreground font colour", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.muted", "Muted text colour", v => a.Muted = v, () => a.Muted,
                "Secondary labels and hints in Custom theme.", "theme secondary grey", "appearance.thememode", "Custom"),
            Colour(Appearance, "Custom palette", "appearance.timelinescrollbar", "Timeline scrollbar thumb", v => a.TimelineScrollBarThumbColour = v, () => a.TimelineScrollBarThumbColour,
                "Colour of the arrangement timeline scrollbar thumb; the hover colour is derived from this value.", "timeline scrollbar scroll thumb navigation bar"),
            Text(Appearance, "Typography", "appearance.font", "Interface font", v => a.FontFamily = v, () => a.FontFamily,
                "Font family used by the interface.", "font family typeface"),
            Number(Appearance, "Typography", "appearance.fontsize", "Interface font size", v => a.FontSize = v, () => a.FontSize, 8, 24,
                "Base text size before UI scale is applied.", "font size text scale", "pt", 1, 0),
            Number(Appearance, "Typography", "appearance.iconsize", "Icon size", v => a.IconSize = v, () => a.IconSize, 10, 22,
                "Base size of toolbar and title-bar icons.", "icon glyph toolbar buttons", "px", 1, 0),
            Bool(Appearance, "Typography", "appearance.toolbaricons", "Show icons on the tool palette", v => a.ShowToolbarIcons = v, () => a.ShowToolbarIcons,
                "Use glyphs instead of text-only toolbar buttons.", "icons glyphs toolbar"),

            // Score & notation
            Choice(Score, "Notation", "score.defaultnotation", "Default score display", v => { s.Notation = v; s.NotationPreferenceSet = true; },
                () => s.NotationPreferenceSet ? s.Notation ?? "TabAndStaff" : "TabAndStaff",
                new[] { "TabAndStaff", "TabOnly", "StaffOnly" }, "Default display for new scores: notation + TAB, TAB only, or notation only.", "notation tablature tab staff default display"),
            Text(Score, "Notation", "appearance.scorefont", "Score font family", v => a.ScoreFontFamily = v, () => a.ScoreFontFamily,
                "Typeface for score text, fret numbers and annotations.", "score notation typeface"),
            Number(Score, "Notation", "appearance.scorefontsize", "Score text size", v => a.ScoreTextSize = v, () => a.ScoreTextSize, 8, 24,
                "Size of text rendered on the score page.", "score notation text size fret number", "px", 0.5, 1),
            Bool(Score, "Notation", "appearance.scorebold", "Bold score text", v => a.ScoreTextBold = v, () => a.ScoreTextBold,
                "Render score text in bold.", "score notation weight"),
            Bool(Score, "Notation", "appearance.scoreitalic", "Italic score text", v => a.ScoreTextItalic = v, () => a.ScoreTextItalic,
                "Render score text in italic.", "score notation style"),
            Number(Score, "Layout", "appearance.spacing", "Tablature spacing", v => a.ScoreSpacing = v, () => a.ScoreSpacing, 0.85, 1.6,
                "Scale tablature string spacing and fret-number size.", "tablature fret size string gap readability", "x", 0.05, 2),
            Number(Score, "Layout", "score.systemspacing", "System vertical spacing", v => a.SystemVerticalSpacing = v, () => a.SystemVerticalSpacing, 0.7, 1.6,
                "Scale the vertical gap between standard notation and TAB systems.", "staff system vertical gap spacing", "x", 0.05, 2),
            Number(Score, "Layout", "score.measurespacing", "Measure horizontal spacing", v => a.MeasureHorizontalSpacing = v, () => a.MeasureHorizontalSpacing, 0.8, 1.6,
                "Scale engraved measure widths while preserving note and annotation clearance.", "bar measure horizontal width spacing", "x", 0.05, 2),
            Choice(Score, "Layout", "score.ledger", "Ledger lines", v => a.LedgerLines = v, () => a.LedgerLines,
                new[] { "Standard", "Minimal", "Hidden" }, "Choose the notation ledger-line style.", "staff ledger lines minimal standard hidden"),
            Number(Score, "Layout", "score.staffopacity", "Staff and ledger line opacity", v => a.StaffLineOpacity = v / 100, () => a.StaffLineOpacity * 100, 0, 100,
                "Opacity of the staff, TAB and ledger lines. Staff lines and ledger lines always share one colour (the dark / light mode staff-line colours) and one opacity.", "staff tab string ledger line alpha percent opacity", "%", 1, 0),
            Bool(Score, "Labels", "score.barnumbers", "Show bar numbers", v => a.ShowScoreBarNumbers = v, () => a.ShowScoreBarNumbers,
                "Show measure numbers on the score page.", "measure number bar label"),
            Int(Score, "Labels", "score.barnumberfrequency", "Bar-number frequency", v => a.ScoreBarNumberFrequency = v, () => a.ScoreBarNumberFrequency, 1, 16,
                "Draw every Nth bar number; 1 shows every measure.", "bar numbers every frequency interval"),
            Bool(Score, "Labels", "score.sectionheadings", "Show section headings", v => a.ShowSectionHeadings = v, () => a.ShowSectionHeadings,
                "Show marker names above the score.", "section marker title heading label"),
            Bool(Score, "Labels", "score.dynamics", "Show dynamics", v => a.ShowDynamics = v, () => a.ShowDynamics,
                "Engrave dynamics markings (ppp to fff) under the staff where the dynamic changes.", "dynamics markings forte piano ppp mf fff loudness"),
            Number(Score, "Highlighting", "score.hoverintensity", "Hover-highlight intensity", v => a.HoverHighlightIntensity = v / 100, () => a.HoverHighlightIntensity * 100, 0, 100,
                "Strength of the score hover outline.", "hover pointer highlight opacity percent", "%", 1, 0),
            Number(Score, "Highlighting", "score.selectionintensity", "Selection-highlight intensity", v => a.SelectionHighlightIntensity = v / 100, () => a.SelectionHighlightIntensity * 100, 0, 100,
                "Opacity of the selected score range.", "selected range highlight opacity percent", "%", 1, 0),
            Choice(Score, "Score colours", "appearance.paper", "Score paper", v => a.ScorePaper = v, () => a.ScorePaper,
                new[] { "Dark", "Light" }, "Choose dark or light score paper.", "page paper background print"),
            Colour(Score, "Score colours", "appearance.scorepaper.dark", "Dark score page", v => a.DarkScorePaperColour = v, () => a.DarkScorePaperColour,
                "Background of the dark score page.", "score paper dark background"),
            Colour(Score, "Score colours", "appearance.scorepaper.light", "Light score page", v => a.LightScorePaperColour = v, () => a.LightScorePaperColour,
                "Background of the light score page.", "score paper light background"),
            Colour(Score, "Score colours", "appearance.scoreink.dark", "Dark-mode notation", v => a.DarkScoreInkColour = v, () => a.DarkScoreInkColour,
                "Staff, TAB and title text on dark score paper.", "score notation ink dark"),
            Colour(Score, "Score colours", "appearance.scoreink.light", "Light-mode notation", v => a.LightScoreInkColour = v, () => a.LightScoreInkColour,
                "Staff, TAB and title text on light score paper.", "score notation ink light"),
            Colour(Score, "Score colours", "appearance.scorelines.dark", "Dark-mode staff lines", v => a.DarkScoreLinesColour = v, () => a.DarkScoreLinesColour,
                "Staff and TAB line colour on dark score paper.", "score staff line dark"),
            Colour(Score, "Score colours", "appearance.scorelines.light", "Light-mode staff lines", v => a.LightScoreLinesColour = v, () => a.LightScoreLinesColour,
                "Staff and TAB line colour on light score paper.", "score staff line light"),
            Colour(Score, "Score colours", "appearance.cursor", "Edit cursor colour", v => a.CursorColour = v, () => a.CursorColour,
                "Colour of the edit caret when playback is stopped.", "edit cursor caret"),
            // Page layout: the score right-click menu's Page / Continuous and Vertical / Horizontal choices (saved as the layout songs open with).
            Choice(Score, "Page layout", "score.pagelayout", "Score page layout", v => s.PreferredContinuousScoreView = v == "Continuous",
                () => s.PreferredContinuousScoreView ? "Continuous" : "Page", new[] { "Page", "Continuous" },
                "Page: the score is drawn as paper pages. Continuous: one seamless sheet. Used for every song you open; right-click the score to change the open song.",
                "page paper continuous seamless score layout view right click"),
            Choice(Score, "Page layout", "score.scrolling", "Score scrolling", v => s.PreferredHorizontalScoreView = v == "Horizontal",
                () => s.PreferredHorizontalScoreView ? "Horizontal" : "Vertical", new[] { "Vertical", "Horizontal" },
                "Vertical: lines of music wrap and you scroll down. Horizontal: one long line you scroll to the right. Used for every song you open; right-click the score to change the open song.",
                "vertical horizontal scroll wrap line score layout direction"),
            Button(Score, "Notation", "score.textfonts", "Text & fonts for each part of the score",
                "Choose the font, size and style of each kind of score text (title and header, chord names, lyrics, fret numbers, techniques, bar information) separately.",
                "score text fonts per area title lyrics chord names fret numbers style"),

            // Playback
            Choice(Playback, "Follow", "follow.mode", "Scroll the score while playing", v => fv.Mode = v, () => fv.Mode,
                new[] { FollowModes.Off, FollowModes.Jump, FollowModes.Smooth }, "Off, jump a line at a time, or scroll smoothly.", "follow auto scroll mode playhead"),
            Bool(Playback, "Follow", "follow.horizontal", "Horizontal follow", v => fv.HorizontalFollow = v, () => fv.HorizontalFollow,
                "Scroll horizontally as playback reaches the end of the visible system.", "follow sideways x axis", dependsOn: "follow.mode", dependsOnValue: "Jump|Smooth"),
            Bool(Playback, "Follow", "follow.vertical", "Vertical follow", v => fv.VerticalFollow = v, () => fv.VerticalFollow,
                "Scroll vertically when the active system reaches its trigger position.", "follow vertical y axis", dependsOn: "follow.mode", dependsOnValue: "Jump|Smooth"),
            Int(Playback, "Follow", "follow.anticipation", "Look-ahead bars", v => fv.AnticipationBars = v, () => fv.AnticipationBars, 0, 4,
                "Keep this many bars visible ahead of the playhead before horizontal scrolling begins.", "follow horizontal anticipation lookahead ahead bars", "bars", dependsOn: "follow.horizontal"),
            Int(Playback, "Follow", "follow.verticaltrigger", "Vertical trigger position", v => fv.VerticalTriggerPercent = v, () => fv.VerticalTriggerPercent, 40, 95,
                "Start following when the active system reaches this viewport position.", "follow vertical trigger lookahead percent", "%", dependsOn: "follow.vertical"),
            Int(Playback, "Follow", "follow.margin", "Playhead position in the view", v => fv.MarginPercent = v, () => fv.MarginPercent, 0, 60,
                "Preferred vertical position of the active system from the top of the viewport.", "follow target viewport top margin", "%", dependsOn: "follow.vertical"),
            Bool(Playback, "Follow", "follow.stopmanual", "Pause following when I scroll by hand", v => fv.StopOnManualScroll = v, () => fv.StopOnManualScroll,
                "Stop automatic movement while you scroll by hand.", "manual scroll pause"),
            Bool(Playback, "Follow", "follow.stopatend", "Stop scrolling once the end is visible", v => fv.StopAtEnd = v, () => fv.StopAtEnd,
                "Avoid scrolling once the last system or loop end is already visible.", "last end visible"),
            Int(Playback, "Follow", "follow.fps", "Smooth scrolling frame rate", v => fv.MaxFps = v, () => fv.MaxFps, 10, 240,
                "Refresh ceiling for Smooth mode.", "fps frame rate animation", "fps"),
            Bool(Playback, "Highlighting", "follow.highlight", "Tint the playing beat", v => fv.HighlightPlayedBeat = v, () => fv.HighlightPlayedBeat,
                "Tint the currently sounding beat.", "playing beat note highlight tint"),
            Colour(Playback, "Highlighting", "follow.colour", "Playing note colour", v => fv.HighlightColour = v, () => fv.HighlightColour,
                "Colour of sounding note heads and fret numbers.", "note playback green"),
            Colour(Playback, "Highlighting", "follow.bg", "Playing beat background", v => fv.HighlightBackground = v, () => fv.HighlightBackground,
                "Background tint behind the elapsed part of the sounding beat.", "beat tint background"),
            Bool(Playback, "Highlighting", "follow.duration.enabled", "Duration tint", v => fv.DurationTintEnabled = v, () => fv.DurationTintEnabled,
                "Show the tail that marks the remaining duration of active notes; beat tint is controlled separately.", "duration shading tint tail"),
            Colour(Playback, "Highlighting", "follow.durationglow", "Duration glow colour", v => fv.DurationGlowColour = v, () => fv.DurationGlowColour,
                "Colour of the translucent active-note duration region.", "duration glow active note colour", dependsOn: "follow.duration.enabled"),
            Number(Playback, "Highlighting", "follow.durationopacity", "Duration glow intensity", v => fv.DurationGlowOpacity = v / 100, () => fv.DurationGlowOpacity * 100, 0, 100,
                "Scales playback shading opacity. 0% disables the duration and beat fills; the playback line and note-position indicator remain.", "duration glow intensity opacity", "%", 1, 0, "follow.duration.enabled"),
            Colour(Playback, "Highlighting", "follow.playhead", "Playback line colour", v => fv.PlayheadColour = v, () => fv.PlayheadColour,
                "Colour of the vertical playback line in the score (the timeline keeps its white line).", "playhead line cursor"),
            Number(Playback, "Highlighting", "follow.playhead.thickness", "Playback line thickness", v => fv.PlayheadThickness = v, () => fv.PlayheadThickness, 0.5, 5,
                "Stroke width of the vertical playback line in the score.", "playhead line width thickness", "px", 0.1, 1),

            // Audio
            Choice(AudioVst, "Audio output", "vst.driver", "Audio driver", v => { if (pl.Driver != v) { pl.Driver = v; pl.Device = ""; } }, () => pl.Driver, AudioDrivers.All,
                "How plug-in audio reaches your speakers. WASAPI (shared) works everywhere; exclusive and ASIO give the lowest latency. ASIO also carries the recording input. DirectSound is the legacy fallback (larger buffer, more latency); WASAPI or ASIO is recommended. Tracks on Windows MIDI are not affected.",
                "audio driver wasapi asio directsound exclusive shared latency output"),
            Choice(AudioVst, "Audio output", "vst.device", "Output device", v => pl.Device = v == DefaultAudioDevice ? "" : v,
                () => pl.Device.Length == 0 ? DefaultAudioDevice : pl.Device, AudioDeviceChoices(pl),
                "The device plug-in audio plays on (for ASIO: the ASIO driver; use Configure… for its buffer size and routing). Windows default follows the device chosen in Windows sound settings. The list follows the driver above.",
                "audio output device speakers interface asio driver name headphones"),
            Choice(AudioVst, "Audio output", "vst.asio.out", "ASIO output: first channel", v => pl.AsioOutputChannel = Math.Max(0, AsioOutputs(pl).IndexOf(v)),
                () => AsioOutputs(pl)[Math.Clamp(pl.AsioOutputChannel, 0, AsioOutputs(pl).Count - 1)], AsioOutputs(pl).ToArray(),
                "The first ASIO output channel (the output pair starts here). Names come from the driver.", "asio output channels range outputs first",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Choice(AudioVst, "Audio output", "vst.asio.outlast", "ASIO output: last channel", v => pl.AsioOutputLastChannel = Math.Max(0, AsioOutputs(pl).IndexOf(v)),
                () => AsioOutputs(pl)[Math.Clamp(pl.AsioOutputLastChannel, 0, AsioOutputs(pl).Count - 1)], AsioOutputs(pl).ToArray(),
                "The last ASIO output channel: the next one after the first for stereo, or the same as the first for one mono output (the mix is summed to it).", "asio output channels range outputs last mono",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Bool(AudioVst, "Audio output", "vst.playall", "Play the whole song through the audio engine", v => pl.PlayAllThroughEngine = v, () => pl.PlayAllThroughEngine,
                "Off: tracks without plug-ins play on Windows MIDI, which never uses the audio driver above (so ASIO does not apply to them, and the Windows volume controls them). On: every track is played by TabForge's own General MIDI synth through the chosen driver (ASIO included). Uses a little more memory and CPU. Track volume, pan, mute, solo and the master volume apply to plug-in tracks either way.",
                "play all engine asio whole song general midi synth windows midi driver route"),
            Bool(AudioVst, "Audio output", "vst.autogm", "Auto-switch to GM sound when no VST instrument plays", v => pl.AutoGmSound = v, () => pl.AutoGmSound,
                "On: when a track's VST instrument stops playing it (chain switched off, instrument bypassed or removed) its GM sound is ticked, and unticked again when the instrument plays. A GM sound you untick yourself stays off. Same option as in the FX window.",
                "auto gm general midi sound switch instrument bypass chain off fallback"),
            Bool(AudioVst, "Audio output", "vst.autopitch", "Match VST instrument pitch automatically", v => pl.AutoPitchMatch = v, () => pl.AutoPitchMatch,
                "On (default): when a VST instrument loads or its preset changes, TabForge measures silently which octave it sounds at and transposes it to match the notes (e.g. a bass preset two octaves low gets +24). Each FX window can override this per chain. Drum tracks are never transposed.",
                "auto pitch match octave transpose instrument preset tuning measure"),
            Text(AudioVst, "Startup tracks", "vst.startuptracks", "Startup tracks", v =>
                {
                    var keep = v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    pl.StartupTracks.RemoveAll(t => !keep.Contains(t.Name));
                },
                () => string.Join("; ", pl.StartupTracks.Select(t => t.Name)),
                "Chains ticked with 'Add as a track on startup' in the FX window; each is added (not armed) to every song you open or create. Delete a name from the list (separated by ';') to remove it. Added tracks are not saved with the song until you untick the option.",
                "startup track template live guitar amp sim chain add every song remove"),
            Int(AudioVst, "Audio output", "vst.winmidilatency", "Windows MIDI latency (ms)", v => pl.WindowsMidiLatencyMs = v, () => pl.WindowsMidiLatencyMs, 0, 600,
                "Output latency of the Windows MIDI synth (about 200 ms measured on a typical PC). Raise it if plug-in tracks such as drums sound ahead of the others, lower it if they lag. Measure plays one very quiet hit through the Windows synth and reads it back (only when nothing else plays).",
                "windows midi latency delay offset sync early late plugin vst ahead lag microsoft gs synth", unit: "ms"),
            Bool(AudioVst, "Audio output", "vst.followvolume", "Follow the Windows volume", v => pl.FollowWindowsVolume = v, () => pl.FollowWindowsVolume,
                "ASIO bypasses the Windows mixer, so the Windows volume keys and slider do nothing to it. On: TabForge scales its audio by the Windows master volume and mute (a software level; the interface's own knob is not touched). On by default.",
                "asio windows volume master follow keys slider mute interface knob",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Bool(AudioVst, "Audio output", "vst.renderlimiter", "Safety limiter on rendered audio", v => s.Render.SafetyLimiter = v, () => s.Render.SafetyLimiter,
                "File > Render: a transparent limiter on the master mix (ceiling -0.3 dBFS, 1.5 ms lookahead, about 80 ms release) catches peaks that would otherwise clip the file. Material already below the ceiling is unchanged. Stems are never limited. On by default.",
                "render export limiter safety clip clipping peak ceiling master wav mp3 loud"),
            Bool(AudioVst, "Audio output", "vst.livelimiter", "Safety limiter on live playback", v => pl.LiveLimiter = v, () => pl.LiveLimiter,
                "The same limiter on the live output of the audio engine, after the master chain and Monitor FX. Off by default: it adds about 1.5 ms of delay and normal playback should not need it.",
                "live playback limiter safety clip clipping peak ceiling output engine loud"),
            Bool(AudioVst, "Audio input", "vst.asio.inputs", "Enable ASIO inputs", v => pl.AsioInputsEnabled = v, () => pl.AsioInputsEnabled,
                "Use the ASIO driver's inputs for recording and monitoring. Off: no input through ASIO.", "asio enable inputs recording monitor",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Choice(AudioVst, "Audio input", "vst.asio.infirst", "ASIO input: first channel", v => pl.AsioInputChannel = Math.Max(0, AsioInputs(pl).IndexOf(v)),
                () => AsioInputs(pl)[Math.Clamp(pl.AsioInputChannel, 0, AsioInputs(pl).Count - 1)], AsioInputs(pl).ToArray(),
                "The first ASIO input channel recorded. Pick the same channel for first and last for one mono input (a guitar plugged into input 2, say).", "asio input channels range inputs first",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Choice(AudioVst, "Audio input", "vst.asio.inlast", "ASIO input: last channel", v => pl.AsioInputLastChannel = Math.Max(0, AsioInputs(pl).IndexOf(v)),
                () => AsioInputs(pl)[Math.Clamp(pl.AsioInputLastChannel, 0, AsioInputs(pl).Count - 1)], AsioInputs(pl).ToArray(),
                "The last ASIO input channel: the same as the first for mono, or the next one for a stereo pair (at most two channels).", "asio input channels range inputs last",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Int(AudioVst, "Audio input", "vst.recordoffset", "Recording offset (ms)", v => pl.RecordingOffsetMs = v, () => pl.RecordingOffsetMs, -1000, 1000,
                "Shifts new recordings on the timeline, on top of the input latency the device reports. If takes sound late against the song, enter a positive value (they move earlier); if early, a negative one. 0 by default.",
                "recording offset latency compensation input late early align takes record manual offset", unit: "ms"),
            Choice(AudioVst, "Audio input", "vst.input", "Recording device", v => pl.InputDevice = v == DefaultAudioDevice ? "" : v,
                () => pl.InputDevice.Length == 0 ? DefaultAudioDevice : pl.InputDevice, AudioInputChoices(pl),
                "The device armed tracks record from (WASAPI, DirectSound). With ASIO the recording comes through the ASIO driver instead.",
                "audio input recording device microphone interface capture"),
            Choice(AudioVst, "Audio output", "vst.samplerate", "Sample rate", v => pl.SampleRate = int.Parse(v), () => pl.SampleRate.ToString(),
                AudioDrivers.SampleRates.Select(r => r.ToString()).ToArray(), "Plug-in processing sample rate in Hz.", "sample rate hz 44100 48000 96000"),
            Choice(AudioVst, "Audio output", "vst.buffer", "Buffer size", v => pl.BufferSize = Math.Clamp(int.Parse(v), AudioDrivers.MinBuffer, AudioDrivers.MaxBuffer), () => pl.BufferSize.ToString(),
                AudioDrivers.BufferSizes.Select(r => r.ToString()).ToArray(),
                "Samples per block (type any value from 16 to 8192, or pick one). Smaller is lower latency but uses more CPU; raise it if you hear crackles. ASIO: this size is requested from the driver (kept inside what it allows); the status bar shows the size the driver really uses.", "buffer size latency samples crackle"),
            Button(AudioVst, "Linked audio", "audio.linkedmedia", "Linked audio from network or removable drives",
                "A song can link audio files on a network location or a removable drive. They are read only from folders you approved. Review the approved folders here and revoke any you no longer want.",
                "network removable linked audio approve revoke folders drive usb unc share"),
            Text(AudioVst, "Plug-ins", "vst.folders", "Plug-in folders", v => pl.Folders = v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                () => string.Join("; ", pl.Folders),
                "Folders that hold your VST2 (.dll) and VST3 (.vst3) plug-ins, separated by ';'. Use Browse to add one. Only these folders are scanned unless the option below is on.",
                "vst plugin folder directory path browse scan vst2 vst3"),
            Text(AudioVst, "Plug-ins", "vst.commonfolders", "Common plug-in folders (Scan common folders)", v => pl.CommonFolders = v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                () => string.Join("; ", pl.CommonFolders.Count > 0 ? pl.CommonFolders : TabForge.Plugins.VstScannerService.DefaultCommonFolders),
                "The usual VST locations scanned by the Add plug-in window's Scan common folders button, separated by ';'. %ENV% variables work; missing folders are skipped. Clear the box to go back to the built-in list (the Add plug-in window's Edit list button also has Reset to defaults).",
                "vst plugin common folders standard locations scan reset defaults"),
            Bool(AudioVst, "Plug-ins", "vst.rememberscan", "Remember the plug-in list", v => pl.RememberScan = v, () => pl.RememberScan,
                "Off (default): the folders are scanned each time you add a plug-in. On: they are scanned once and the list is remembered; use Rescan in the Add plug-in window after installing new plug-ins.",
                "remember scan cache permanent folders once list plugins"),
            Bool(AudioVst, "Plug-ins", "vst.dock", "Show plug-in windows inside the FX chain window", v => pl.DockPluginWindows = v, () => pl.DockPluginWindows,
                "On (default): the selected plug-in's own controls appear in the FX chain window. Double-click a plug-in to float its window. Off: plug-in windows always float.",
                "dock plugin window embedded ui float fx chain"),
            Bool(AudioVst, "Plug-ins", "vst.ontop", "Keep floating plug-in windows on top", v => pl.PluginWindowsOnTop = v, () => pl.PluginWindowsOnTop,
                "Floating plug-in windows stay above other windows.", "plugin window always on top float"),
            Bool(AudioVst, "Plug-ins", "vst.scanstandard", "Also scan the standard VST folders", v => pl.ScanStandardFolders = v, () => pl.ScanStandardFolders,
                "Off (default): only the folders you added are scanned. On: the usual Common Files VST3 and VstPlugins folders too.", "scan standard default folders common files vst3"),
            Button(AudioVst, "Plug-ins", "vst.quarantine", "Plug-ins switched off after a crash",
                "A plug-in that crashed is switched off so it cannot crash the next playback too. Review the list here and allow a plug-in again; it is then loaded on the next playback (the same button is in its FX chain window). Allowing again does not approve an untrusted file.",
                "quarantine crashed crash switched off plugin allow again blocked faulted"),
            Bool(AudioVst, "Safety", "vst.isolate", "Run each plug-in in its own process", v => pl.SeparateProcessPerPlugin = v, () => pl.SeparateProcessPerPlugin,
                "Off (default): all plug-ins share one audio engine process, separate from TabForge, so a crash never closes TabForge. On: each plug-in gets its own process, so a crash stops only that plug-in (uses more CPU and memory). This contains crashes only; it is not a security sandbox, so load only plug-ins you trust.",
                "isolate sandbox crash process bridge separate safe plugin"),
            Bool(Audio, "Transport", "audio.metronome", "Metronome", v => au.Metronome = v, () => au.Metronome,
                "Play clicks during playback.", "click tempo count"),
            Bool(Audio, "Transport", "audio.countin", "Count-in before playback", v => au.CountIn = v, () => au.CountIn,
                "Play a count-in before the score starts.", "count in lead"),
            Int(Audio, "Transport", "audio.countinbars", "Count-in bars", v => au.CountInBars = v, () => au.CountInBars, 1, 4,
                "Number of count-in bars.", "count in bars length", "bars", dependsOn: "audio.countin"),
            Number(Audio, "Transport", "audio.speed", "Playback speed", v => au.Speed = v, () => au.Speed, 0.25, 2,
                "Practice at a fraction of the written tempo.", "speed trainer slow down tempo", "x", 0.05, 2),
            Bool(Audio, "Note preview", "audio.preview", "Preview notes", v => au.PreviewNotes = v, () => au.PreviewNotes,
                "Audition notes when clicking or entering them.", "preview audition click"),
            Int(Audio, "Note preview", "audio.previewlen", "Preview length", v => au.PreviewLengthMs = v, () => au.PreviewLengthMs, 60, 1200,
                "Duration of a previewed note.", "preview duration ms", "ms", dependsOn: "audio.preview"),
            Int(Audio, "Note preview", "audio.letring", "Let-ring tail limit", v => au.LetRingCapMs = v, () => au.LetRingCapMs, 400, 6000,
                "Maximum sustain for let-ring notes.", "let ring sustain drone cap", "ms"),
            Int(Audio, "Metronome", "audio.metroclick", "Metronome click note", v => au.MetronomeClick = v, () => au.MetronomeClick, 0, 127,
                "General MIDI percussion note used for regular clicks.", "gm percussion midi", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metroaccent", "Metronome accent note", v => au.MetronomeAccent = v, () => au.MetronomeAccent, 0, 127,
                "General MIDI percussion note used for the first-beat accent.", "gm percussion downbeat", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metrovolume", "Metronome volume", v => au.MetronomeVolume = v, () => au.MetronomeVolume, 0, 100,
                "Master metronome level.", "click master percent", "%", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metroaccentvolume", "First-beat volume", v => au.MetronomeAccentVolume = v, () => au.MetronomeAccentVolume, 0, 100,
                "Level of the accented first beat.", "downbeat accent percent", "%", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metroclickvolume", "Regular-click volume", v => au.MetronomeClickVolume = v, () => au.MetronomeClickVolume, 0, 100,
                "Level of regular metronome beats.", "regular click percent", "%", dependsOn: "audio.metronome"),
            Choice(Audio, "Metronome", "audio.metrodivision", "Metronome subdivisions", v => au.MetronomeSubdivision = int.Parse(v), () => au.MetronomeSubdivision.ToString(),
                new[] { "1", "2", "3", "4" }, "Clicks per beat: quarter, eighth, triplet or sixteenth.", "beat subdivision triplet", dependsOn: "audio.metronome"),

            Choice(Timeline, "Track controls", "timeline.volumestyle", "Track volume control", v => au.VolumeKnobs = v == "Knob", () => au.VolumeKnobs ? "Knob" : "Slider",
                new[] { "Slider", "Knob" }, "Slider with the value on its handle, or a rotary knob.", "volume slider knob circle mixer"),
            Choice(Timeline, "Track controls", "timeline.panstyle", "Track pan control", v => au.PanKnobs = v == "Knob", () => au.PanKnobs ? "Knob" : "Slider",
                new[] { "Slider", "Knob" }, "Slider with the value on its handle, or a rotary knob.", "pan slider knob circle mixer balance"),

            Bool(Timeline, "Track controls", "timeline.autofit", "Auto-resize track list to fit", v => timeline.AutoFitTrackList = v, () => timeline.AutoFitTrackList,
                "Grow or shrink the track list / arrangement panel so every track and group row fits, when tracks are added or removed and when groups are shown, hidden, collapsed or expanded.", "track list height fit resize groups collapse auto"),

            // Editing
            Choice(Editing, "Entry", "editing.duration", "Default note value", v => ed.DefaultDuration = int.Parse(v), () => ed.DefaultDuration.ToString(),
                new[] { "1", "2", "4", "8", "16", "32", "64" }, "Duration used for newly entered notes.", "whole half quarter eighth sixteenth thirty-second value"),
            Bool(Editing, "Entry", "editing.advance", "Advance after entering a note", v => ed.AutoAdvance = v, () => ed.AutoAdvance,
                "Move the caret forward by the entered note duration.", "auto advance caret move"),
            Bool(Editing, "Entry", "editing.reverseplusminus", "Reverse + / - duration keys", v => ed.ReversePlusMinusDuration = v, () => ed.ReversePlusMinusDuration,
                "Off (default): + makes the note shorter (8th to 16th), - makes it longer. On: the opposite.", "plus minus duration shorter longer reverse gp5"),
            Bool(Editing, "Entry", "editing.preventoverflow", "Prevent rhythms that overfill a bar", v => ed.PreventBarOverflow = v, () => ed.PreventBarOverflow,
                "Off (default): notes can be made longer freely and a bar that no longer adds up turns red. On: such changes are refused.", "red bar overfill duration longer block prevent"),
            Bool(Editing, "Safety", "editing.confirmdelete", "Confirm before deleting a bar", v => ed.ConfirmDeleteBar = v, () => ed.ConfirmDeleteBar,
                "Ask before deleting a bar and shifting later content.", "confirm delete bar prompt", hotkey: "Bar.Delete"),
            Int(Editing, "Navigation", "editing.scorewheel", "Score wheel scroll distance", v => ed.ScoreWheelScrollPixels = v, () => ed.ScoreWheelScrollPixels, 12, 96,
                "Pixels moved per mouse-wheel notch over the score page.", "score page mouse wheel scroll", "px"),

            // Copy and paste: one row per paste question (Q1..Q5)
            PasteRow(ed, PasteQuestion.BeatsOntoNotes, "Pastes that land on existing notes: replace them at the cursor, or insert and push the following notes along.",
                "paste copy beats replace insert push notes cursor overwrite"),
            PasteRow(ed, PasteQuestion.Octave, "Pastes between instruments of different range, such as guitar and bass: keep the exact pitch, or shift by an octave automatically.",
                "paste copy octave pitch range guitar bass shift instrument"),
            PasteRow(ed, PasteQuestion.BarsOntoNotes, "Pastes of whole bars onto bars that already have notes: overwrite them, or insert before or after.",
                "paste copy bars overwrite insert before after replace"),
            PasteRow(ed, PasteQuestion.BarSettings, "Whether pasted bars bring their time signature, key, tempo and similar settings, or keep the target bars' own.",
                "paste copy bar settings time signature key tempo keep target"),
            PasteRow(ed, PasteQuestion.Drums, "Pastes between a pitched instrument and a drum track: put the rhythm onto one drum sound, or do not paste.",
                "paste copy drums drum sound rhythm pitched track"),

            // Timeline & sections
            Bool(Timeline, "Sections", "timeline.similarcolours", "Same colour for similar sections", v => timeline.MatchSimilarSectionColours = v, () => timeline.MatchSimilarSectionColours,
                "Sections with the same base name (Verse 1, Verse 2, Chorus x2) are shown in the first one's colour.", "section colour similar verse chorus match same"),
            Bool(Timeline, "Sections", "timeline.brackets", "Show section brackets", v => timeline.ShowSectionBrackets = v, () => timeline.ShowSectionBrackets,
                "Draw [ ] brackets around the section being played or edited.", "section brackets indicator highlight outline"),
            Bool(Timeline, "Sections", "timeline.names", "Show section names", v => timeline.ShowSectionNames = v, () => timeline.ShowSectionNames,
                "Show titles inside section blocks.", "section title marker label"),
            Bool(Timeline, "Timeline", "appearance.arrangement", "Show the arrangement overview", v => a.ShowArrangementOverview = v, () => a.ShowArrangementOverview,
                "Show the track arrangement overview.", "timeline arrangement overview panel"),
            Bool(Timeline, "Ruler", "timeline.numbers", "Show bar numbers in the ruler",v => timeline.ShowBarNumbers = v, () => timeline.ShowBarNumbers,
                "Show measure numbers in the timeline ruler.", "bar measure ruler number"),
            Number(Timeline, "Sections", "follow.sectionglow", "Section glow intensity", v => fv.SectionGlowIntensity = v / 100, () => fv.SectionGlowIntensity * 100, 0, 100,
                "Intensity of active and hovered section highlights.", "section glow active hover", "%", 1, 0),
            Number(Timeline, "Sections", "appearance.sectionbracket", "Section bracket thickness", v => a.SectionBracketThickness = v, () => a.SectionBracketThickness, 1, 24,
                "Stroke width of the active section indicator.", "active bracket width", "px"),
            Bool(Timeline, "Sections", "timeline.confirmdelete", "Confirm section deletion", v => g.ConfirmDeleteSection = v, () => g.ConfirmDeleteSection,
                "Ask before removing the section and its musical content from every track.", "confirm destructive remove"),
            // Timeline appearance: the four toggles that used to live only in the timeline's right-click menu.
            Bool(Timeline, "Timeline appearance", "timeline.individualnotes", "Show the notes in each track lane", v => { timeline.ShowIndividualNotes = v; if (v) timeline.ShowContinuousLine = false; }, () => timeline.ShowIndividualNotes,
                "Draw every note as a small mark in its track's lane. Turning this on turns off the continuous line.",
                "timeline individual notes marks lane draw appearance"),
            Bool(Timeline, "Timeline appearance", "timeline.continuousline", "Show one continuous line per track lane", v => { timeline.ShowContinuousLine = v; if (v) timeline.ShowIndividualNotes = false; }, () => timeline.ShowContinuousLine,
                "Draw each track's music as one continuous line instead of separate notes. Turning this on turns off the individual notes.",
                "timeline continuous line blocks lane draw appearance"),
            Bool(Timeline, "Timeline appearance", "timeline.hideemptygrid", "Hide the grid in empty bars", v => timeline.HideEmptyGrid = v, () => timeline.HideEmptyGrid,
                "Leave out the bar lines in bars that have no notes, so the timeline only shows where there is music.",
                "timeline grid empty bars hide lines appearance"),
            Bool(Timeline, "Timeline appearance", "timeline.barglow", "Soft glow on bars that have notes", v => timeline.BarGlow = v, () => timeline.BarGlow,
                "A faint glow around bars that contain notes. Turn it off for a flatter, plainer timeline.",
                "timeline subtle bar glow soft outline appearance"),
            Choice(Timeline, "Timeline appearance", "timeline.playheadstyle", "Playback position marker", v => timeline.PlayheadStyle = PlayheadStyles.Normalize(v), () => PlayheadStyles.Normalize(timeline.PlayheadStyle),
                PlayheadStyles.All,
                "How the playback position shows on the timeline. Line is the white vertical line. Bar marker is a small dark square in the current bar of the selected track (it moves bar by bar). Both shows the line and the marker.",
                "timeline playhead playback position marker line bar square cursor style"),
            Bool(Timeline, "Track list", "timeline.trackgroups", "Show tracks in groups in new songs", v => timeline.ShowGroupsInNewSongs = v, () => timeline.ShowGroupsInNewSongs,
                "New songs list their tracks under a header per group (guitars, basses, drums...), which can be collapsed. For the open song use the track list's right-click menu or the Mixer (Groups in track list).",
                "track list groups headers collapse guitars basses drums mixer group tracks new song"),
            Bool(Timeline, "Clip lanes", "timeline.removeemptylanes", "Remove empty clip lanes automatically", v => timeline.AutoRemoveEmptyLanes = v, () => timeline.AutoRemoveEmptyLanes,
                "When a lane under a track has no clips left (you moved, cut or deleted the last one), remove it and close the lanes below it. Turn it off to keep empty lanes until you remove them yourself. A track that is armed for recording keeps its lanes.",
                "clip lanes empty remove delete auto close takes audio midi tidy"),
            Bool(Timeline, "Dragging", "timeline.draganimation", "Animate section dragging", v => timeline.SectionDragAnimation = v, () => timeline.SectionDragAnimation,
                "Animate sections moving aside while dragging.", "drag transition animation"),

            // Fretboard (legacy values remain in Editing for file compatibility)
            Bool(Fretboard, "Appearance","appearance.fretboard", "Show the fretboard", v => a.ShowFretboard = v, () => a.ShowFretboard,
                "Show the instrument/fretboard panel.", "instrument panel hide"),
            Choice(Fretboard, "Appearance","fretboard.position", "Fretboard position", v => a.FretboardPosition = v, () => a.FretboardPosition,
                new[] { "Left", "Centre", "Right" }, "Snap the fretboard horizontally.", "fretboard alignment position"),
            Choice(Fretboard, "Appearance","editing.frets", "Fretboard frets", v => ed.FretboardFrets = int.Parse(v), () => ed.FretboardFrets.ToString(),
                new[] { "12", "24" }, "Show 12 or 24 frets.", "fret count range"),
            Bool(Fretboard, "Appearance","editing.lefthanded", "Left-handed fretboard", v => ed.LeftHanded = v, () => ed.LeftHanded,
                "Mirror the fretboard for left-handed playing.", "left handed mirror orientation"),
            Bool(Fretboard, "Appearance","editing.notenames", "Show note names on the fretboard", v => ed.ShowNoteNames = v, () => ed.ShowNoteNames,
                "Label frets with their note names.", "pitch labels names"),
            Bool(Fretboard, "Preview", "editing.horizon.enabled", "Show look-ahead notes", v => ed.PreviewNotesEnabled = v, () => ed.PreviewNotesEnabled,
                "Show upcoming notes on the fretboard.", "preview next future notes"),
            Choice(Fretboard, "Appearance","fretboard.instrumentview", "Default instrument view", v => ed.InstrumentView = v, () => ed.InstrumentView,
                InstrumentViews.All,
                "Match the instrument (default): stringed instruments get a fretboard with the track's own strings, drums get drum pads, and piano, winds and everything else get a keyboard. Or always show one view. Right-click the panel to change it for one track or all tracks.",
                "instrument view fretboard keyboard piano drums pads default show match"),
            Choice(Fretboard, "Appearance","fretboard.keyboardkeys", "Keyboard size", v => ed.KeyboardKeys = int.TryParse(v, out var k) ? k : 88, () => ed.KeyboardKeys.ToString(),
                InstrumentViews.KeyboardSizes.Select(k => k.ToString()).ToArray(),
                "Keys on the keyboard view: 88 is a full piano. Smaller keyboards follow the notes being played.",
                "keyboard piano keys size 88 76 61 49 37 25"),
            Choice(Fretboard, "Appearance","fretboard.keyboardcolours", "Keyboard key colours", v => ed.KeyboardKeyColours = v, () => ed.KeyboardKeyColours,
                KeyboardKeyStyles.All,
                "The keyboard view's white keys: match the theme (soft grey in the dark theme, white in the light theme), or always grey or always white.",
                "keyboard piano keys colour color grey gray white appearance"),
            Choice(Fretboard, "Appearance","fretboard.scalestyle", "Scale highlight style", v => ed.ScaleHighlightStyle = v, () => ed.ScaleHighlightStyle,
                ScaleHighlightStyles.All,
                "How the notes of a highlighted scale are marked on the fretboard and keyboard: shaded cells, small circles or rings. The root is always marked more strongly.",
                "scale highlight style circles dots rings shaded appearance guitar pro"),
            Choice(Fretboard, "Appearance","fretboard.scalecolour", "Scale highlight colour", v => ed.ScaleHighlightColour = v, () => ed.ScaleHighlightColour,
                ScaleHighlightStyles.Colours,
                "Colour of the scale highlight on the fretboard and keyboard.",
                "scale highlight colour color blue green amber purple red teal grey"),
            Int(Fretboard, "Appearance", "fretboard.scalestrength", "Scale highlight strength", v => ed.ScaleHighlightStrength = v, () => ed.ScaleHighlightStrength,
                ScaleHighlightStyles.MinStrength, ScaleHighlightStyles.MaxStrength,
                "How strong the scale highlight looks on the fretboard and keyboard: lower is dimmer, higher is brighter (100% is the standard look). The root note always stays stronger than the other scale notes.",
                "scale highlight strength opacity brightness dimmer brighter intensity transparency", unit: "%", step: ScaleHighlightStyles.StrengthStep),
            Choice(Fretboard, "Appearance","fretboard.markercolour", "Fret marker colour", v => ed.FretMarkerColour = v, () => ed.FretMarkerColour,
                FretMarkerLevels.Colours,
                "Colour of the position dots on the fretboard (frets 3, 5, 7, 9, 12...). White is the standard; Default uses the theme's own dot colour.",
                "fret marker dots inlay position colour color"),
            Choice(Fretboard, "Appearance","fretboard.markerbrightness", "Fret marker brightness", v => ed.FretMarkerBrightness = v, () => ed.FretMarkerBrightness,
                FretMarkerLevels.All,
                "How bright the fretboard position dots are. Original (the standard) is the plain, dimmer look; the other levels lift the dots towards white.",
                "fret marker dots inlay position brightness bright dim"),
            Choice(Fretboard, "Appearance","fretboard.numbersize", "Fret number size", v => ed.FretNumberSize = v, () => ed.FretNumberSize,
                FretNumberSizes.All,
                "Size of the fret numbers, technique tags and note bubbles on the fretboard (Large is the standard size).",
                "fret number size small medium large bubble label"),
            Choice(Fretboard, "Appearance","fretboard.stringspacing", "String spacing", v => ed.FretStringSpacing = v, () => ed.FretStringSpacing,
                FretStringSpacings.All,
                "How far apart the strings are drawn relative to the fret width. Natural keeps real-fretboard proportions in any window shape; Wide stretches up to 1.5x natural.",
                "fretboard string spacing stretch compact natural wide tall portrait"),
            Choice(Fretboard, "Appearance","audio.fretboardstyle", "Fretboard style", v => au.FretboardStyle = v, () => au.FretboardStyle,
                new[] { "TabForge", "GP5: Beat", "GP5: Beat + next beat", "GP5: Beat + bar", "GP5: Bar" },
                "TabForge previews a set number of upcoming notes. The beat and bar layouts follow the score instead: the current beat, the next beat, or every note of the current bar.",
                "fretboard style guitar pro gp5 look preview red show beat bar next"),
            Int(Fretboard, "Preview", "editing.horizon", "Look-ahead notes", v => ed.PreviewHorizon = v, () => ed.PreviewHorizon, 1, 10,
                "Number of upcoming notes to display.", "horizon preview upcoming", dependsOn: "editing.horizon.enabled"),
            Choice(Fretboard, "Preview", "editing.scale", "Scale highlight", v => ed.ScaleHighlight = v == "Off" ? null : v,
                () => ed.ScaleHighlight ?? "Off", new[] { "Off" }.Concat(from root in MusicTheoryService.NoteNames from scale in new[] { "Major", "Natural Minor", "Minor Pentatonic", "Major Pentatonic", "Dorian", "Mixolydian", "Blues" } select $"{root} {scale}").ToArray(),
                "Highlight scale tones on the fretboard.", "scale key root highlight notes"),
            Bool(Fretboard, "Playback", "follow.fretboard", "Update the fretboard during playback", v => fv.FollowFretboard = v, () => fv.FollowFretboard,
                "Update fretboard note positions as playback moves.", "playback live update"),
            // Size and per-track views: the fretboard right-click menu's "Lock fretboard size", "Show this track as" and "Show all tracks as".
            Bool(Fretboard, "Size", "fretboard.locksize", "Lock fretboard size", v => a.LockInstrumentSize = v, () => a.LockInstrumentSize,
                "Locked: dragging the pane's edge does not resize the fretboard or keyboard. Unlocked: the drawing scales with the pane (between 0.7x and 2x). The pane never clips: it scrolls when it is too small.",
                "lock fretboard keyboard size resize pane splitter height scale drag", hotkey: "View.LockInstrumentSize"),
            Button(Fretboard, "Instrument view", "fretboard.showallas", "Show all tracks as",
                "Use the default instrument view above for every track of the open song, replacing any view you chose for a single track (right-click the fretboard > Show this track as).",
                "show all tracks as instrument view fretboard keyboard drums apply every track reset per track"),

            // Tabs & windows
            Bool(Tabs, "Window", "general.restorewindow", "Remember the window size", v => g.RestoreWindow = v, () => g.RestoreWindow,
                "Restore window geometry and maximised state between runs.", "window restore geometry size"),
            Bool(Tabs, "Tabs", "appearance.tabstrip", "Show the tab strip in the title bar", v => a.ShowTabStrip = v, () => a.ShowTabStrip,
                "Show document tabs in the title bar.", "document title bar hide"),
            Choice(Tabs, "Tabs", "tabs.style", "Tab shape", v => tabs.Style = v, () => tabs.Style,
                new[] { TabStyles.Rounded, TabStyles.Square }, "Rounded browser-like tabs or flat rectangles.", "tab shape corner"),
            Choice(Tabs, "Tabs", "tabs.width", "Tab width", v => tabs.WidthMode = v, () => tabs.WidthMode,
                new[] { TabWidthModes.Fit, TabWidthModes.Content }, "Share the strip width or size tabs to their titles.", "tab width fit content"),
            Number(Tabs, "Tabs", "tabs.maxwidth", "Maximum tab width", v => tabs.MaxTabWidth = v, () => tabs.MaxTabWidth, 80, 600,
                "Upper bound on a tab's width.", "tab width maximum size", "px"),
            Number(Tabs, "Tabs", "tabs.minwidth", "Minimum tab width", v => tabs.MinTabWidth = v, () => tabs.MinTabWidth, 46, 400,
                "Tabs never shrink below this; extra tabs scroll with the strip's arrows instead.", "tab width minimum size shrink", "px"),
            Number(Tabs, "Tabs", "tabs.height", "Tab height", v => tabs.TabHeight = v, () => tabs.TabHeight, 26, 44,
                "Height of each tab in the title bar.", "tab height size tall", "px"),
            Number(Tabs, "Tabs", "tabs.fontsize", "Tab title size", v => tabs.TabFontSize = v, () => tabs.TabFontSize, 10, 16,
                "Font size of tab titles.", "tab font text size title", "pt"),
            Choice(Tabs, "Tabs", "tabs.closebutton", "Close button", v => tabs.CloseButton = v, () => tabs.CloseButton,
                new[] { CloseButtonModes.Always, CloseButtonModes.ActiveAndHover, CloseButtonModes.ActiveOnly, CloseButtonModes.Never },
                "When each tab's close button is visible.", "tab close cross"),
            Bool(Tabs, "Tabs", "tabs.doubleclick", "Double-click closes a tab", v => tabs.CloseOnDoubleClick = v, () => tabs.CloseOnDoubleClick,
                "Close a tab by double-clicking it.", "double click close"),
            Choice(Tabs, "Tabs", "tabs.middleclick", "Middle-click a tab", v => tabs.MiddleClick = v, () => tabs.MiddleClick,
                new[] { MiddleClickActions.Close, MiddleClickActions.Duplicate, MiddleClickActions.NewTab, MiddleClickActions.Nothing },
                "Action performed when a tab is middle-clicked.", "middle click mouse"),
            Bool(Tabs, "Tabs", "tabs.middlebar", "Middle-click title bar opens a tab", v => tabs.MiddleClickTitleBarNewTab = v, () => tabs.MiddleClickTitleBarNewTab,
                "Open a new tab from empty title-bar space.", "middle click title bar"),
            Choice(Tabs, "Tabs", "tabs.lastclosed", "When the last tab is closed", v => tabs.LastTabClosed = v, () => tabs.LastTabClosed,
                new[] { LastTabActions.NewTab, LastTabActions.CloseWindow }, "Open a fresh tab or close the window.", "last tab close window"),
            Choice(Tabs, "Tabs", "tabs.newposition", "New tab position", v => tabs.NewTabPosition = v, () => tabs.NewTabPosition,
                new[] { NewTabPositions.AfterCurrent, NewTabPositions.AtEnd }, "Open next to the active tab or at the end.", "new tab order"),
            Bool(Tabs, "Opening", "tabs.openincurrent", "Open projects in the current tab", v => tabs.OpenInCurrentTab = v, () => tabs.OpenInCurrentTab,
                "Replace the active tab when opening a project.", "open project replace ctrl o"),
            Choice(Tabs, "Playback", "tabs.playbackonswitch", "When opening/switching to another tab while music is playing", v => tabs.PlaybackOnTabSwitch = v, () => tabs.PlaybackOnTabSwitch,
                new[] { TabPlaybackActions.ContinuePlayingPrevious, TabPlaybackActions.PausePrevious, TabPlaybackActions.StopPrevious },
                "Choose whether playback in other tabs continues, pauses or stops.", "audio playback switch"),
            Bool(Tabs, "Window", "tabs.detach", "Drag a tab out to a new window", v => tabs.DetachToNewWindow = v, () => tabs.DetachToNewWindow,
                "Tear a tab into its own window by dragging it away.", "detach tear out"),
            Bool(Tabs, "Window", "tabs.merge", "Allow dropping tabs onto other windows", v => tabs.MergeAcrossWindows = v, () => tabs.MergeAcrossWindows,
                "Move tabs between TabForge windows by dragging.", "merge drop"),
            Bool(Tabs, "Tabs", "tabs.playing", "Show playing badge", v => tabs.ShowPlayingIndicator = v, () => tabs.ShowPlayingIndicator,
                "Mark tabs whose scores are playing.", "playing audio badge"),

            // Advanced contains a deliberately informational page, not fabricated runtime toggles.
        };

        // One colour per mixer group: the colour "Colour tracks by group" (track list menu) and the Mixer's group swatches use.
        foreach (var group in new[] { MixerGroups.Guitars, MixerGroups.Basses, MixerGroups.Keys, MixerGroups.Drums, MixerGroups.Other, MixerGroups.AllButGuitarsAndBass })
        {
            var name = group;
            list.Add(Colour(Appearance, "Track group colours", "appearance.groupcolour." + name.Replace(' ', '-').ToLowerInvariant(), $"{name} track colour",
                v => a.GroupColours[name] = v, () => TrackColouring.ColourOf(name, a.GroupColours),
                $"The colour of the {name} group. \"Colour tracks by group\" in the track list's right-click menu gives every {name.ToLowerInvariant()} track this colour.",
                "track group colours colour color mixer guitars basses keys drums other instruments colour tracks by group"));
        }

        for (var i = 0; i < list.Count; i++)
        {
            var d = list[i];
            // A row not in the layout table keeps the page and group it was declared with (the catalogue self-test flags it).
            var place = Places.TryGetValue(d.Key, out var p) ? p : null;
            var page = place?.Page ?? d.Category;
            var group = place?.Group ?? d.Group;
            OldNames.TryGetValue(d.Key, out var oldName);
            PageWords.TryGetValue(page, out var pageWords);
            list[i] = new SettingDescriptor
            {
                Key = d.Key, Category = page, Group = group, Title = d.Title, Description = d.Description,
                Kind = d.Kind, Choices = d.Choices, Min = d.Min, Max = d.Max, Step = d.Step, Decimals = d.Decimals,
                Unit = d.Unit, DependsOn = d.DependsOn, DependsOnValue = d.DependsOnValue,
                HotkeyAction = d.HotkeyAction, Keywords = d.Keywords,
                Get = d.Get, Set = d.Set, More = place?.More ?? false, Order = place?.Order ?? int.MaxValue,
                SearchText = string.Join(' ', new[] { d.Title, d.Description, page, group, d.Key, oldName ?? "", pageWords ?? "" }.Concat(d.Keywords)).ToLowerInvariant()
            };
        }
        return list;
    }

    private static SettingDescriptor Bool(string category, string group, string key, string title, Action<bool> set,
        Func<bool> get, string description, string keywords = "", string? hotkey = null, string? dependsOn = null,
        string? dependsOnValue = null) =>
        Make(category, group, key, title, description, SettingKind.Bool, () => get(), v => set(v is bool b && b),
            keywords, dependsOn, hotkey: hotkey, dependsOnValue: dependsOnValue);

    /// <summary>A "Copy and paste" row: "Ask every time" plus each answer of the question.</summary>
    private static SettingDescriptor PasteRow(EditingSettings ed, PasteQuestion q, string description, string keywords)
    {
        var options = PasteQuestionInfo.Options(q);
        var choices = new[] { PasteQuestionInfo.AskLabel }.Concat(options.Select(o => o.Label)).ToArray();
        return Choice(Editing, "Copy and paste", PasteQuestionInfo.SettingKey(q), PasteQuestionInfo.Title(q),
            v => PasteQuestionInfo.Set(ed, q, options.FirstOrDefault(o => o.Label == v)?.Id),
            () => options.FirstOrDefault(o => o.Id == PasteQuestionInfo.Get(ed, q))?.Label ?? PasteQuestionInfo.AskLabel,
            choices, description, keywords);
    }

    private static SettingDescriptor Choice(string category, string group, string key, string title, Action<string> set,
        Func<string> get, string[] choices, string description, string keywords = "", string? dependsOn = null,
        string? dependsOnValue = null) =>
        Make(category, group, key, title, description, SettingKind.Choice, () => get(), v => set(v?.ToString() ?? ""),
            keywords, dependsOn, choices: choices, dependsOnValue: dependsOnValue);

    private static SettingDescriptor Int(string category, string group, string key, string title, Action<int> set,
        Func<int> get, double min, double max, string description, string keywords = "", string unit = "",
        double step = 1, string? dependsOn = null, string? hotkey = null, string? dependsOnValue = null) =>
        Make(category, group, key, title, description, SettingKind.Number, () => (double)get(),
            v => set((int)Math.Round(Math.Clamp(Convert.ToDouble(v), min, max), MidpointRounding.AwayFromZero)),
            keywords, dependsOn, min, max, step, 0, unit, hotkey, dependsOnValue: dependsOnValue);

    private static SettingDescriptor Number(string category, string group, string key, string title, Action<double> set,
        Func<double> get, double min, double max, string description, string keywords = "", string unit = "",
        double step = 1, int decimals = 0, string? dependsOn = null, string? dependsOnValue = null) =>
        Make(category, group, key, title, description, SettingKind.Number, () => get(),
            v => set(Math.Clamp(Convert.ToDouble(v), min, max)), keywords, dependsOn, min, max, step, decimals, unit,
            dependsOnValue: dependsOnValue);

    private static SettingDescriptor Text(string category, string group, string key, string title, Action<string> set,
        Func<string?> get, string description, string keywords = "") =>
        Make(category, group, key, title, description, SettingKind.Text, () => get() ?? "",
            v => set(v?.ToString() ?? ""), keywords);

    /// <summary>A row with a button (the editor is built by the Preferences window from the key); it stores nothing.</summary>
    private static SettingDescriptor Button(string category, string group, string key, string title, string description, string keywords) =>
        Make(category, group, key, title, description, SettingKind.Button, () => "", _ => { }, keywords);

    private static SettingDescriptor Colour(string category, string group, string key, string title, Action<string> set,
        Func<string> get, string description, string keywords = "", string? dependsOn = null,
        string? dependsOnValue = null) =>
        Make(category, group, key, title, description, SettingKind.Colour, () => get(),
            v => set(v?.ToString() ?? ""), keywords, dependsOn, dependsOnValue: dependsOnValue);

    private static SettingDescriptor Make(string category, string group, string key, string title, string description,
        SettingKind kind, Func<object?> get, Action<object?> set, string keywords, string? dependsOn = null,
        double min = 0, double max = 100, double step = 1, int decimals = 0, string unit = "",
        string? hotkey = null, IReadOnlyList<string>? choices = null, string? dependsOnValue = null) => new()
    {
        Key = key, Category = category, Group = group, Title = title, Description = description, Kind = kind,
        Get = get, Set = set, Keywords = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        DependsOn = dependsOn, DependsOnValue = dependsOnValue, Min = min, Max = max, Step = step, Decimals = decimals, Unit = unit,
        HotkeyAction = hotkey, Choices = choices ?? Array.Empty<string>()
    };
}
