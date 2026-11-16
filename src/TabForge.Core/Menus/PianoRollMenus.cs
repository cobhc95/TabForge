// Owns: the piano roll's right-click menus as data: on a note or the selection (Clear loop inside the loop; cut, copy, paste, delete, duplicate;
//   quantize; mute; the Transpose and Velocity submenus; select all and select this pitch; Show in score mode) and on empty grid (paste, select
//   all, quantize all, legato, Show in score mode), the note colour modes, the ruler's rows, the ids the rows carry and the ids of the key-only commands.
// Does not own: opening the menu or running the rows (the app's PianoRollView), or the edits (PianoRollPresenter).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.

using TabForge.Commands;
using TabForge.Services;

namespace TabForge.Menus;

/// <summary>What the piano roll's menu depends on: a note under the pointer or a selection, a clipboard, and whether the pointer is inside the loop.</summary>
public sealed record PianoRollMenuState(bool OnNotes, bool CanPaste, bool InsideLoop, bool HasNotes = true, string ColourMode = "Track", bool OnSection = false, bool Needle = true,
    Func<string, string>? Key = null);

public static class PianoRollMenus
{
    public const string Cut = "PianoRoll.Cut", Copy = "PianoRoll.Copy", Paste = "PianoRoll.Paste", Delete = "PianoRoll.Delete", Duplicate = PianoRollKeyIds.Duplicate,
        Quantize = PianoRollKeyIds.Quantize, QuantizeSettings = "PianoRoll.QuantizeSettings", Mute = PianoRollKeyIds.Mute,
        TransposeUp = "PianoRoll.TransposeUp", TransposeDown = "PianoRoll.TransposeDown", OctaveUp = "PianoRoll.OctaveUp", OctaveDown = "PianoRoll.OctaveDown",
        TransposeBy = "PianoRoll.TransposeBy", VelocitySet = "PianoRoll.VelocitySet", VelocityScale = "PianoRoll.VelocityScale", VelocityRamp = "PianoRoll.VelocityRamp",
        SelectAll = "PianoRoll.SelectAll", SelectPitch = "PianoRoll.SelectPitch", InvertSelection = PianoRollKeyIds.InvertSelection, ShowInScoreId = "PianoRoll.ShowInScore", ClearLoop = CommandIds.Transport.ClearLoopArea,
        Legato = PianoRollKeyIds.Legato, Glue = "PianoRoll.Glue", Randomise = "PianoRoll.Randomise", Humanise = "PianoRoll.Humanise", Colours = "PianoRoll.Colours",
        MovePlayhead = "PianoRoll.MovePlayhead", LoopBar = "PianoRoll.LoopBar", LoopSection = "PianoRoll.LoopSection",
        PlaybackNeedle = "PianoRoll.PlaybackNeedle";

    /// <summary>Piano roll commands that only a key runs (no menu row): the Transpose dialog, zoom to fit and the five tools.</summary>
    public const string TransposeDialog = PianoRollKeyIds.TransposeDialog, ZoomToFit = PianoRollKeyIds.ZoomToFit, EditTool = PianoRollKeyIds.EditTool, EraseTool = PianoRollKeyIds.EraseTool,
        SplitTool = PianoRollKeyIds.SplitTool, MuteTool = PianoRollKeyIds.MuteTool, GlueTool = PianoRollKeyIds.GlueTool;

    public const string ShowInScore = "Show in score mode (this track)", NeedleSettingKey = "editing.pianoroll.needle";

    private static MenuSpec NeedleRow(bool on) =>
        new() { Id = PlaybackNeedle, Header = "Show playback needle", Checkable = true, Checked = on, SettingKey = NeedleSettingKey };

    private static readonly HotkeySettings DefaultKeys = new();

    /// <summary>The live key text of a bindable piano roll command (the menu state's lookup, else the catalogue default).</summary>
    private static string Key(PianoRollMenuState s, string id) => s.Key?.Invoke(id) ?? HotkeyCatalog.DisplayAll(DefaultKeys, id);

    private static MenuSpec Row(string id, string header, string shortcut = "", bool enabled = true) =>
        new() { Id = id, Header = header, Shortcut = shortcut, Enabled = enabled };

    /// <summary>The rows for a right-click: the note menu over a note or inside the selection, else the empty-grid menu.</summary>
    public static List<MenuSpec> Rows(PianoRollMenuState s) => s.OnNotes ? NoteRows(s) : EmptyRows(s);

    private static List<MenuSpec> NoteRows(PianoRollMenuState s)
    {
        var list = new List<MenuSpec>();
        if (s.InsideLoop) list.AddRange(new[] { Row(ClearLoop, "Clear loop", FixedKeys.EscTwice), MenuSpec.Separator() });
        list.Add(Row(Cut, "Cut", FixedKeys.Cut));
        list.Add(Row(Copy, "Copy", FixedKeys.Copy));
        list.Add(Row(Paste, "Paste", FixedKeys.Paste, s.CanPaste));
        list.Add(Row(Delete, "Delete", FixedKeys.Delete));
        list.Add(Row(Duplicate, "Duplicate", Key(s, Duplicate)));
        list.Add(MenuSpec.Separator());
        list.Add(Row(Quantize, "Quantize", Key(s, Quantize)));
        list.Add(Row(QuantizeSettings, "Quantize settings…"));
        list.Add(Row(Mute, "Mute / unmute", Key(s, Mute)));
        list.Add(new MenuSpec
        {
            Header = "Transpose",
            Children = new List<MenuSpec>
            {
                Row(TransposeUp, "Up a semitone", FixedKeys.Up), Row(TransposeDown, "Down a semitone", FixedKeys.Down),
                Row(OctaveUp, "Up an octave", FixedKeys.ShiftUp), Row(OctaveDown, "Down an octave", FixedKeys.ShiftDown),
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
        list.Add(Row(Legato, "Legato", Key(s, Legato)));
        list.Add(Row(Glue, "Glue same-pitch notes"));
        list.Add(MenuSpec.Separator());
        list.Add(Row(SelectAll, "Select all", FixedKeys.SelectAll));
        list.Add(Row(InvertSelection, "Invert selection", Key(s, InvertSelection)));
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
        if (s.InsideLoop) list.AddRange(new[] { MenuSpec.Separator(), Row(ClearLoop, "Clear loop", FixedKeys.EscTwice) });
        return list;
    }

    private static List<MenuSpec> EmptyRows(PianoRollMenuState s)
    {
        var list = new List<MenuSpec>();
        if (s.InsideLoop) list.AddRange(new[] { Row(ClearLoop, "Clear loop", FixedKeys.EscTwice), MenuSpec.Separator() });
        list.Add(Row(Paste, "Paste", FixedKeys.Paste, s.CanPaste));
        list.Add(Row(SelectAll, "Select all", FixedKeys.SelectAll, s.HasNotes));
        list.Add(Row(InvertSelection, "Invert selection", Key(s, InvertSelection), s.HasNotes));
        list.Add(Row(Quantize, "Quantize all", Key(s, Quantize), s.HasNotes));
        list.Add(Row(Legato, "Legato all", Key(s, Legato), s.HasNotes));
        list.Add(ColourRows(s.ColourMode));
        list.Add(NeedleRow(s.Needle));
        list.Add(MenuSpec.Separator());
        list.Add(Row(ShowInScoreId, ShowInScore));
        return list;
    }
}
