using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Preferences catalogue integrity (docs/PREFERENCES_AUDIT.md 6.1): every row still has a home, findable, reachable by deep link.</summary>
public static partial class SelfTest
{
    // Every row key of the Preferences window before the reorganisation (docs/PREFERENCES_MIGRATION.txt). None may disappear.
    private const string OldPreferenceKeys =
        "general.checkupdates general.openfromexplorer general.autosave general.saveformat general.associate general.toolbar general.statusbar " +
        "general.confirmclose general.confirmdiscardsettings appearance.thememode appearance.uiscale appearance.tracktint appearance.density " +
        "appearance.reduceanimations appearance.animationspeed appearance.accent appearance.selection appearance.hover appearance.background " +
        "appearance.panel appearance.titlebar appearance.tabactive appearance.tabhover appearance.text appearance.muted appearance.timelinescrollbar " +
        "appearance.font appearance.fontsize appearance.iconsize appearance.toolbaricons score.defaultnotation appearance.scorefont " +
        "appearance.scorefontsize appearance.scorebold appearance.scoreitalic appearance.spacing score.systemspacing score.measurespacing score.ledger " +
        "score.staffopacity score.barnumbers score.barnumberfrequency score.sectionheadings score.dynamics score.hoverintensity " +
        "score.selectionintensity appearance.paper appearance.scorepaper.dark appearance.scorepaper.light appearance.scoreink.dark " +
        "appearance.scoreink.light appearance.scorelines.dark appearance.scorelines.light appearance.cursor follow.mode follow.horizontal " +
        "follow.vertical follow.anticipation follow.verticaltrigger follow.margin follow.stopmanual follow.stopatend follow.fps follow.highlight " +
        "follow.colour follow.bg follow.duration.enabled follow.durationglow follow.durationopacity follow.playhead follow.playhead.thickness " +
        "vst.driver vst.device vst.asio.out vst.asio.outlast vst.playall vst.autogm vst.autopitch vst.startuptracks vst.winmidilatency " +
        "vst.followvolume vst.asio.inputs vst.asio.infirst vst.asio.inlast vst.recordoffset vst.input vst.samplerate vst.buffer audio.linkedmedia " +
        "vst.folders vst.commonfolders vst.rememberscan vst.dock vst.ontop vst.scanstandard vst.isolate audio.metronome audio.countin " +
        "audio.countinbars audio.speed audio.preview audio.previewlen audio.letring audio.metroclick audio.metroaccent audio.metrovolume " +
        "audio.metroaccentvolume audio.metroclickvolume audio.metrodivision timeline.volumestyle timeline.panstyle timeline.autofit editing.duration " +
        "editing.advance editing.reverseplusminus editing.preventoverflow editing.confirmdelete editing.scorewheel timeline.similarcolours " +
        "timeline.brackets timeline.names appearance.arrangement timeline.numbers follow.sectionglow appearance.sectionbracket timeline.confirmdelete " +
        "timeline.draganimation appearance.fretboard fretboard.position editing.frets editing.lefthanded editing.notenames editing.horizon.enabled " +
        "fretboard.instrumentview fretboard.keyboardkeys fretboard.keyboardcolours fretboard.scalestyle fretboard.scalecolour fretboard.markercolour " +
        "fretboard.markerbrightness fretboard.numbersize fretboard.stringspacing audio.fretboardstyle editing.horizon editing.scale follow.fretboard " +
        "general.restorewindow appearance.tabstrip tabs.style tabs.width tabs.maxwidth tabs.minwidth tabs.height tabs.fontsize tabs.closebutton " +
        "tabs.doubleclick tabs.middleclick tabs.middlebar tabs.lastclosed tabs.newposition tabs.openincurrent tabs.playbackonswitch tabs.detach " +
        "tabs.merge tabs.playing editing.paste.beats editing.paste.octave editing.paste.bars editing.paste.barsettings editing.paste.drums " +
        // rows added for the menu options earlier the same night
        "score.pagelayout score.scrolling score.textfonts timeline.individualnotes timeline.continuousline timeline.hideemptygrid timeline.barglow " +
        "timeline.playheadstyle timeline.trackgroups fretboard.locksize fretboard.showallas appearance.groupcolour.guitars appearance.groupcolour.basses " +
        "appearance.groupcolour.keys appearance.groupcolour.drums appearance.groupcolour.other appearance.groupcolour.other-instruments";

    private static void TestPreferencesCatalog()
    {
        using var keepAlive = KeepAlive();   // closing the last test window must not shut the app down (later UI tests need it)
        var catalog = SettingsCatalog.Build(new AppSettings());
        var byKey = catalog.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
        var oldKeys = OldPreferenceKeys.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Check("preferences: no old row key was removed", oldKeys.All(byKey.ContainsKey), string.Join(", ", oldKeys.Where(k => !byKey.ContainsKey(k))));
        Check("preferences: every old row key still maps to a page and group",
            oldKeys.All(k => SettingsCatalog.PlaceOf(k) is { } p && SettingsCatalog.Categories.Contains(p.Page) && p.Group.Length > 0),
            string.Join(", ", oldKeys.Where(k => SettingsCatalog.PlaceOf(k) is null)));
        Check("preferences: every catalogue row has a place in the layout table (no orphans)",
            catalog.All(d => SettingsCatalog.PlaceOf(d.Key) is not null), string.Join(", ", catalog.Where(d => SettingsCatalog.PlaceOf(d.Key) is null).Select(d => d.Key)));
        Check("preferences: each row shows on the page the table names",
            catalog.All(d => SettingsCatalog.PlaceOf(d.Key) is { } p && p.Page == d.Category && p.Group == d.Group && p.More == d.More));
        Check("preferences: every row has a title and a description that is not the title",
            catalog.All(d => d.Title.Length > 0 && d.Description.Length > 0 && !d.Description.Equals(d.Title, StringComparison.OrdinalIgnoreCase)));
        Check("preferences: no two rows on a page share a title",
            catalog.GroupBy(d => d.Category).All(page => page.Select(d => d.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count() == page.Count()),
            string.Join(", ", catalog.GroupBy(d => d.Category + "/" + d.Title).Where(g => g.Count() > 1).Select(g => g.Key)));
        Check("preferences: no title appears on two pages (the old double 'Show bar numbers')",
            catalog.GroupBy(d => d.Title, StringComparer.OrdinalIgnoreCase).All(g => g.Select(d => d.Category).Distinct().Count() == 1),
            string.Join(", ", catalog.GroupBy(d => d.Title, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(d => d.Category).Distinct().Count() > 1).Select(g => g.Key)));

        // Colours live with their feature: no colour row sits on the Appearance page except the interface and custom-palette ones.
        Check("preferences: score, fretboard and playback colours sit on their own feature pages",
            byKey["appearance.paper"].Category == SettingsCatalog.Score && byKey["appearance.selection"].Category == SettingsCatalog.Score &&
            byKey["fretboard.scalecolour"].Category == SettingsCatalog.Fretboard && byKey["follow.colour"].Category == SettingsCatalog.Playback &&
            byKey["appearance.timelinescrollbar"].Category == SettingsCatalog.Timeline);
        Check("preferences: a feature page has an 'Appearance' group for its looks",
            new[] { SettingsCatalog.Score, SettingsCatalog.Fretboard, SettingsCatalog.Playback }
                .All(page => catalog.Any(d => d.Category == page && d.Group == "Appearance")));
        Check("preferences: the Basic rows of a page stay few (About a dozen per page at most)",
            catalog.GroupBy(d => d.Category).All(page => page.Count(d => !d.More) <= 16),
            string.Join(", ", catalog.GroupBy(d => d.Category).Select(g => $"{g.Key}={g.Count(d => !d.More)}")));

        // Pages: 14 in four bands plus the home page; ids and old names resolve; unknown ids do not.
        Check("preferences: 14 pages in the rail, Common settings first, Shortcuts and Audio & Plug-ins named as decided",
            SettingsCatalog.Categories.Count == 14 && SettingsCatalog.Categories[0] == "Common settings" &&
            SettingsCatalog.Categories.Contains("Shortcuts") && SettingsCatalog.Categories.Contains("Audio & Plug-ins") &&
            SettingsCatalog.Bands.SelectMany(b => b.Pages).SequenceEqual(SettingsCatalog.Categories));
        Check("preferences: page ids and old page names resolve to the new pages",
            SettingsCatalog.ResolvePage("audio") == SettingsCatalog.AudioVst && SettingsCatalog.ResolvePage("Audio & VST") == SettingsCatalog.AudioVst &&
            SettingsCatalog.ResolvePage("Hotkeys") == SettingsCatalog.Hotkeys && SettingsCatalog.ResolvePage("Appearance & colours") == SettingsCatalog.Appearance &&
            SettingsCatalog.ResolvePage("Timeline & sections") == SettingsCatalog.Timeline && SettingsCatalog.ResolvePage("no such page") is null);

        // Search: a row's own title, tokenised AND, synonyms and the old menu names.
        Check("preferences: searching a row's own title finds it", catalog.All(d => SettingsCatalog.Matches(d, d.Title.ToLowerInvariant())),
            string.Join(", ", catalog.Where(d => !SettingsCatalog.Matches(d, d.Title.ToLowerInvariant())).Select(d => d.Key)));
        bool Finds(string query, string key) => SettingsCatalog.Matches(byKey[key], query);
        Check("preferences: 'dark theme' finds the Theme row (every word, any order)", Finds("dark theme", "appearance.thememode") && Finds("theme dark", "appearance.thememode"));
        Check("preferences: a query whose words are not all present finds nothing", !Finds("dark xylophone", "appearance.thememode"));
        Check("preferences: synonyms find the same row",
            Finds("skin", "appearance.thememode") && Finds("soundcard", "vst.device") && Finds("speakers", "vst.device") && Finds("microphone", "vst.input") &&
            Finds("lag", "vst.buffer") && Finds("measure", "score.barnumbers") && Finds("backup", "general.autosave") && Finds("typeface", "appearance.font") &&
            Finds("click", "audio.metronome") && Finds("neck", "editing.frets"));
        Check("preferences: the old menu names still find their rows",
            Finds("preview layout", "audio.fretboardstyle") && Finds("preview next notes", "editing.horizon.enabled") && Finds("number size", "fretboard.numbersize") &&
            Finds("dark page", "appearance.paper") && Finds("page turn", "follow.mode") && Finds("subtle bar glow", "timeline.barglow") &&
            Finds("timeline display", "timeline.individualnotes") && Finds("follow the playhead", "follow.mode") && Finds("density", "appearance.density"));

        // Reachability: a window deep-linked to a row opens on its page and builds every row of that page (collapsed ones included).
        var failures = new List<string>();
        foreach (var page in SettingsCatalog.Categories.Where(p => catalog.Any(d => d.Category == p)))
        {
            var rows = catalog.Where(d => d.Category == page).ToList();
            var last = rows.OrderByDescending(d => d.Order).First();
            PreferencesWindow.SetTarget(page, last.Key);
            var window = new PreferencesWindow(new AppSettings());
            var built = window.BuiltRowKeys;
            if (window.SelectedCategory != page) failures.Add($"{page}: opened {window.SelectedCategory}");
            foreach (var row in rows.Where(r => !built.Contains(r.Key, StringComparer.OrdinalIgnoreCase))) failures.Add($"{page}: {row.Key} not built");
            window.Close();
        }
        Check("preferences: every row is reachable on its page through a deep link", failures.Count == 0, string.Join("; ", failures));
        PreferencesWindow.SetTarget("audio");
        var audio = new PreferencesWindow(new AppSettings());
        Check("preferences: a deep link by page id opens that page", audio.SelectedCategory == SettingsCatalog.AudioVst, audio.SelectedCategory);
        audio.Close();
        PreferencesWindow.SetTarget("Audio & VST");
        var oldName = new PreferencesWindow(new AppSettings());
        Check("preferences: a deep link by the old page name still opens the page", oldName.SelectedCategory == SettingsCatalog.AudioVst, oldName.SelectedCategory);
        oldName.Close();
        PreferencesWindow.SetTarget("nonsense");
        var unknown = new PreferencesWindow(new AppSettings());
        Check("preferences: an unknown page id opens Common settings", unknown.SelectedCategory == SettingsCatalog.Home, unknown.SelectedCategory);
        unknown.Close();
        PreferencesWindow.SetTarget(SettingsCatalog.Home);
        var home = new PreferencesWindow(new AppSettings());
        var homeKeys = home.BuiltRowKeys;
        Check("preferences: Common settings shows the everyday rows as references to the real ones",
            new[] { "appearance.thememode", "vst.driver", "audio.metronome", "score.defaultnotation", "general.autosave" }.All(homeKeys.Contains));
        home.Close();
    }
}
