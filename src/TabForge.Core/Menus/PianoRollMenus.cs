// Owns: the piano roll's right-click menus as data: on a note or the selection (Clear loop inside the loop; cut, copy, paste, delete, duplicate;
//   quantize; mute; the Transpose and Velocity submenus; select all and select this pitch; Show in score mode) and on empty grid (paste, select
//   all, quantize all, legato, Show in score mode), the note colour modes, the ruler's rows, and the ids the rows carry.
// Does not own: opening the menu or running the rows (the app's PianoRollView), or the edits (PianoRollPresenter).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.

namespace TabForge.Menus;

/// <summary>What the piano roll's menu depends on: a note under the pointer or a selection, a clipboard, and whether the pointer is inside the loop.</summary>
public sealed record PianoRollMenuState(bool OnNotes, bool CanPaste, bool InsideLoop, bool HasNotes = true, string ColourMode = "Track", bool OnSection = false, bool Needle = true);

public static class PianoRollMenus
{
    public const string Cut = "PianoRoll.Cut", Copy = "PianoRoll.Copy", Paste = "PianoRoll.Paste", Delete = "PianoRoll.Delete", Duplicate = "PianoRoll.Duplicate",
        Quantize = "PianoRoll.Quantize", QuantizeSettings = "PianoRoll.QuantizeSettings", Mute = "PianoRoll.Mute",
        TransposeUp = "PianoRoll.TransposeUp", TransposeDown = "PianoRoll.TransposeDown", OctaveUp = "PianoRoll.OctaveUp", OctaveDown = "PianoRoll.OctaveDown",
        TransposeBy = "PianoRoll.TransposeBy", VelocitySet = "PianoRoll.VelocitySet", VelocityScale = "PianoRoll.VelocityScale", VelocityRamp = "PianoRoll.VelocityRamp",
        SelectAll = "PianoRoll.SelectAll", SelectPitch = "PianoRoll.SelectPitch", InvertSelection = "PianoRoll.InvertSelection", ShowInScoreId = "PianoRoll.ShowInScore", ClearLoop = "Transport.ClearLoopArea",
        Legato = "PianoRoll.Legato", Glue = "PianoRoll.Glue", Randomise = "PianoRoll.Randomise", Humanise = "PianoRoll.Humanise", Colours = "PianoRoll.Colours",
        MovePlayhead = "PianoRoll.MovePlayhead", LoopBar = "PianoRoll.LoopBar", LoopSection = "PianoRoll.LoopSection",
        PlaybackNeedle = "PianoRoll.PlaybackNeedle";

    public const string ShowInScore = "Show in score mode (this track)", NeedleSettingKey = "editing.pianoroll.needle";

    private static MenuSpec NeedleRow(bool on) =>
        new() { Id = PlaybackNeedle, Header = "Show playback needle", Checkable = true, Checked = on, SettingKey = NeedleSettingKey };

    private static MenuSpec Row(string id, string header, string shortcut = "", bool enabled = true) =>
        new() { Id = id, Header = header, Shortcut = shortcut, Enabled = enabled };

    /// <summary>The rows for a right-click: the note menu over a note or inside the selection, else the empty-grid menu.</summary>
    public static List<MenuSpec> Rows(PianoRollMenuState s) => s.OnNotes ? NoteRows(s) : EmptyRows(s);

    private static List<MenuSpec> NoteRows(PianoRollMenuState s)
    {
        var list = new List<MenuSpec>();
        if (s.InsideLoop) list.AddRange(new[] { Row(ClearLoop, "Clear loop", "Esc twice"), MenuSpec.Separator() });
        list.Add(Row(Cut, "Cut", "Ctrl+X"));
        list.Add(Row(Copy, "Copy", "Ctrl+C"));
        list.Add(Row(Paste, "Paste", "Ctrl+V", s.CanPaste));
        list.Add(Row(Delete, "Delete", "Del"));
        list.Add(Row(Duplicate, "Duplicate", "Ctrl+D"));
        list.Add(MenuSpec.Separator());
        list.Add(Row(Quantize, "Quantize", "Q"));
        list.Add(Row(QuantizeSettings, "Quantize settings…"));
        list.Add(Row(Mute, "Mute / unmute", "M"));
        list.Add(new MenuSpec
        {
            Header = "Transpose",
            Children = new List<MenuSpec>
            {
                Row(TransposeUp, "Up a semitone", "Up"), Row(TransposeDown, "Down a semitone", "Down"),
                Row(OctaveUp, "Up an octave", "Shift+Up"), Row(OctaveDown, "Down an octave", "Shift+Down"),
                MenuSpec.Separator(), Row(TransposeBy, "By…"),
            },
        });
        list.Add(new MenuSpec
        {
            Header = "Velocity",
            Children = new List<MenuSpec>
            {
                Row(VelocitySet, "Set…"), Row(VelocityScale, "Scale by %…"), Row(VelocityRamp, "Ramp (first → last)…"), Row(Randomise, "Randomise ±…"),
            },
        });
        list.Add(MenuSpec.Separator());
        list.Add(Row(Humanise, "Humanise timing…"));
        list.Add(Row(Legato, "Legato", "Ctrl+L"));
        list.Add(Row(Glue, "Glue same-pitch notes"));
        list.Add(MenuSpec.Separator());
        list.Add(Row(SelectAll, "Select all", "Ctrl+A"));
        list.Add(Row(InvertSelection, "Invert selection", "Ctrl+I"));
        list.Add(Row(SelectPitch, "Select all notes of this pitch"));
        list.Add(ColourRows(s.ColourMode));
        list.Add(NeedleRow(s.Needle));
        list.Add(MenuSpec.Separator());
        list.Add(Row(ShowInScoreId, ShowInScore));
        return list;
    }

    /// <summary>The note colour modes (radio): track colour (brighter = louder), velocity heat, pitch class or MIDI channel.</summary>
    private static MenuSpec ColourRows(string mode) => new()
    {
        Header = "Note colours",
        Children = new[] { ("Track", "Track colour (brighter = louder)"), ("Velocity", "By velocity"), ("Lane", "By lane (the lower lane's value)"), ("Pitch", "By pitch class"), ("Channel", "By MIDI channel") }
            .Select(c => new MenuSpec { Id = Colours, Arg = c.Item1, Header = c.Item2, Checkable = true, Radio = true, Checked = c.Item1 == mode, NoSetting = "a view choice of the piano roll" }).ToList(),
    };

    /// <summary>The ruler's right-click rows: move the playhead, loop the bar or the section under the pointer, and Clear loop inside the loop.</summary>
    public static List<MenuSpec> RulerRows(PianoRollMenuState s)
    {
        var list = new List<MenuSpec> { Row(MovePlayhead, "Move playhead here"), Row(LoopBar, "Loop this bar"), Row(LoopSection, "Loop this section", enabled: s.OnSection) };
        if (s.InsideLoop) list.AddRange(new[] { MenuSpec.Separator(), Row(ClearLoop, "Clear loop", "Esc twice") });
        return list;
    }

    private static List<MenuSpec> EmptyRows(PianoRollMenuState s)
    {
        var list = new List<MenuSpec>();
        if (s.InsideLoop) list.AddRange(new[] { Row(ClearLoop, "Clear loop", "Esc twice"), MenuSpec.Separator() });
        list.Add(Row(Paste, "Paste", "Ctrl+V", s.CanPaste));
        list.Add(Row(SelectAll, "Select all", "Ctrl+A", s.HasNotes));
        list.Add(Row(InvertSelection, "Invert selection", "Ctrl+I", s.HasNotes));
        list.Add(Row(Quantize, "Quantize all", "Q", s.HasNotes));
        list.Add(Row(Legato, "Legato all", "Ctrl+L", s.HasNotes));
        list.Add(ColourRows(s.ColourMode));
        list.Add(NeedleRow(s.Needle));
        list.Add(MenuSpec.Separator());
        list.Add(Row(ShowInScoreId, ShowInScore));
        return list;
    }
}
