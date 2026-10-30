using TabForge.Models;
// Owns: the plain main-menu rows as data: the top-level menu each group goes in, the skeleton item it follows, each row's caption
//   (with the access-key underscore the menu shows), its command id (the hotkey id of HotkeyCatalog) and its tag, the separators and the submenus (a caption over its own rows).
// Does not own: the click handlers (MainWindow.Menus.cs binds each command id to one), the menu skeleton (MainWindow.xaml: the
// menus and the Panels item the dock layouts fill), or the app (Views/MainMenu/MenuTable.cs) and the Avalonia build (MainWindowMenu.cs).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests.
namespace TabForge.Presenters;

using static TabForge.Presenters.MainMenuRow;

/// <summary>One main-menu row: a caption and the command it runs, a submenu (a caption and its rows), or a separator (a null caption). A toggle is a checkable row;
/// <see cref="CheckedAtStart"/> is its state before the window's settings apply.</summary>
public sealed record MainMenuRow(string? Header, string? CommandId = null, string? Tag = null, bool IsCheckable = false, bool CheckedAtStart = false, IReadOnlyList<MainMenuRow>? Children = null)
{
    public static readonly MainMenuRow Separator = new((string?)null);

    public bool IsSeparator => Header is null;

    /// <summary>True for a row that opens a submenu: it has a caption and <see cref="Children"/> but runs no command.</summary>
    public bool IsSubmenu => Children is not null;

    public static MainMenuRow Submenu(string header, params MainMenuRow[] children) => new(header, null, null, false, false, children);

    public static MainMenuRow Item(string header, string commandId, string? tag = null) => new(header, commandId, tag);

    /// <summary>A row of a mutually exclusive group: checkable, and its check follows the live value (MainWindowCommands.AddCheckState).</summary>
    public static MainMenuRow Choice(string header, string commandId) => new(header, commandId, null, true, false);

    public static MainMenuRow Toggle(string header, string commandId, bool checkedAtStart = false) => new(header, commandId, null, true, checkedAtStart);
}

/// <summary>A run of rows inserted into the top-level menu <see cref="Menu"/> right after the skeleton item named <see cref="After"/> (at the start when null).</summary>
public sealed record MainMenuGroup(string Menu, string? After, params MainMenuRow[] Rows);

/// <summary>A row a feature module adds: it goes right after the row of command <see cref="AfterId"/> in menu <see cref="Menu"/>.</summary>
public sealed record MainMenuFeatureRow(string Menu, string AfterId, string Header, string CommandId);

/// <summary>
/// The main-menu rows in menu order. A static table, built once at type load and never written, so any thread may read it.
/// Each command id appears once; the window's tests check it on the built menu.
/// </summary>
public static class MainMenuTable
{
    public static IReadOnlyList<MainMenuGroup> Groups { get; } = new MainMenuGroup[]
    {
        new("_File", null,
            Item("_New", "File.New"),
            Item("New from _template…", "File.NewFromTemplate"),
            Item("New ta_b", "Tab.New"),
            Separator,
            Item("_Open…", "File.Open"),
            Item("Open in ne_w tab…", "File.OpenInNewTab"),
            Separator,
            Item("_Save", "File.Save"),
            Item("Save _As…", "File.SaveAs"),
            Item("Save as temp_late…", "File.SaveAsTemplate"),
            Separator,
            Item("_Close tab", "Tab.Close"),
            Separator,
            Submenu("_Export",
                Item("_MIDI…", "File.ExportMidi"),
                Item("_Video (MP4)…", "File.ExportVideo"),
                Item("_Audio file…", "File.Render"),
                Item("ASCII _tab…", "File.ExportAscii"),
                Item("_Compatible .gp file…", "File.ExportGuitarPro"),
                Item(".gp_5 file (older programs)…", "File.ExportGp5"),
                Item(".gp_4 file (older programs)…", "File.ExportGp4"),
                Item(".gp_3 file (older programs)…", "File.ExportGp3"),
                Item("_PDF…", "File.ExportPdf"),
                Item("Music_XML…", "File.ExportMusicXml")),
            Item("Record _video", "Transport.RecordVideo"),
            Separator,
            Item("Collect _media…", "File.CollectMedia"),
            Item("_Find missing media…", "File.FindMissingMedia"),
            Separator,
            Item("Score _information…", "Bar.ScoreInfo"),
            Item("Print _preview…", "File.PrintPreview"),
            Item("P_rint…", "File.Print"),
            Separator,
            Item("E_xit", "File.Exit")),
        new("_Edit", null,
            Item("_Undo", "Edit.Undo"),
            Item("_Redo", "Edit.Redo"),
            Separator,
            Item("_Copy", "Edit.Copy"),
            Item("Cu_t", "Edit.Cut"),
            Item("_Paste", "Edit.Paste"),
            Item("Paste _special…", "Edit.PasteSpecial"),
            Separator,
            Item("Select _all", "Edit.SelectAll"),
            Item("Invert selection", "Edit.InvertSelection"),
            Separator,
            Item("Repeat selection as _bars", "Edit.RepeatSelection"),
            Item("_Delete beats", "Edit.DeleteBeats"),
            Item("Copy _last beat", "Note.RepeatBeat")),
        new("_Insert", null,
            Submenu("_Track",
                Item("_Tab track…", "Track.Add"),
                Item("_Bass", "Track.AddBass"),
                Item("_Drums", "Track.AddDrums"),
                Item("_Keys", "Track.AddKeys"),
                Item("_Audio / MIDI track", "Track.AddAudio")),
            Submenu("_Bar",
                Item("_Before selected", "Bar.Insert"),
                Item("_After selected", "Bar.InsertAfter"),
                Item("_Several bars…", "Bar.InsertSeveral"),
                Item("At _end", "Bar.Append"),
                Item("Bea_t", "Edit.InsertBeat")),
            Item("Se_ction…", "Section.Add"),
            Separator,
            Item("Te_mpo change…", "Tool.composition:tempo"),
            Item("Time _signature change…", "Bar.TimeSignature"),
            Item("_Key signature change…", "Bar.KeySignature"),
            Submenu("_Repeat / ending",
                Item("Repeat _open", "Bar.RepeatOpen"),
                Item("Repeat _close…", "Bar.RepeatClose"),
                Item("_Directions / ending…", "Bar.Directions"),
                Item("Double _bar", "Bar.DoubleBar"),
                Item("Repeat one b_ar", "Bar.RepeatOneBar"),
                Item("Repeat _two bars", "Bar.RepeatTwoBars")),
            Separator,
            Item("C_hord…", "Note.Chord"),
            Item("Te_xt…", "Note.Text"),
            Item("_Lyrics…", "Insert.Lyrics"),
            Separator,
            Item("_Audio file…", "Insert.AudioFile"),
            Item("MIDI _file…", "Insert.MidiFile"),
            Item("_Empty MIDI clip", "Insert.EmptyMidiClip")),
        new("_Bar", null,
            Item("_Delete bar", "Bar.Delete"),
            Item("D_uplicate bar", "Bar.Duplicate"),
            Item("Cl_ear bar", "Bar.EmptyBar"),
            Separator,
            Item("Cle_f", "Bar.Clef"),
            Item("_Triplet feel", "Bar.TripletFeel"),
            Separator,
            Item("Fi_rst bar", "Bar.First"),
            Item("_Last bar", "Bar.Last"),
            Item("_Go to…", "Bar.GoTo"),
            Separator,
            Item("_Previous section", "Section.Previous"),
            Item("_Next section", "Section.Next"),
            Separator,
            Item("_Section…", "Section.Edit"),
            Item("Chec_k bar duration", "Bar.Check")),
        new("_Track", null,
            Item("_Delete track", "Track.Delete"),
            Item("Move _up", "Track.MoveUp"),
            Item("Move do_wn", "Track.MoveDown"),
            Separator,
            Item("_Properties…", "Track.Properties"),
            Separator,
            Item("Test selected track _output", "Sound.TestOutput")),
        new("_Note", null,
            Item("Whole", "Note.Whole"),
            Item("Half", "Note.Half"),
            Item("Quarter", "Note.Quarter"),
            Item("Eighth", "Note.Eighth"),
            Item("Sixteenth", "Note.Sixteenth"),
            Item("32nd", "Note.ThirtySecond"),
            Item("64th", "Note.SixtyFourth"),
            Separator,
            Item("Longer note value", "Note.Longer"),
            Item("Shorter note value", "Note.Shorter"),
            Item("Dotting", "Note.Dot"),
            Item("Double dotting", "Note.DoubleDot"),
            Item("Triplet", "Note.Triplet"),
            Separator,
            Item("Tie note", "Note.Tie"),
            Item("Rest", "Note.Rest"),
            Item("Fermata", "Note.Fermata"),
            Item("Accent", "Note.Accent"),
            Item("Staccato", "Note.Staccato"),
            Item("Tenuto", "Note.Tenuto"),
            Separator,
            Item("Shift pitch up (semitone)", "Note.PitchUp"),
            Item("Shift pitch down (semitone)", "Note.PitchDown"),
            Item("Move to higher string", "Note.MoveStringUp"),
            Item("Move to lower string", "Note.MoveStringDown")),
        new("_Effects", null,
            Item("Dead note", "Note.Dead"),
            Item("Ghost note", "Note.Ghost"),
            Item("Palm mute", "Note.PalmMute"),
            Item("Let ring", "Note.LetRing"),
            Item("Hammer / pull", "Note.HammerPull"),
            Separator,
            Submenu("Bend", Item("Bend", "Note.Bend"), Item("Bend editor…", "Note.BendEditor", "Bend")),
            Submenu("Slide", Item("Legato slide", "Note.Slide"), Item("Shift slide", "Note.ShiftSlide")),
            Submenu("Vibrato", Item("Vibrato", "Note.Vibrato"), Item("Wide vibrato", "Note.WideVibrato"), Item("Vibrato settings…", "Note.VibratoSettings")),
            Submenu("Tremolo bar", Item("Tremolo bar", "Note.TremoloBar"), Item("Tremolo bar editor…", "Note.TremoloBarEditor", "TremoloBar")),
            Submenu("Harmonic", Item("Natural harmonic", "Note.Harmonic"), Item("Artificial harmonic", "Note.ArtificialHarmonic"), Item("Harmonic editor…", "Note.HarmonicEditor", "Harmonic")),
            Submenu("Trill", Item("Trill", "Note.Trill"), Item("Trill editor…", "Note.TrillEditor", "Trill")),
            Submenu("Grace", Item("Grace before beat", "Note.Grace"), Item("Grace note editor…", "Note.GraceEditor", "Grace")),
            Separator,
            Item("Tapping", "Note.Tapping"),
            Item("Slap", "Note.Slap"),
            Item("Pop", "Note.Pop"),
            Item("Tremolo picking", "Note.TremoloPicking"),
            Separator,
            Submenu("Fade", Item("Fade in", "Note.FadeIn"), Item("Fade out", "Note.FadeOut")),
            Submenu("Wah", Item("Wah open", "Note.WahOpen"), Item("Wah close", "Note.WahClose")),
            Submenu("Brush", Item("Brush down", "Note.BrushDown"), Item("Brush up", "Note.BrushUp")),
            Submenu("Arpeggio", Item("Arpeggio down", "Note.ArpeggioDown"), Item("Arpeggio up", "Note.ArpeggioUp"))),
        new("_View", null,
            Choice(NotationNames.Display(NotationMode.TabAndStaff), "View.NotationBoth"),
            Choice(NotationNames.Display(NotationMode.TabOnly), "View.NotationTab"),
            Choice(NotationNames.Display(NotationMode.StaffOnly), "View.NotationStaff"),
            Separator,
            Submenu("Score page", Choice("Dark", "View.ScoreThemeDark"), Choice("Light", "View.ScoreThemeLight")),
            Separator,
            Toggle("Instrument view", "View.InstrumentPanel", checkedAtStart: true),
            Toggle("Arrangement overview", "View.Global", checkedAtStart: true),
            Toggle("Band view", "View.BandView"),
            Item("Keyboard mode (experimental)", "View.LearnMode"),
            Item("Keyboard mode: view in its own window", "View.LearnPopout"),
            Item("Compact mode", "View.CompactMode"),
            Item("Mixer / VST", "View.Mixer"),
            Separator,
            Submenu("Arrangement lanes",
                Toggle("Show the notes in each track lane", "View.ArrangementIndividualNotes"),
                Toggle("Show one continuous line per track lane", "View.ArrangementContinuousBlocks")),
            Toggle("Highlight playing bar", "View.PlayingBar"),
            Separator),
        new("_View", "DockPanelsMenu",
            Item("Reset all panels to default positions", "View.ResetPanels"),
            Separator,
            Submenu("Zoom",
                Item("Zoom in", "View.ZoomIn"),
                Item("Zoom out", "View.ZoomOut"),
                Choice("75%", "View.Zoom75"),
                Choice("100%", "View.Zoom100"),
                Choice("150%", "View.Zoom150")),
            Toggle("Full screen", "View.Fullscreen")),
        new("_Tools", null,
            Item("Transpose…", "Tools.Transpose"),
            Item("Scale finder…", "Tools.ScaleFinder"),
            Item("Chord finder…", "Tools.ChordFinder"),
            Item("Song stats…", "Tools.SongStats"),
            Item("Tuner…", "Tools.Tuner"),
            Item("Global tuning…", "Tools.GlobalTuning"),
            Item("Preset manager…", "Tools.PresetManager"),
            Separator,
            Item("Panic (all notes off)", "Transport.Panic")),
        new("_Options", null,
            Item("Preferences…", "App.Preferences"),
            Item("MIDI / Audio setup", "Sound.AudioSetup"),
            Toggle("Preview note sound", "Tools.PreviewNotes", checkedAtStart: true)),
        new("_Help", null,
            Item("Keyboard shortcuts…", "App.Shortcuts"),
            Item("Command palette…", "App.CommandPalette"),
            Item("Check for updates…", "Help.CheckForUpdates"),
            Item("About", "Help.About")),
        // <groups>
    };

    /// <summary>The rows a feature module adds: none today (the feature rows sit in their menus above). A row listed here goes right after the top-level row of its anchor command.</summary>
    public static IReadOnlyList<MainMenuFeatureRow> FeatureRows { get; } = new MainMenuFeatureRow[] { };

    /// <summary>Every row that runs a command, in menu order, at any submenu depth (separators and the submenu rows themselves left out).</summary>
    public static IEnumerable<MainMenuRow> CommandRows(IEnumerable<MainMenuGroup> groups) => groups.SelectMany(g => g.Rows).SelectMany(Leaves);

    private static IEnumerable<MainMenuRow> Leaves(MainMenuRow row) =>
        row.IsSubmenu ? row.Children!.SelectMany(Leaves) : row.IsSeparator ? Enumerable.Empty<MainMenuRow>() : new[] { row };

    /// <summary>The groups with each feature row placed right after the row of its anchor command id, in the menu it names. An anchor no row holds throws.</summary>
    public static IReadOnlyList<MainMenuGroup> WithFeatureRows()
    {
        var merged = Groups.Select(g => new MainMenuGroup(g.Menu, g.After, g.Rows.ToArray())).ToList();
        foreach (var feature in FeatureRows)
        {
            var group = merged.FindIndex(g => g.Menu == feature.Menu && Array.Exists(g.Rows, r => r.CommandId == feature.AfterId));
            if (group < 0) throw new InvalidOperationException($"Feature menu row '{feature.CommandId}': anchor '{feature.AfterId}' not found in menu '{feature.Menu}'.");
            var rows = merged[group].Rows.ToList();
            rows.Insert(rows.FindIndex(r => r.CommandId == feature.AfterId) + 1, MainMenuRow.Item(feature.Header, feature.CommandId));
            merged[group] = merged[group] with { Rows = rows.ToArray() };
        }
        return merged;
    }
}
