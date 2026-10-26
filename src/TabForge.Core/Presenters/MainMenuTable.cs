using TabForge.Models;
// Owns: the plain main-menu rows as data: the top-level menu each group goes in, the skeleton item it follows, each row's caption
//   (with the access-key underscore the menu shows), its command id (the hotkey id of HotkeyCatalog) and its tag, and the separators.
// Does not own: the click handlers (MainWindow.Menus.cs binds each command id to one), the menu skeleton (MainWindow.xaml: the
// menus and the Panels item the dock layouts fill), or the app (Views/MainMenu/MenuTable.cs) and the Avalonia build (MainWindowMenu.cs).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests.
namespace TabForge.Presenters;

using static TabForge.Presenters.MainMenuRow;

/// <summary>One main-menu row: a caption and the command it runs, or a separator (a null caption). A toggle is a checkable row;
/// <see cref="CheckedAtStart"/> is its state before the window's settings apply.</summary>
public sealed record MainMenuRow(string? Header, string? CommandId = null, string? Tag = null, bool IsCheckable = false, bool CheckedAtStart = false)
{
    public static readonly MainMenuRow Separator = new((string?)null);

    public bool IsSeparator => Header is null;

    public static MainMenuRow Item(string header, string commandId, string? tag = null) => new(header, commandId, tag);

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
        new("_Help", null,
            Item("Tutorial…", "Help.Tutorial"),
            Item("Detailed guide…", "Help.TutorialDetailed"),
            Item("Keyboard shortcuts…", "App.Shortcuts"),
            Item("Check for updates…", "Help.CheckForUpdates"),
            Item("About", "Help.About")),
        new("_Options", null,
            Item("Stylesheet…", "Options.Stylesheet"),
            Item("Preferences…", "App.Preferences")),
        new("_Sound", null,
            Item("Play / pause", "Transport.PlayPause"),
            Item("Play from beginning", "Transport.PlayFromStart"),
            Item("First bar", "Bar.First"),
            Item("Last bar", "Bar.Last"),
            Separator,
            Item("Empty bar", "Bar.EmptyBar"),
            Item("MIDI / Audio setup", "Sound.AudioSetup"),
            Item("Test selected track output", "Sound.TestOutput")),
        new("_Sections", null,
            Item("Add section…", "Section.Add"),
            Item("Go to…", "Bar.GoTo"),
            Item("Previous section", "Section.Previous"),
            Item("Next section", "Section.Next")),
        new("_Track", null,
            Item("Add _track…", "Track.Add"),
            Item("Add _Bass", "Track.AddBass"),
            Item("Add _Drums", "Track.AddDrums"),
            Item("Add _Keys", "Track.AddKeys"),
            Separator,
            Item("_Delete track", "Track.Delete"),
            Item("Move _up", "Track.MoveUp"),
            Item("Move _down", "Track.MoveDown"),
            Separator,
            Item("_Properties…", "Track.Properties")),
        new("_Edit", null,
            Item("_Undo", "Edit.Undo"),
            Item("_Redo", "Edit.Redo"),
            Separator,
            Item("_Copy", "Edit.Copy"),
            Item("Cu_t", "Edit.Cut"),
            Item("_Paste", "Edit.Paste"),
            Item("Paste _special…", "Edit.PasteSpecial"),
            Separator,
            Item("_Insert beat", "Edit.InsertBeat"),
            Item("_Delete beats", "Edit.DeleteBeats"),
            Item("_Copy last beat", "Note.RepeatBeat")),
        new("_Bar", null,
            Item("Insert _bar", "Bar.Insert"),
            Item("_Delete bar", "Bar.Delete"),
            Item("Duplicate bar", "Bar.Duplicate"),
            Item("Repeat selection as bars", "Edit.RepeatSelection"),
            Separator,
            Item("_Time signature…", "Bar.TimeSignature"),
            Item("_Key signature…", "Bar.KeySignature"),
            Item("_Clef", "Bar.Clef"),
            Item("Triplet _feel", "Bar.TripletFeel"),
            Separator,
            Item("Repeat _open", "Bar.RepeatOpen"),
            Item("Repeat _close…", "Bar.RepeatClose"),
            Item("_Directions / ending…", "Bar.Directions"),
            Item("_Double bar", "Bar.DoubleBar"),
            Item("Repeat one _bar", "Bar.RepeatOneBar"),
            Item("Repeat two b_ars", "Bar.RepeatTwoBars"),
            Separator,
            Item("_Section…", "Section.Edit")),
        new("_Tools", null,
            Item("Check bar duration", "Bar.Check"),
            Item("Transpose…", "Tools.Transpose"),
            Item("Scale finder…", "Tools.ScaleFinder"),
            Item("Chord finder…", "Tools.ChordFinder"),
            Item("Song stats…", "Tools.SongStats"),
            Item("Tuner…", "Tools.Tuner"),
            Item("Global tuning…", "Tools.GlobalTuning"),
            Separator,
            Toggle("Metronome", "Transport.Metronome"),
            Toggle("Count-in", "Transport.CountIn"),
            Toggle("Preview note sound", "Tools.PreviewNotes", checkedAtStart: true)),
        new("_View", null,
            Item(NotationNames.Display(NotationMode.TabAndStaff), "View.NotationBoth"),
            Item(NotationNames.Display(NotationMode.TabOnly), "View.NotationTab"),
            Item(NotationNames.Display(NotationMode.StaffOnly), "View.NotationStaff"),
            Separator,
            Item("Dark score page", "View.ScoreThemeDark"),
            Item("Light score page", "View.ScoreThemeLight"),
            Separator,
            Toggle("Instrument view", "View.InstrumentPanel", checkedAtStart: true),
            Toggle("Arrangement overview", "View.Global", checkedAtStart: true),
            Toggle("Band view", "View.BandView"),
            Toggle("Show the notes in each track lane", "View.ArrangementIndividualNotes"),
            Toggle("Show one continuous line per track lane", "View.ArrangementContinuousBlocks"),
            Toggle("Highlight playing bar", "View.PlayingBar"),
            Item("Mixer / VST", "View.Mixer"),
            Separator),
        new("_View", "DockPanelsMenu",
            Item("Reset all panels to default positions", "View.ResetPanels"),
            Separator,
            Item("Zoom 75%", "View.Zoom75"),
            Item("Zoom 100%", "View.Zoom100"),
            Item("Zoom 150%", "View.Zoom150"),
            Item("Full screen", "View.Fullscreen")),
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
            Item("Bend", "Note.Bend"),
            Item("Bend editor…", "Note.BendEditor", "Bend"),
            Item("Legato slide", "Note.Slide"),
            Item("Shift slide", "Note.ShiftSlide"),
            Item("Vibrato", "Note.Vibrato"),
            Item("Wide vibrato", "Note.WideVibrato"),
            Item("Tremolo bar", "Note.TremoloBar"),
            Item("Tremolo bar editor…", "Note.TremoloBarEditor", "TremoloBar"),
            Item("Natural harmonic", "Note.Harmonic"),
            Item("Harmonic editor…", "Note.HarmonicEditor", "Harmonic"),
            Item("Trill editor…", "Note.TrillEditor", "Trill"),
            Item("Grace note editor…", "Note.GraceEditor", "Grace"),
            Item("Artificial harmonic", "Note.ArtificialHarmonic"),
            Item("Tapping", "Note.Tapping"),
            Item("Slap", "Note.Slap"),
            Item("Pop", "Note.Pop"),
            Item("Trill", "Note.Trill"),
            Item("Tremolo picking", "Note.TremoloPicking"),
            Item("Fade in", "Note.FadeIn"),
            Item("Fade out", "Note.FadeOut"),
            Item("Wah open", "Note.WahOpen"),
            Item("Wah close", "Note.WahClose"),
            Item("Brush down", "Note.BrushDown"),
            Item("Brush up", "Note.BrushUp"),
            Item("Arpeggio down", "Note.ArpeggioDown"),
            Item("Arpeggio up", "Note.ArpeggioUp"),
            Item("Grace before beat", "Note.Grace"),
            Separator,
            Item("Chord…", "Note.Chord"),
            Item("Text…", "Note.Text")),
        new("_File", null,
            Item("_New", "File.New"),
            Item("New from t_emplate…", "File.NewFromTemplate"),
            Item("New _tab", "Tab.New"),
            Item("_Open…", "File.Open"),
            Item("Open in new _tab…", "File.OpenInNewTab"),
            Item("_Save", "File.Save"),
            Item("Save _As…", "File.SaveAs"),
            Item("_Close tab", "Tab.Close"),
            Separator,
            Item("Export _MIDI…", "File.ExportMidi"),
            Item("_Render to audio file…", "File.Render"),
            Item("Command pa_lette…", "App.CommandPalette"),
            Separator,
            Item("Score _information…", "Bar.ScoreInfo"),
            Item("Print _preview…", "File.PrintPreview"),
            Item("_Print…", "File.Print"),
            Separator,
            Item("E_xit", "File.Exit")),
        // <groups>
    };

    /// <summary>The rows the feature modules add, in the order they are placed: a row whose anchor is another feature row comes after that row.</summary>
    public static IReadOnlyList<MainMenuFeatureRow> FeatureRows { get; } = new MainMenuFeatureRow[]
    {
        new("_Tools", "Tools.Tuner", "Preset manager…", "Tools.PresetManager"),
        new("_File", "File.ExportMidi", "Export _video (MP4)…", "File.ExportVideo"),
        new("_Sound", "Transport.PlayFromStart", "Record _video", "Transport.RecordVideo"),
        new("_File", "File.SaveAs", "Save as temp_late…", "File.SaveAsTemplate"),
        new("_File", "File.SaveAsTemplate", "Collect media…", "File.CollectMedia"),
        new("_File", "File.CollectMedia", "Find missing media…", "File.FindMissingMedia"),
        new("_File", "File.Render", "Export _ASCII tab…", "File.ExportAscii"),
        new("_File", "File.ExportAscii", "Export compatible .gp file…", "File.ExportGuitarPro"),
        new("_File", "File.ExportGuitarPro", "Export P_DF…", "File.ExportPdf"),
        new("_File", "File.ExportPdf", "Export Music_XML…", "File.ExportMusicXml"),
        new("_View", "View.Mixer", "Keyboard mode (experimental)", "View.LearnMode"),
        new("_View", "View.LearnMode", "Keyboard mode: view in its own window", "View.LearnPopout"),
        new("_View", "View.LearnPopout", "Compact mode", "View.CompactMode"),
    };

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
