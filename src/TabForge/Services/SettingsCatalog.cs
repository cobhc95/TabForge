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

// Owns: the catalogue of settings descriptors used by search and the settings pages.
// Does not own: storing the values (AppSettings) and the page layout.
// Tests: TestTabUi, TestContextMenuLayouts.
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
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TypeInitializationException) // Not logged: device list probe for a settings row: an empty list is shown
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
        (General, "Updates", "general.checkupdates general.checknow"),
        (General, "Window", "general.restorewindow"),
        (General, "Confirmations", "general.confirmclose general.confirmdiscardsettings editing.confirmdelete editing.bardelete timeline.confirmdelete"),

        (Appearance, "Theme", "appearance.thememode"),
        (Appearance, "Size and text", "appearance.uiscale appearance.density appearance.font* appearance.fontsize* appearance.iconsize* appearance.toolbaricons*"),
        (Appearance, "Panels and toolbars", "general.toolbar general.statusbar appearance.tabstrip"),
        (Appearance, "Track colours", "appearance.tracktint appearance.mutedtrackdim mixer.grouprules appearance.groupcolour.guitars* appearance.groupcolour.basses* appearance.groupcolour.keys* appearance.groupcolour.drums* appearance.groupcolour.other* appearance.groupcolour.other-instruments*"),
        (Appearance, "Interface colours", "appearance.accent"),
        (Appearance, "Motion", "appearance.reduceanimations appearance.animationspeed* timeline.draganimation*"),
        (Appearance, "Custom palette", "appearance.background* appearance.panel* appearance.titlebar* appearance.tabactive* appearance.tabhover* appearance.text* appearance.muted*"),

        (Score, "What is shown", "score.defaultnotation score.pagelayout score.scrolling score.barnumbers score.sectionheadings score.dynamics score.barnumberfrequency* score.ledger*"),
        (Score, "Spacing and size", "appearance.spacing score.systemspacing* score.measurespacing*"),
        (Score, "Text and fonts", "appearance.scorefont appearance.scorefontsize score.textfonts appearance.scorebold* appearance.scoreitalic*"),
        (Score, "Appearance", "appearance.paper appearance.scorepaper.dark* appearance.scorepaper.light* appearance.scoreink.dark* appearance.scoreink.light* appearance.scorelines.dark* appearance.scorelines.light* appearance.cursor* score.staffopacity* appearance.selection* appearance.hover* score.hoverintensity* score.selectionintensity*"),

        (Fretboard, "Panel", "appearance.fretboard fretboard.position fretboard.instrumentview fretboard.showallas fretboard.dockposition fretboard.locksize"),
        (Fretboard, "Guitar fretboard", "editing.frets editing.lefthanded editing.notenames"),
        (Fretboard, "Keyboard", "fretboard.keyboardkeys fretboard.keyboardcolours*"),
        (Fretboard, "Practice aids", "editing.horizon.enabled editing.scale follow.fretboard editing.horizon* audio.fretboardstyle*"),
        (Fretboard, "Appearance", "fretboard.scalestyle* fretboard.scalecolour* fretboard.scalestrength* fretboard.markercolour* fretboard.markerbrightness* fretboard.numbersize* fretboard.markersize* fretboard.stringspacing*"),

        (Timeline, "Track list", "timeline.volumestyle timeline.panstyle timeline.autofit timeline.trackgroups* timeline.addtracklane*"),
        (Timeline, "Clip lanes", "timeline.removeemptylanes*"),
        (Timeline, "Timeline display", "appearance.arrangement timeline.numbers timeline.individualnotes timeline.continuousline* timeline.hideemptygrid* timeline.tracklines* timeline.barglow* timeline.playheadstyle* appearance.timelinescrollbar*"),
        (Timeline, "Sections", "timeline.similarcolours timeline.brackets timeline.names follow.sectionglow* appearance.sectionbracket*"),

        (Editing, "Note entry", "editing.duration editing.advance editing.reverseplusminus* editing.preventoverflow* editing.fillrests* editing.deleteleaves*"),
        (Editing, "Mouse and scrolling", "editing.scorewheel*"),
        (Editing, "Converting tracks", "editing.convertmidi"),
        (Editing, "Clips", "editing.trimemptybars"),
        (Editing, "Copy and paste", "editing.paste.beats* editing.paste.octave* editing.paste.bars* editing.paste.barsettings* editing.paste.drums*"),

        (Playback, "Scrolling and following", "follow.mode follow.horizontal follow.vertical follow.anticipation* follow.verticaltrigger* follow.margin* follow.stopmanual* follow.stopatend* follow.fps*"),
        (Playback, "Appearance", "follow.highlight follow.colour follow.playhead follow.playingbar follow.playingbar.colour follow.playingbar.opacity follow.playingbar.stopped* follow.bg* follow.duration.enabled* follow.durationglow* follow.durationopacity* follow.playhead.thickness*"),
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
        foreach (var (page, group, keys) in Features.FeatureRegistry.MergeLayout(Layout))
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
        ["timeline.continuousline"] = "timeline display continuous line", ["timeline.hideemptygrid"] = "timeline display hide grid", ["timeline.tracklines"] = "timeline display track lines separator",
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
        list.AddRange(Features.FeatureRegistry.SettingRows(s));
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
