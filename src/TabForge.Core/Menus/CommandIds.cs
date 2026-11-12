namespace TabForge.Menus;

// Owns: the names of the command ids that more than four files spell out (menu table, hotkey catalogue, left strip, transport bar, context menus, handlers). Every value is the id as the
//   settings and the hotkey bindings store it, so a constant is never renamed or re-valued.
// Does not own: the other command ids (still string literals in the files that register or list them), what a command does (the MainWindow* handler classes) or the key bindings (HotkeyCatalog).
// Tests: TestCommandIdsAreInTheCatalogue.
/// <summary>The command ids used in five or more files, by area: <c>CommandIds.Transport.Loop</c> is "Transport.Loop".</summary>
public static class CommandIds
{
    public static class App
    {
        public const string Preferences = "App.Preferences";
    }

    public static class Bar
    {
        public const string Delete = "Bar.Delete";
        public const string Duplicate = "Bar.Duplicate";
        public const string First = "Bar.First";
        public const string Insert = "Bar.Insert";
        public const string KeySignature = "Bar.KeySignature";
        public const string Last = "Bar.Last";
        public const string RepeatClose = "Bar.RepeatClose";
        public const string RepeatOpen = "Bar.RepeatOpen";
        public const string TimeSignature = "Bar.TimeSignature";
    }

    public static class Clip
    {
        public const string Copy = "Clip.Copy";
        public const string Cut = "Clip.Cut";
        public const string Delete = "Clip.Delete";
        public const string Duplicate = "Clip.Duplicate";
        public const string Paste = "Clip.Paste";
    }

    public static class Edit
    {
        public const string Copy = "Edit.Copy";
        public const string Cut = "Edit.Cut";
        public const string Paste = "Edit.Paste";
        public const string PasteSpecial = "Edit.PasteSpecial";
        public const string Redo = "Edit.Redo";
        public const string Undo = "Edit.Undo";
    }

    public static class File
    {
        public const string New = "File.New";
        public const string NewFromTemplate = "File.NewFromTemplate";
        public const string Open = "File.Open";
        public const string Render = "File.Render";
        public const string Save = "File.Save";
        public const string SaveAs = "File.SaveAs";
    }

    public static class Highlight
    {
        public const string Copy = "Highlight.Copy";
        public const string Cut = "Highlight.Cut";
        public const string Delete = "Highlight.Delete";
        public const string Paste = "Highlight.Paste";
    }

    public static class Note
    {
        public const string Accent = "Note.Accent";
        public const string Bend = "Note.Bend";
        public const string BrushDown = "Note.BrushDown";
        public const string BrushUp = "Note.BrushUp";
        public const string Dead = "Note.Dead";
        public const string Dot = "Note.Dot";
        public const string DoubleDot = "Note.DoubleDot";
        public const string Eighth = "Note.Eighth";
        public const string FadeIn = "Note.FadeIn";
        public const string Fermata = "Note.Fermata";
        public const string Ghost = "Note.Ghost";
        public const string Half = "Note.Half";
        public const string HammerPull = "Note.HammerPull";
        public const string LetRing = "Note.LetRing";
        public const string MoveStringDown = "Note.MoveStringDown";
        public const string MoveStringUp = "Note.MoveStringUp";
        public const string PalmMute = "Note.PalmMute";
        public const string PitchDown = "Note.PitchDown";
        public const string PitchUp = "Note.PitchUp";
        public const string Pop = "Note.Pop";
        public const string Quarter = "Note.Quarter";
        public const string RepeatBeat = "Note.RepeatBeat";
        public const string Sixteenth = "Note.Sixteenth";
        public const string SixtyFourth = "Note.SixtyFourth";
        public const string Slap = "Note.Slap";
        public const string Slide = "Note.Slide";
        public const string Staccato = "Note.Staccato";
        public const string Tapping = "Note.Tapping";
        public const string ThirtySecond = "Note.ThirtySecond";
        public const string Tie = "Note.Tie";
        public const string TremoloBar = "Note.TremoloBar";
        public const string TremoloPicking = "Note.TremoloPicking";
        public const string Triplet = "Note.Triplet";
        public const string Vibrato = "Note.Vibrato";
        public const string Whole = "Note.Whole";
    }

    public static class Range
    {
        public const string Delete = "Range.Delete";
    }

    public static class Section
    {
        public const string Add = "Section.Add";
        public const string Next = "Section.Next";
        public const string Previous = "Section.Previous";
    }

    public static class Tab
    {
        public const string Close = "Tab.Close";
    }

    public static class Tools
    {
        public const string ChordFinder = "Tools.ChordFinder";
        public const string GlobalTuning = "Tools.GlobalTuning";
        public const string Tuner = "Tools.Tuner";
    }

    public static class Track
    {
        public const string Add = "Track.Add";
    }

    public static class Transport
    {
        public const string ClearLoopArea = "Transport.ClearLoopArea";
        public const string Loop = "Transport.Loop";
        public const string PlayPause = "Transport.PlayPause";
        public const string Record = "Transport.Record";
        public const string Stop = "Transport.Stop";
    }

    public static class View
    {
        public const string BandView = "View.BandView";
        public const string CompactMode = "View.CompactMode";
        public const string InstrumentPanel = "View.InstrumentPanel";
        public const string LearnMode = "View.LearnMode";
        public const string Mixer = "View.Mixer";
        public const string ZoomIn = "View.ZoomIn";
        public const string ZoomOut = "View.ZoomOut";
    }
}
