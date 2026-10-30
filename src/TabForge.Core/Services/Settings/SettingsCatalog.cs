using TabForge.Models;

namespace TabForge.Services;

public enum SettingKind { Bool, Choice, Number, Text, Colour, Button, FolderList }

/// <summary>A searchable, grouped setting descriptor bound to a live settings model.</summary>
public sealed class SettingDescriptor : Presenters.IPreferenceRow
{
    bool Presenters.IPreferenceRow.IsSwitch => Kind == SettingKind.Bool;
    bool Presenters.IPreferenceRow.IsColour => Kind == SettingKind.Colour;
    object? Presenters.IPreferenceRow.Value => Get();

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
    /// <summary>Shown under a disabled dependent row instead of the "Choose … above to use this." hint; null = the automatic hint.</summary>
    public string? DisabledNote { get; init; }
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

// Owns: the catalogue of settings descriptors used by search and the settings pages.
// Does not own: storing the values (AppSettings) and the page layout.
// Tests: ContextMenuTests.
/// <summary>Declarative catalogue of settings exposed in the Settings window.</summary>
public static partial class SettingsCatalog
{
    // Pages, in rail order. The display name is the page key.
    public const string Home = "Common settings";
    public const string General = "General";
    public const string Appearance = "Appearance";
    public const string Score = "Score & Notation";
    public const string Fretboard = "Fretboard & Keyboard";
    public const string Timeline = "Timeline & Tracks";
    public const string Editing = "Editing";
    /// <summary>Scrolling while playing, speed, metronome, count-in and note preview.</summary>
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

    /// <summary>The line under a page's title.</summary>
    public static string Subtitle(string category) => category switch
    {
        SettingsCatalog.Home => "The settings people change most, and where to find the rest",
        SettingsCatalog.General => "Updates, window memory and the questions TabForge asks before it acts",
        SettingsCatalog.Appearance => "How the program looks: theme, size and text, panels, motion, track colours and the playing highlight",
        SettingsCatalog.Score => "What the score shows, its spacing, text, page and ink colours, selection and hover",
        SettingsCatalog.Fretboard => "The instrument panel: view, frets, note names, scales, practice aids and its appearance",
        SettingsCatalog.Timeline => "The track list, the timeline display and sections",
        SettingsCatalog.Editing => "Note entry, copy and paste, mouse scrolling",
        SettingsCatalog.Playback => "Scrolling while playing, speed, metronome, count-in and note preview",
        SettingsCatalog.AudioVst => "Output device, driver, buffer, plug-in folders and plug-in options",
        SettingsCatalog.Recording => "Input device, ASIO inputs and the recording offset",
        SettingsCatalog.Tabs => "Opening, closing and dragging tabs, and how they look",
        SettingsCatalog.Hotkeys => "Search, assign and resolve keyboard shortcuts",
        SettingsCatalog.Files => "Opening and saving, autosave and recovery, Windows file association, linked audio folders",
        SettingsCatalog.Advanced => "Version, settings file, compatibility and reset",
        _ => category
    };

    public const string DefaultAudioDevice = "(Windows default)";

    /// <summary>The ASIO driver's channels as the driver names them: "1: Analogue 1".</summary>
    private static List<string> AsioInputs(PluginSettings pl) => Numbered(AsioNames(pl)?.Inputs, "Input");
    private static List<string> AsioOutputs(PluginSettings pl) => Numbered(AsioNames(pl)?.Outputs, "Output");
    internal static List<string> Numbered(IReadOnlyList<string>? names, string kind) => (names is { Count: > 0 } ? names : Enumerable.Range(1, 8).Select(i => $"{kind} {i}").ToArray()).Select((n, i) => $"{i + 1}: {n}").ToList();

    /// <summary>The ASIO driver's channel names; null while the engine is still asked (see <see cref="PendingChoices"/>); none when the driver is not ASIO or its names are unknown (generic names are listed then).</summary>
    private static (IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs)? AsioNames(PluginSettings pl) =>
        pl.Driver == AudioDrivers.Asio ? Features.CatalogComposition.Platform.AsioChannelNames(pl.Device) : (Array.Empty<string>(), Array.Empty<string>());

    /// <summary>The channel choices of <paramref name="channels"/> (inputs or outputs): "Checking…" until the engine has told the driver's channel names.</summary>
    private static PendingChoices AsioChoices(PluginSettings pl, Func<PluginSettings, List<string>> channels) =>
        new(() => AsioNames(pl) is null ? null : channels(pl), () => Features.CatalogComposition.Platform.SampleRatesSettled(pl.Driver, pl.Device));

    /// <summary>A choice list computed on first use, so building the catalogue never enumerates audio devices or ASIO drivers.</summary>
    private sealed class LazyChoices : IReadOnlyList<string>
    {
        private readonly Lazy<IReadOnlyList<string>> _items;
        public LazyChoices(Func<IReadOnlyList<string>> make) => _items = new(make);
        public int Count => _items.Value.Count;
        public string this[int index] => _items.Value[index];
        public IEnumerator<string> GetEnumerator() => _items.Value.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static IReadOnlyList<string> Lazy(Func<IReadOnlyList<string>> make) => new LazyChoices(make);

    private static string[]? AudioInputChoices(PluginSettings settings)
    {
        if (Features.CatalogComposition.Platform.InputDeviceNames() is not { } listed) return null;   // still being listed
        var names = new List<string> { DefaultAudioDevice };
        names.AddRange(listed);
        if (settings.InputDevice.Length > 0 && !names.Contains(settings.InputDevice)) names.Add(settings.InputDevice);
        return names.Distinct().ToArray();
    }

    /// <summary>Output devices for the chosen driver (names only: no driver is loaded), plus the current choice. ASIO lists the installed drivers and has no Windows default.</summary>
    private static string[]? AudioDeviceChoices(PluginSettings settings)
    {
        var names = new List<string>();
        if (settings.Driver != AudioDrivers.Asio) names.Add(DefaultAudioDevice);
        try
        {
            if (Features.CatalogComposition.Platform.OutputDeviceNames(settings.Driver) is not { } listed) return null;   // still being listed
            names.AddRange(listed);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TypeInitializationException) // Traced to errors.log; an empty list is shown for this settings row
        {
            Services.Trace.Error(Services.Trace.Ui, $"audio device list failed: {ex.Message}");
        }
        if (settings.Device.Length > 0 && !names.Contains(settings.Device)) names.Add(settings.Device);
        return names.Distinct().ToArray();
    }

    /// <summary>The output device row's value: ASIO has no Windows default, so an empty device (an older setting) is the first installed driver (as the engine opens it).</summary>
    private static string ShownOutputDevice(PluginSettings pl) => ConfiguredOutputDevice(pl) is { Length: > 0 } device ? device : DefaultAudioDevice;

    /// <summary>The output device the settings name; for ASIO with none (an older setting) the first installed driver, as the engine opens it; empty: the Windows default.</summary>
    public static string ConfiguredOutputDevice(PluginSettings pl) =>
        pl.Device.Length > 0 ? pl.Device : pl.Driver == AudioDrivers.Asio ? AudioDeviceChoices(pl)?.FirstOrDefault() ?? "" : "";

    /// <summary>The device a driver starts with when chosen: the first installed driver for ASIO, else the Windows default (empty).</summary>
    private static string FirstOutputDevice(string driver) =>
        driver == AudioDrivers.Asio ? AudioDeviceChoices(new PluginSettings { Driver = driver })?.FirstOrDefault() ?? "" : "";

    public static readonly IReadOnlyList<string> Categories = new[]
    {
        Home, General, Appearance, Score, Fretboard, Timeline, Editing, Playback, AudioVst, Recording, Tabs, Hotkeys, Files, Advanced
    };

    /// <summary>
    /// Where every catalogue row lives: page, group, and Basic (always shown) or More (inside the group's collapsed "More options",
    /// marked with a trailing *). The order here is the order on the page. Only the place a row is shown changes; nothing is stored differently.
    /// </summary>
    private static readonly IReadOnlyList<(string Page, string Group, string Keys)> Layout = new (string Page, string Group, string Keys)[]
    {
        (General, "Updates", "general.checkupdates general.checknow"),
        (General, "Window", "general.restorewindow"),
        (General, "Confirmations", "general.confirmclose general.confirmdiscardsettings editing.confirmdelete editing.bardelete timeline.confirmdelete"),

        (Appearance, "Theme", "appearance.thememode"),
        (Appearance, "Size and text", "appearance.uiscale appearance.density appearance.fontsource appearance.font* appearance.fontsize* appearance.iconsize* appearance.toolbaricons*"),
        (Appearance, "Panels and toolbars", "general.toolbar general.statusbar appearance.tabstrip"),
        (Appearance, "Track colours", "appearance.tracktint appearance.mutedtrackdim mixer.grouprules appearance.groupcolour.guitars* appearance.groupcolour.basses* appearance.groupcolour.keys* appearance.groupcolour.drums* appearance.groupcolour.other* appearance.groupcolour.other-instruments*"),
        (Appearance, "Interface colours", "appearance.accent appearance.playbackhighlight appearance.playbackhighlightcolour"),
        (Appearance, "Playback look", "follow.highlight follow.playingbar follow.playingbar.colour follow.playingbar.opacity follow.playingbar.stopped* follow.bg* follow.duration.enabled* follow.durationglow* follow.durationopacity* follow.playhead.thickness*"),
        (Appearance, "Motion", "appearance.reduceanimations appearance.animationspeed* timeline.draganimation*"),
        (Appearance, "Custom palette", "appearance.background* appearance.panel* appearance.titlebar* appearance.tabactive* appearance.tabhover* appearance.text* appearance.muted* appearance.resetcolours"),

        (Score, "What is shown", "score.defaultnotation score.pagelayout score.scrolling score.barnumbers score.sectionheadings score.dynamics score.barnumberfrequency* score.ledger*"),
        (Score, "Spacing and size", "appearance.spacing score.systemspacing* score.measurespacing*"),
        (Score, "Text and fonts", "appearance.scorefont appearance.scorefontsize score.textfonts appearance.scorebold* appearance.scoreitalic*"),
        (Score, "Appearance", "appearance.paper appearance.scorepaper.dark* appearance.scorepaper.light* appearance.scoreink.dark* appearance.scoreink.light* appearance.scorelines.dark* appearance.scorelines.light* appearance.cursor* score.staffopacity* appearance.selection* appearance.hover* score.hoverintensity* score.noteoutline* score.selectionintensity*"),

        (Fretboard, "Panel", "appearance.fretboard fretboard.position fretboard.instrumentview fretboard.showallas fretboard.dockposition fretboard.locksize"),
        (Fretboard, "Guitar fretboard", "editing.frets editing.lefthanded editing.notenames"),
        (Fretboard, "Keyboard", "fretboard.keyboardkeys fretboard.keyboardcolours*"),
        (Fretboard, "Practice aids", "editing.horizon.enabled editing.scale follow.fretboard editing.horizon*"),   // audio.fretboardstyle: unbuilt, row hidden
        (Fretboard, "Appearance", "fretboard.scalestyle* fretboard.scalecolour* fretboard.scalestrength* fretboard.markercolour* fretboard.markerbrightness* fretboard.numbersize* fretboard.markersize* fretboard.stringspacing*"),
        (Fretboard, "Scale readout", "scalereadout.readfrom scalereadout.listento scalereadout.prefercommon scalereadout.fretboard scalereadout.family.major* scalereadout.family.minor* scalereadout.family.pentatonic* scalereadout.family.blues* scalereadout.family.harmonic-minor* scalereadout.family.melodic-minor* scalereadout.family.modes* scalereadout.family.exotic*"),

        (Timeline, "Track list", "timeline.autofittimeline.trackgroups* timeline.addtracklane* timeline.mastertrack* timeline.mastertrackcolours* timeline.mastertrackcolour* timeline.mastertrackopacity*"),
        (Timeline, "Clip lanes", "timeline.removeemptylanes*"),
        (Timeline, "Timeline display", "appearance.arrangement timeline.numbers timeline.individualnotes timeline.continuousline* timeline.hideemptygrid* timeline.tracklines* timeline.shadeselected* timeline.barglow* timeline.playheadstyle* appearance.timelinescrollbar*"),
        (Timeline, "Sections", "timeline.similarcolours timeline.brackets timeline.names follow.sectionglow* appearance.sectionbracket*"),

        (Editing, "Note entry", "editing.duration editing.advance editing.reverseplusminus editing.preventoverflow* editing.fillrests* editing.deleteleaves*"),
        (Editing, "Mouse and scrolling", "editing.scorewheel*"),
        (Editing, "Converting tracks", "editing.convertmidi"),
        (Editing, "Clips", "editing.trimemptybars editing.sectionaudio editing.clipoverlap editing.emptylane"),
        (Editing, "Sections", "editing.sectionstretch editing.sectionstretchback"),
        (Editing, "Copy and paste", "editing.paste.beats* editing.paste.octave* editing.paste.bars* editing.paste.barsettings* editing.paste.drums*"),
        (Editing, "Piano roll", "editing.pianoroll.needle"),

        (Playback, "Scrolling and following", "follow.playback follow.resume follow.mode follow.horizontal follow.vertical follow.anticipation* follow.verticaltrigger* follow.margin* follow.stopmanual* follow.stopatend* follow.fps*"),
        (Playback, "Practice", "audio.metronome audio.countin audio.speed audio.countinbars*"),
        (Playback, "Metronome", "audio.metrovolume audio.metroclick* audio.metroaccent* audio.metroaccentvolume* audio.metroclickvolume* audio.metrodivision*"),
        (Playback, "Note preview", "audio.preview audio.previewlen* audio.letring*"),
        (Playback, "Several tabs", "tabs.playbackonswitch"),

        (AudioVst, "Output device", "vst.driver vst.device vst.asio.configure vst.buffer vst.samplerate vst.renderlimiter vst.asio.out* vst.asio.outlast* vst.followvolume* vst.livelimiter* vst.popguard*"),
        (AudioVst, "Playback engine", "vst.soundengine vst.playall* vst.autogm* vst.winmidilatency*"),
        (AudioVst, "Plug-ins", "vst.folders vst.commonfolders* vst.rememberscan* vst.dock* vst.ontop* vst.scanstandard* vst.allowinstalled* vst.isolate* vst.autopitch* vst.startuptracks* vst.quarantine*"),

        (Recording, "Input device", "vst.input vst.asio.inputs vst.asio.infirst* vst.asio.oneinput* vst.allinputs* vst.inputsshown*"),
        (Recording, "Timing", "vst.recordoffset*"),
        (Recording, "MIDI recording", "vst.midiover* vst.midiquantize*"),

        (Tabs, "Opening and closing tabs", "tabs.closebutton tabs.lastclosed tabs.newposition tabs.openincurrent tabs.dropnewtab tabs.midiimport tabs.playing"),
        (Tabs, "Tab look", "tabs.style* tabs.width* tabs.maxwidth* tabs.minwidth* tabs.height* tabs.fontsize*"),
        (Tabs, "Mouse actions", "tabs.doubleclick* tabs.middleclick* tabs.middlebar*"),
        (Tabs, "Dragging between windows", "tabs.detach* tabs.merge*"),

        (Files, "Opening and saving", "general.openfromexplorer general.saveformat general.externalmedia general.collectlayout"),
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
        foreach (var (page, group, keys) in Features.CatalogComposition.MergeLayout(Layout))
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

    /// <summary>Where the playing-look rows used to be: Playback and Practice, Appearance card, Highlighting.</summary>
    private const string MovedFromPlayback = "playback practice appearance highlighting";

    /// <summary>Menu wording, old titles and old page names that still find a row in search (the audit's "old names" rule).</summary>
    private static readonly Dictionary<string, string> OldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio.fretboardstyle"] = "preview layout", ["editing.horizon.enabled"] = "preview next notes", ["editing.horizon"] = "preview length",
        ["fretboard.numbersize"] = "number size", ["appearance.paper"] = "dark page light page", ["follow.mode"] = "page turn smooth page turn follow the playhead",
        ["timeline.barglow"] = "subtle bar glow", ["timeline.individualnotes"] = "timeline display individual notes",
        ["timeline.continuousline"] = "timeline display continuous line", ["timeline.hideemptygrid"] = "timeline display hide grid", ["timeline.tracklines"] = "timeline display track lines separator",
        ["appearance.density"] = "density", ["score.defaultnotation"] = "show standard notation default score display",
        ["score.pagelayout"] = "page continuous", ["score.scrolling"] = "vertical horizontal", ["score.textfonts"] = "text fonts",
        ["fretboard.showallas"] = "show all tracks as", ["fretboard.instrumentview"] = "show this track as",
        ["appearance.playbackhighlight"] = "playing note colour auto", ["appearance.playbackhighlightcolour"] = "playing note colour playback line colour",
        ["follow.highlight"] = MovedFromPlayback, ["follow.playingbar"] = MovedFromPlayback, ["follow.playingbar.colour"] = MovedFromPlayback, ["follow.playingbar.opacity"] = MovedFromPlayback + " playing bar strength",
        ["follow.playingbar.stopped"] = MovedFromPlayback, ["follow.bg"] = MovedFromPlayback, ["follow.duration.enabled"] = MovedFromPlayback, ["follow.durationglow"] = MovedFromPlayback,
        ["follow.durationopacity"] = MovedFromPlayback, ["follow.playhead.thickness"] = MovedFromPlayback,
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
        var list = new List<SettingDescriptor>();
        list.AddRange(GeneralRows(s));
        list.AddRange(AppearanceRows(s));
        list.AddRange(ScoreRows(s));
        list.AddRange(PlaybackRows(s));
        list.AddRange(VstOutputRows(s));
        list.AddRange(VstInputRows(s));
        list.AddRange(AudioRows(s));
        list.AddRange(EditingRows(s));
        list.AddRange(TimelineRows(s));
        list.AddRange(Features.CatalogComposition.SettingRows(s));
        list.AddRange(FretboardRows(s));
        list.AddRange(TabsRows(s));

        list.Add(Button(Appearance, "Track colours", "mixer.grouprules", "Mixer group rules",
            "Which instruments go in which mixer group (Guitars, Basses, Drums, Other instruments by default): add, rename and reorder groups and give each its rules. Choose All songs to change the rules every song follows; a song can also keep its own.",
            "mixer group rules groups instrument piano pad strings custom bass guitar drums other"));
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
            list[i] = new SettingDescriptor
            {
                Key = d.Key, Category = page, Group = group, Title = d.Title, Description = d.Description,
                Kind = d.Kind, Choices = d.Choices, Min = d.Min, Max = d.Max, Step = d.Step, Decimals = d.Decimals,
                Unit = d.Unit, DependsOn = d.DependsOn, DependsOnValue = d.DependsOnValue, DisabledNote = d.DisabledNote,
                HotkeyAction = d.HotkeyAction, Keywords = d.Keywords,
                Get = d.Get, Set = d.Set, More = place?.More ?? false, Order = place?.Order ?? int.MaxValue,
                SearchText = SearchTextOf(page, d.Title, d.Description, group, d.Key, oldName, KindWords(d.Kind), string.Join(' ', d.Keywords))
            };
        }
        return list;
    }

    private static SettingDescriptor Bool(string category, string group, string key, string title, Action<bool> set,
        Func<bool> get, string description, string keywords = "", string? hotkey = null, string? dependsOn = null,
        string? dependsOnValue = null, string? disabledNote = null) =>
        Make(category, group, key, title, description, SettingKind.Bool, () => get(), v => set(v is bool b && b),
            keywords, dependsOn, hotkey: hotkey, dependsOnValue: dependsOnValue, disabledNote: disabledNote);

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
        Func<string> get, IReadOnlyList<string> choices, string description, string keywords = "", string? dependsOn = null,
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
    private static SettingDescriptor Button(string category, string group, string key, string title, string description, string keywords,
        string? dependsOn = null, string? dependsOnValue = null) =>
        Make(category, group, key, title, description, SettingKind.Button, () => "", _ => { }, keywords, dependsOn, dependsOnValue: dependsOnValue);

    /// <summary>A list of folders (Add folder…, Remove in the Preferences window). Stored by the caller; the value is the folders joined by "; ".</summary>
    private static SettingDescriptor FolderList(string category, string group, string key, string title, Action<List<string>> set,
        Func<IReadOnlyList<string>> get, string description, string keywords = "") =>
        Make(category, group, key, title, description, SettingKind.FolderList, () => FolderListText.Join(get()),
            v => set(FolderListText.Parse(v?.ToString())), keywords);

    private static SettingDescriptor Colour(string category, string group, string key, string title, Action<string> set,
        Func<string> get, string description, string keywords = "", string? dependsOn = null,
        string? dependsOnValue = null) =>
        Make(category, group, key, title, description, SettingKind.Colour, () => get(),
            v => set(v?.ToString() ?? ""), keywords, dependsOn, dependsOnValue: dependsOnValue);

    private static SettingDescriptor Make(string category, string group, string key, string title, string description,
        SettingKind kind, Func<object?> get, Action<object?> set, string keywords, string? dependsOn = null,
        double min = 0, double max = 100, double step = 1, int decimals = 0, string unit = "",
        string? hotkey = null, IReadOnlyList<string>? choices = null, string? dependsOnValue = null, string? disabledNote = null) => new()
    {
        Key = key, Category = category, Group = group, Title = title, Description = description, Kind = kind,
        Get = get, Set = set, Keywords = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        DependsOn = dependsOn, DependsOnValue = dependsOnValue, DisabledNote = disabledNote, Min = min, Max = max, Step = step, Decimals = decimals, Unit = unit,
        HotkeyAction = hotkey, Choices = choices ?? Array.Empty<string>()
    };
}
