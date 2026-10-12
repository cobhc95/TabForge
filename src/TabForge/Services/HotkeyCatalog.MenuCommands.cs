// Owns: the catalogue rows for main-menu commands that had no id, so each can be given a key in Preferences > Shortcuts.
// Does not own: the menu rows and their handlers (MainWindow.Menus.cs), the registration of rows as commands (Views/MainMenu/MenuRowCommands.cs).
// Tests: TestAddTrackKeys.
namespace TabForge.Services;

public static partial class HotkeyCatalog
{
    // Every row here has no default key: a key is set only by the user, so no existing preset changes.
    private static HotkeyAction[] SliceMenu() => new HotkeyAction[]
    {
        new("Help.About", CategoryView, "About", "", "Show the About window with the version and the licence."),
        new("Options.Stylesheet", CategoryView, "Stylesheet", "", "Open the Preferences window to the stylesheet settings."),
        new("Bar.EmptyBar", CategoryBar, "Empty bar", "", "Remove every note from the bar at the cursor."),
        new("Sound.AudioSetup", CategoryTransport, "MIDI / Audio setup", "", "Open the MIDI and audio setup page of the settings."),
        new("Sound.TestOutput", CategoryTransport, "Test selected track output", "", "Play a short test sound through the output of the selected track."),
        new("Track.AddBass", CategoryNote, "Add Bass", "", "Add a bass track."),
        new("Track.AddDrums", CategoryNote, "Add Drums", "", "Add a drum track."),
        new("Track.AddKeys", CategoryNote, "Add Keys", "", "Add a keyboard track."),
        new("Bar.Duplicate", CategoryBar, "Duplicate bar", "", "Copy the bar at the cursor into the bar after it."),
        new("Bar.TripletFeel", CategoryBar, "Triplet feel", "", "Toggle the triplet feel of the bar."),
        new("Bar.DoubleBar", CategoryBar, "Double bar", "", "Toggle a double bar line at the end of the bar."),
        new("Bar.RepeatOneBar", CategoryBar, "Repeat one bar", "", "Mark the bar as a repeat of the bar before it (one-bar simile)."),
        new("Bar.RepeatTwoBars", CategoryBar, "Repeat two bars", "", "Mark the bar as a repeat of the two bars before it (two-bar simile)."),
        new("View.NotationBoth", CategoryView, "Tablature + standard", "", "Show both the tablature and the standard notation."),
        new("View.NotationTab", CategoryView, "Tablature only", "", "Show only the tablature."),
        new("View.NotationStaff", CategoryView, "Standard notation only", "", "Show only the standard notation."),
        new("View.ScoreThemeDark", CategoryView, "Dark score page", "", "Draw the score page in dark colours."),
        new("View.ScoreThemeLight", CategoryView, "Light score page", "", "Draw the score page in light colours."),
        new("View.ResetPanels", CategoryView, "Reset all panels to default positions", "", "Put every dockable panel back at its default position."),
        new("View.Zoom75", CategoryView, "Zoom 75%", "", "Set the score zoom to 75 %."),
        new("View.Zoom100", CategoryView, "Zoom 100%", "", "Set the score zoom to 100 %."),
        new("View.Zoom150", CategoryView, "Zoom 150%", "", "Set the score zoom to 150 %."),
        new("Note.Whole", CategoryDuration, "Whole", "", "Set the note value to a whole note."),
        new("Note.Half", CategoryDuration, "Half", "", "Set the note value to a half note."),
        new("Note.Quarter", CategoryDuration, "Quarter", "", "Set the note value to a quarter note."),
        new("Note.Eighth", CategoryDuration, "Eighth", "", "Set the note value to an eighth note."),
        new("Note.Sixteenth", CategoryDuration, "Sixteenth", "", "Set the note value to a sixteenth note."),
        new("Note.ThirtySecond", CategoryDuration, "32nd", "", "Set the note value to a 32nd note."),
        new("Note.SixtyFourth", CategoryDuration, "64th", "", "Set the note value to a 64th note."),
        new("Note.ShiftSlide", CategoryEffects, "Shift slide", "", "Shift slide on the selected note."),
        new("Note.WideVibrato", CategoryEffects, "Wide vibrato", "", "Wide vibrato on the selected note."),
        new("Note.ArtificialHarmonic", CategoryEffects, "Artificial harmonic", "", "Artificial harmonic on the selected note."),
        new("Note.Tapping", CategoryEffects, "Tapping", "", "Tapped note."),
        new("Note.Slap", CategoryEffects, "Slap", "", "Slapped note."),
        new("Note.Pop", CategoryEffects, "Pop", "", "Popped note."),
        new("Note.TremoloPicking", CategoryEffects, "Tremolo picking", "", "Tremolo picking on the selected note."),
        new("Note.WahOpen", CategoryEffects, "Wah open", "", "Open wah on the selected note."),
        new("Note.WahClose", CategoryEffects, "Wah close", "", "Closed wah on the selected note."),
        new("Note.BrushDown", CategoryEffects, "Brush down", "", "Strum the chord with a downward brush stroke."),
        new("Note.BrushUp", CategoryEffects, "Brush up", "", "Strum the chord with an upward brush stroke."),
        new("Note.ArpeggioDown", CategoryEffects, "Arpeggio down", "", "Play the chord as a downward arpeggio."),
        new("Note.ArpeggioUp", CategoryEffects, "Arpeggio up", "", "Play the chord as an upward arpeggio."),
        new("File.Exit", CategoryFile, "Exit", "", "Close TabForge; the usual unsaved-changes prompt still applies."),
    };
}
